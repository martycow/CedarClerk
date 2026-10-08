using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Owner-level publishing rhythm (T-166/T-244): weekly publish counts and the streak they form.
/// A "publish" is any of the three ways content leaves the desk — a Telegram channel post, a
/// succeeded publish-queue job, a blog publication — bucketed by ISO week in UTC.
/// </summary>
public static class StatsInsightsEndpoints
{
    public static void MapStatsInsightsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stats/publishing", async (ClaimsPrincipal user, CedarDbContext db, Guid? project = null) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return Results.Ok(PublishingStreaks.Compute(await PublishDatesAsync(db, uid, project), DateTime.UtcNow));
        }).RequireAuthorization();
    }

    /// <summary>Every publish instant of the owner, narrowed to one project's documents when one is named.</summary>
    public static async Task<List<DateTime>> PublishDatesAsync(CedarDbContext db, string uid, Guid? project)
    {
        var channelDates = await db.ChannelPosts
            .Where(p => p.OwnerId == uid
                        && (project == null || db.Drafts.Any(d => d.Id == p.DraftId && d.ProjectId == project)))
            .Select(p => p.PublishedAt)
            .ToListAsync();
        var jobDates = await db.PublishJobs
            .Where(j => j.OwnerId == uid && j.Status == PublishJobStatus.Succeeded && j.FinishedAt != null
                        && (project == null || db.Drafts.Any(d => d.Id == j.DraftId && d.ProjectId == project)))
            .Select(j => j.FinishedAt!.Value)
            .ToListAsync();
        var blogDates = await db.Drafts
            .Where(d => d.OwnerId == uid && d.BlogPublishedAt != null && (project == null || d.ProjectId == project))
            .Select(d => d.BlogPublishedAt!.Value)
            .ToListAsync();
        return [.. channelDates, .. jobDates, .. blogDates];
    }
}

public sealed record PublishingWeek(DateTime WeekStartUtc, int Publishes);

public sealed record PublishingStreakResult(
    int CurrentStreakWeeks, int LongestStreakWeeks, IReadOnlyList<PublishingWeek> Weeks);

public static class PublishingStreaks
{
    public const int WeeksServed = 26;

    /// <summary>Monday 00:00 UTC of the ISO week the instant falls in.</summary>
    public static DateTime WeekStartUtc(DateTime utc)
    {
        var date = utc.Date;
        return DateTime.SpecifyKind(date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), DateTimeKind.Utc);
    }

    public static PublishingStreakResult Compute(
        IEnumerable<DateTime> publishEventsUtc, DateTime nowUtc, int weeksServed = WeeksServed)
    {
        var byWeek = publishEventsUtc.GroupBy(WeekStartUtc).ToDictionary(g => g.Key, g => g.Count());
        var currentWeek = WeekStartUtc(nowUtc);

        var weeks = new List<PublishingWeek>(weeksServed);
        for (var i = weeksServed - 1; i >= 0; i--)
        {
            var start = currentWeek.AddDays(-7 * i);
            weeks.Add(new PublishingWeek(start, byWeek.GetValueOrDefault(start)));
        }

        // A current week with nothing in it yet does not break the run — it just has not earned
        // its mark; the count then anchors on last week.
        var anchor = byWeek.ContainsKey(currentWeek) ? currentWeek : currentWeek.AddDays(-7);
        var current = 0;
        for (var week = anchor; byWeek.ContainsKey(week); week = week.AddDays(-7)) current++;

        var longest = 0;
        var run = 0;
        DateTime? previous = null;
        foreach (var week in byWeek.Keys.Order())
        {
            run = previous is { } p && week == p.AddDays(7) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = week;
        }

        return new PublishingStreakResult(current, longest, weeks);
    }
}
