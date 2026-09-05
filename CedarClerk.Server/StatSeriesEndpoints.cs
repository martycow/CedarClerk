using System.Security.Claims;
using System.Text;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public sealed record StatSource(string Id, string Kind, string Network, string Name, IReadOnlyList<string> Tracked, DateOnly? FirstDay);

public sealed record StatSeriesItem(
    string Id,
    IReadOnlyDictionary<string, IReadOnlyList<int>?> Values,
    IReadOnlyDictionary<string, int> Current,
    IReadOnlyDictionary<string, int> Delta,
    IReadOnlyList<DateOnly> PublishDays);

public sealed record StatAudienceEntry(string Code, int Views);

public sealed record StatAudience(IReadOnlyList<StatAudienceEntry> Countries, IReadOnlyList<StatAudienceEntry> Languages);

public sealed record StatSeriesResponse(
    string Zone,
    int RequestedDays,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<StatSource> Available,
    IReadOnlyList<StatSeriesItem> Series,
    StatAudience Audience);

/// <summary>
/// T-242/T-243 — every source of the account on one aligned axis (ADR-161 §2, on the server
/// since ADR-273), and the same matrix as a CSV. The two legacy per-source endpoints stay for the
/// post-details chart.
/// </summary>
public static class StatSeriesEndpoints
{
    public const int MinDays = 7;
    public const int MaxDays = 180;
    public const int DefaultDays = 30;

    public const string BlogSourceId = "blog";
    public const string ChannelPrefix = "channel:";
    public const string TargetPrefix = "target:";

    private static readonly IReadOnlyList<string> BlogTracked = [StatMetrics.ViewCount, StatMetrics.LikeCount, StatMetrics.CommentCount];
    private static readonly IReadOnlyList<string> ChannelTracked = [StatMetrics.MemberCount, StatMetrics.LikeCount, StatMetrics.CommentCount];
    private static readonly IReadOnlyList<string> BlueskyTracked = [StatMetrics.MemberCount, StatMetrics.LikeCount, StatMetrics.CommentCount];
    private static readonly IReadOnlyList<string> XTracked = [StatMetrics.MemberCount];

    public static void MapStatSeriesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stats/series", async (ClaimsPrincipal user, CedarDbContext db, int days = DefaultDays, string? sources = null) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!TryParseSources(sources, out var selected))
                return Results.BadRequest(new { error = ErrorMessages.UnknownStatSource });

