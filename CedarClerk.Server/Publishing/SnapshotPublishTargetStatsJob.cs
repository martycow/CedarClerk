using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace CedarClerk.Server.Publishing;

// T-241 — one reading a night per X/Bluesky account. Not a branch of SnapshotChannelStatsJob: that
// one returns before doing anything while the bot is down, and these networks never touch the bot.
[DisallowConcurrentExecution]
public class SnapshotPublishTargetStatsJob(
    CedarDbContext db,
    TenantProvider tenant,
    IHttpClientFactory httpFactory,
    XPublishTarget x,
    BlueskyPublishTarget bluesky,
    ILogger<SnapshotPublishTargetStatsJob> logger) : IJob
{
    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        tenant.UsePlatform();

        var targets = await db.PublishTargets
            .Where(t => t.IsActive && (t.Network == PublishNetworks.X || t.Network == PublishNetworks.Bluesky))
            .ToListAsync(ct);

        foreach (var target in targets)
        {
            PublishTargetStatSnapshot? snapshot = null;
            try
            {
                snapshot = await PublishTargetStats.TakeAsync(db, target, httpFactory, x, bluesky, logger, ct);
                if (snapshot is not null) db.PublishTargetStatSnapshots.Add(snapshot);
                // Saved per target so one account's failure — a refused write, a dead token — never
                // takes the other accounts' readings with it.
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to snapshot {Network} stats for target {TargetId} ({Name})",
                    target.Network, target.Id, target.DisplayName);
                if (snapshot is not null) db.Entry(snapshot).State = EntityState.Detached;
            }
        }
    }
}

// The reading itself, shared by the nightly job and the connect flows (a target's first row is
// taken the moment it is connected, as a Telegram channel's is). The caller adds and saves.
public static class PublishTargetStats
{
    public const string BlueskyPublicApi = "https://public.api.bsky.app";

    public static async Task<PublishTargetStatSnapshot?> TakeAsync(
        CedarDbContext db,
        PublishTarget target,
        IHttpClientFactory httpFactory,
        XPublishTarget x,
        BlueskyPublishTarget bluesky,
        ILogger logger,
        CancellationToken ct)
    {
        return target.Network switch
        {
            PublishNetworks.X => await TakeXAsync(target, httpFactory, x, logger, ct),
            PublishNetworks.Bluesky => await TakeBlueskyAsync(db, target, httpFactory, bluesky, logger, ct),
            _ => null,
        };
    }

    // Followers and the account's own post total only: per-tweet metrics are not on the free tier,
    // so likes/comments/reposts stay null and the tab says "not tracked" rather than zero.
    private static async Task<PublishTargetStatSnapshot?> TakeXAsync(
        PublishTarget target, IHttpClientFactory httpFactory, XPublishTarget x, ILogger logger, CancellationToken ct)
    {
        var credentials = await x.FreshCredentialsAsync(target, ct);
        if (credentials is null)
        {
            target.LastError = ErrorMessages.XReconnect;
            logger.LogWarning("X target {TargetId} has no usable credentials; skipping its reading", target.Id);
            return null;
        }

        var http = httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{XPublishTarget.ApiBase}/2/users/me?user.fields=public_metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            target.LastError = ErrorMessages.XReconnect;
            logger.LogWarning("X refused the stats read for target {TargetId} (401) — reconnect needed", target.Id);
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("X answered {Status} to the stats read for target {TargetId}: {Body}",
                (int)response.StatusCode, target.Id, await response.Content.ReadAsStringAsync(ct));
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
        var metrics = body?["data"]?["public_metrics"];
        if (metrics is null)
        {
            logger.LogWarning("X returned no public_metrics for target {TargetId}", target.Id);
            return null;
        }

        return new PublishTargetStatSnapshot
        {
            OwnerId = target.OwnerId,
            TargetId = target.Id,
            Network = PublishNetworks.X,
            FollowerCount = (int?)metrics["followers_count"] ?? 0,
            PostCount = (int?)metrics["tweet_count"] ?? 0,
        };
    }

    // Public XRPC, no session: the profile and the author feed are readable by anyone, and a
    // reading must not spend (or depend on) the stored app password.
    private static async Task<PublishTargetStatSnapshot?> TakeBlueskyAsync(
        CedarDbContext db, PublishTarget target, IHttpClientFactory httpFactory, BlueskyPublishTarget bluesky, ILogger logger, CancellationToken ct)
    {
        var handle = bluesky.ReadCredentials(target)?.Handle ?? target.DisplayName;
        var actor = Uri.EscapeDataString(target.RemoteId);
        var http = httpFactory.CreateClient();

        using var profileResponse = await http.GetAsync($"{BlueskyPublicApi}/xrpc/app.bsky.actor.getProfile?actor={actor}", ct);
        if (!profileResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Bluesky answered {Status} to getProfile for {Handle} (target {TargetId})",
                (int)profileResponse.StatusCode, handle, target.Id);
            return null;
        }
        var profile = await profileResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);

        var snapshot = new PublishTargetStatSnapshot
        {
            OwnerId = target.OwnerId,
            TargetId = target.Id,
            Network = PublishNetworks.Bluesky,
            FollowerCount = (int?)profile?["followersCount"] ?? 0,
            PostCount = (int?)profile?["postsCount"] ?? 0,
            LikeCount = 0,
            CommentCount = 0,
            RepostCount = 0,
        };

        // Explicit owner predicate: the job runs under the platform scope with no tenant filter.
        var remoteIds = await db.PublishJobs
            .Where(j => j.OwnerId == target.OwnerId && j.TargetId == target.Id
                && j.Status == PublishJobStatus.Succeeded && j.RemoteId != null)
            .Select(j => j.RemoteId!)
            .ToListAsync(ct);
        var ourUris = remoteIds.Select(r => BlueskyPublishTarget.ParseRef(r)?.Uri ?? r).ToHashSet(StringComparer.Ordinal);
        if (ourUris.Count == 0) return snapshot;

        using var feedResponse = await http.GetAsync(
            $"{BlueskyPublicApi}/xrpc/app.bsky.feed.getAuthorFeed?actor={actor}&limit=100&filter=posts_no_replies", ct);
        if (!feedResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Bluesky answered {Status} to getAuthorFeed for {Handle} (target {TargetId}); engagement left at zero",
                (int)feedResponse.StatusCode, handle, target.Id);
            return snapshot;
        }

        var feed = await feedResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct);
        foreach (var item in feed?["feed"]?.AsArray() ?? [])
        {
            var post = item?["post"];
            if (post?["uri"] is not { } uri || !ourUris.Contains(uri.GetValue<string>())) continue;
            snapshot.LikeCount += (int?)post["likeCount"] ?? 0;
            snapshot.CommentCount += (int?)post["replyCount"] ?? 0;
            snapshot.RepostCount += (int?)post["repostCount"] ?? 0;
        }
        return snapshot;
    }
}