            var result = await BuildAsync(db, uid, days, selected, DateTime.UtcNow);
            return Results.Ok(result.Response);
        }).RequireAuthorization();

        app.MapGet("/api/stats/series.csv", async (ClaimsPrincipal user, CedarDbContext db, int days = DefaultDays, string? sources = null) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!TryParseSources(sources, out var selected))
                return Results.BadRequest(new { error = ErrorMessages.UnknownStatSource });

            var result = await BuildAsync(db, uid, days, selected, DateTime.UtcNow);
            var from = result.Aligned.Days.Count > 0 ? result.Aligned.Days[0] : result.Today;
            var to = result.Aligned.Days.Count > 0 ? result.Aligned.Days[^1] : result.Today;
            // The BOM is already in the string; UTF8.GetBytes adds no preamble of its own.
            var bytes = Encoding.UTF8.GetBytes(StatSeriesCsv.Write(result.Aligned));
            return Results.File(bytes, "text/csv; charset=utf-8", $"cedar-stats-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
        }).RequireAuthorization();
    }

    /// <summary>
    /// The selected ids as written. False only for an id of a kind this endpoint does not know —
    /// an unknown or unowned guid is dropped later without a word, so nobody can probe for one.
    /// </summary>
    public static bool TryParseSources(string? sources, out IReadOnlyList<string> selected)
    {
        var ids = new List<string>();
        foreach (var raw in (sources ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var id = raw.ToLowerInvariant();
            var known = id == BlogSourceId
                || (id.StartsWith(ChannelPrefix) && Guid.TryParse(id[ChannelPrefix.Length..], out _))
                || (id.StartsWith(TargetPrefix) && Guid.TryParse(id[TargetPrefix.Length..], out _));
            if (!known)
            {
                selected = [];
                return false;
            }
            if (!ids.Contains(id)) ids.Add(id);
        }
        selected = ids;
        return true;
    }

    public sealed record BuildResult(StatSeriesResponse Response, StatSeries Aligned, DateOnly Today);

    public static async Task<BuildResult> BuildAsync(CedarDbContext db, string uid, int days, IReadOnlyList<string> selected, DateTime nowUtc)
    {
        days = Math.Clamp(days, MinDays, MaxDays);

        var zoneId = TimeZones.NormalizeOrDefault(
            await db.Users.Where(u => u.Id == uid).Select(u => u.TimeZoneId).FirstOrDefaultAsync());
        DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(DisplayTime.ToZone(utc, zoneId));
        var today = DayOf(nowUtc);

        await BlogEndpoints.EnsureTodayBlogSnapshotAsync(db, uid);

        // `available` is every source the account has, in the order the leaf strip shows them:
        // blog, then channels, then X/Bluesky targets.
        var available = new List<StatSource>();
        var blogFirst = await db.BlogStatSnapshots.Where(s => s.OwnerId == uid)
            .OrderBy(s => s.TakenAt).Select(s => (DateTime?)s.TakenAt).FirstOrDefaultAsync();
        available.Add(new StatSource(BlogSourceId, "blog", "blog", "Blog", BlogTracked, blogFirst is { } bf ? DayOf(bf) : null));

        var channels = await db.Channels.Where(c => c.OwnerId == uid)
            .OrderBy(c => c.Title).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Title }).ToListAsync();
        var channelFirsts = await db.ChannelStatSnapshots.Where(s => s.OwnerId == uid)
            .GroupBy(s => s.ChannelId)
            .Select(g => new { ChannelId = g.Key, First = g.Min(s => s.TakenAt) })
            .ToDictionaryAsync(g => g.ChannelId, g => g.First);
        foreach (var channel in channels)
        {
            available.Add(new StatSource(ChannelPrefix + channel.Id, "channel", PublishNetworks.Telegram, channel.Title, ChannelTracked,
                channelFirsts.TryGetValue(channel.Id, out var first) ? DayOf(first) : null));
        }

        var targets = await db.PublishTargets
            .Where(t => t.OwnerId == uid && t.IsActive && (t.Network == PublishNetworks.X || t.Network == PublishNetworks.Bluesky))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Network, t.DisplayName }).ToListAsync();
        var targetFirsts = await db.PublishTargetStatSnapshots.Where(s => s.OwnerId == uid)
            .GroupBy(s => s.TargetId)
            .Select(g => new { TargetId = g.Key, First = g.Min(s => s.TakenAt) })
            .ToDictionaryAsync(g => g.TargetId, g => g.First);
        foreach (var target in targets)
        {
            available.Add(new StatSource(TargetPrefix + target.Id, "target", target.Network, target.DisplayName,
                target.Network == PublishNetworks.X ? XTracked : BlueskyTracked,
                targetFirsts.TryGetValue(target.Id, out var first) ? DayOf(first) : null));
        }

        // Readings only for what is selected AND owned — an id that is not in `available` is
        // dropped here, whoever it belongs to.
        var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
        var readings = new List<SourceReadings>();
        var publishSince = nowUtc.AddDays(-(days + 1));
        foreach (var source in available.Where(s => selectedSet.Contains(s.Id)))
        {
            if (source.Id == BlogSourceId)
            {
                var rows = await db.BlogStatSnapshots.Where(s => s.OwnerId == uid).OrderBy(s => s.TakenAt)
                    .Select(s => new { s.TakenAt, s.ViewCount, s.LikeCount, s.CommentCount }).ToListAsync();
                var publishDays = await db.Drafts
                    .Where(d => d.OwnerId == uid && d.BlogPublishedAt != null && d.BlogPublishedAt >= publishSince)
                    .Select(d => d.BlogPublishedAt!.Value).ToListAsync();
                readings.Add(new SourceReadings(source.Id, source.Name, source.Tracked,
                    rows.Select(r => new StatReading(DayOf(r.TakenAt), new Dictionary<string, int>
                    {
                        [StatMetrics.ViewCount] = r.ViewCount,
                        [StatMetrics.LikeCount] = r.LikeCount,
                        [StatMetrics.CommentCount] = r.CommentCount,
                    })).ToList(),
                    publishDays.Select(DayOf).ToList()));
            }
            else if (source.Kind == "channel")
            {
                var channelId = Guid.Parse(source.Id[ChannelPrefix.Length..]);
                var rows = await db.ChannelStatSnapshots.Where(s => s.OwnerId == uid && s.ChannelId == channelId).OrderBy(s => s.TakenAt)
                    .Select(s => new { s.TakenAt, s.MemberCount, s.TelegramReactionCount, s.TelegramCommentCount }).ToListAsync();
                var publishDays = await db.ChannelPosts
                    .Where(p => p.OwnerId == uid && p.ChannelId == channelId && p.PublishedAt >= publishSince)
                    .Select(p => p.PublishedAt).ToListAsync();
                readings.Add(new SourceReadings(source.Id, source.Name, source.Tracked,
                    rows.Select(r => new StatReading(DayOf(r.TakenAt), new Dictionary<string, int>
                    {
                        [StatMetrics.MemberCount] = r.MemberCount,
                        // ADR-205 — the channel's own numbers, never the blog-attributed columns.
                        [StatMetrics.LikeCount] = r.TelegramReactionCount,
                        [StatMetrics.CommentCount] = r.TelegramCommentCount,
                    })).ToList(),
                    publishDays.Select(DayOf).ToList()));
            }
            else
            {
                var targetId = Guid.Parse(source.Id[TargetPrefix.Length..]);
                var rows = await db.PublishTargetStatSnapshots.Where(s => s.OwnerId == uid && s.TargetId == targetId).OrderBy(s => s.TakenAt)
                    .Select(s => new { s.TakenAt, s.FollowerCount, s.LikeCount, s.CommentCount }).ToListAsync();
                var publishDays = await db.PublishJobs
                    .Where(j => j.OwnerId == uid && j.TargetId == targetId && j.Status == PublishJobStatus.Succeeded
                        && j.FinishedAt != null && j.FinishedAt >= publishSince)
                    .Select(j => j.FinishedAt!.Value).ToListAsync();
                readings.Add(new SourceReadings(source.Id, source.Name, source.Tracked,
                    rows.Select(r =>
                    {
                        var values = new Dictionary<string, int> { [StatMetrics.MemberCount] = r.FollowerCount };
                        if (r.LikeCount is { } likes) values[StatMetrics.LikeCount] = likes;
                        if (r.CommentCount is { } comments) values[StatMetrics.CommentCount] = comments;
                        return new StatReading(DayOf(r.TakenAt), values);
                    }).ToList(),
                    publishDays.Select(DayOf).ToList()));
            }
        }

        var aligned = StatSeriesAligner.Window(readings, today, days) is { } window
            ? StatSeriesAligner.Align(readings, window.From, window.To)
            : StatSeries.Empty;

        // Blog geo over the requested window, always — the audience shelf is blog data whatever is
        // selected (ADR-097), and it is a sum over the period, not a running total.
        var geoSince = nowUtc.Date.AddDays(-(days - 1));
        var geoRows = await db.BlogViewGeoDailies
            .Where(v => v.OwnerId == uid && v.Day >= geoSince)
            .Select(v => new { v.Country, v.Language, v.ViewCount })
            .ToListAsync();
        var audience = new StatAudience(
            geoRows.GroupBy(r => r.Country).Select(g => new StatAudienceEntry(g.Key, g.Sum(r => r.ViewCount)))
                .OrderByDescending(e => e.Views).ThenBy(e => e.Code).ToList(),
            geoRows.GroupBy(r => r.Language).Select(g => new StatAudienceEntry(g.Key, g.Sum(r => r.ViewCount)))
                .OrderByDescending(e => e.Views).ThenBy(e => e.Code).ToList());

        var response = new StatSeriesResponse(
            zoneId,
            days,
            aligned.Days,
            available,
            aligned.Series.Select(s => new StatSeriesItem(s.Id, s.Values, s.Current, s.Delta, s.PublishDays)).ToList(),
            audience);
        return new BuildResult(response, aligned, today);
    }
}
