using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// Runs queued publications (T-090, ADR-081). Everything interesting here is about the two ways a
/// publish can go wrong that a naive "just retry" makes worse.
///
/// **A job is claimed before it sends.** Pending → Running is a conditional update, so a job that
/// the in-process runner already picked up cannot also be picked up by the sweeper.
///
/// **A job that was Running when the process died is never retried.** The request had left; whether
/// the network accepted it is unknowable from here, and a blind retry turns one post into two. It
/// becomes <see cref="PublishJobStatus.Unknown"/>, which is a status an author can act on.
/// </summary>
public class PublishJobRunner(
    IServiceScopeFactory scopes,
    ILogger<PublishJobRunner> logger,
    ProductAnalytics analytics)
{
    /// <summary>Retries only ever happen for failures that could not have posted; three is enough for a flaky link.</summary>
    public const int MaxAttempts = 3;

    /// <summary>How long a claimed job may run before it is presumed abandoned by a restart.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
    ];

    /// <summary>
    /// Status codes that mean the send never reached the network in a form it could have accepted,
    /// so trying again is safe. Everything else — a refusal, a rejected document — is permanent,
    /// and retrying it would only produce the same answer more slowly.
    /// </summary>
    private static bool IsRetryable(int statusCode) =>
        statusCode is StatusCodes.Status502BadGateway
                   or StatusCodes.Status503ServiceUnavailable
                   or StatusCodes.Status504GatewayTimeout
                   // A rate limit is the one 4xx that means "the same request, later" rather than
                   // "not this request".
                   or StatusCodes.Status429TooManyRequests;

    /// <summary>Kicks a job off now, without waiting for the sweeper — what makes publishing feel immediate.</summary>
    public void Kick(Guid jobId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RunOneAsync(jobId, kickSuccessor: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Publish job {JobId} crashed outside its own error handling", jobId);
            }
        });
    }

    /// <summary>Picks up whatever is due — after a restart, after a backoff, or after a missed kick.</summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var now = DateTime.UtcNow;

        // Abandoned first: a job left Running by a restart must be resolved, not re-run.
        var abandoned = await db.PublishJobs
            .Where(j => j.Status == PublishJobStatus.Running && j.StartedAt != null && j.StartedAt < now - AbandonedAfter)
            .ToListAsync(ct);
        foreach (var job in abandoned)
        {
            job.Status = PublishJobStatus.Unknown;
            job.FinishedAt = now;
            job.Error = "The server restarted while this was being sent — check the destination before publishing again.";
            logger.LogWarning("Publish job {JobId} was abandoned mid-send and is left as Unknown", job.Id);
        }
        if (abandoned.Count > 0) await db.SaveChangesAsync(ct);

        // Ordered by part before age: a thread's parts are created in the same millisecond, and
        // CreatedAt alone would leave their order to the database.
        var due = await db.PublishJobs
            .Where(j => j.Status == PublishJobStatus.Pending && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.PartIndex)
            .Select(j => j.Id)
            .Take(20)
            .ToListAsync(ct);

        // Sequentially, and without kicking successors: the loop is already ordered, and a
        // fire-and-forget kick racing it is how part 3 once went out while part 2 was still being
        // decided. The sweep is the deterministic path; the kick exists only for immediacy.
        foreach (var id in due)
            await RunOneAsync(id, kickSuccessor: false, ct);
    }

    private async Task RunOneAsync(Guid jobId, bool kickSuccessor, CancellationToken ct)
    {
        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var targets = scope.ServiceProvider.GetRequiredService<IEnumerable<IPublishTarget>>();

        var job = await db.PublishJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != PublishJobStatus.Pending) return;

        // T-106 — a thread goes out in order, and a part never goes out on its own. Waiting rather
        // than sending is the whole point: parts 5 and 6 arriving before 4 is worse than late.
        string? replyTo = null;
        string? rootRemoteId = null;
        if (job is { ThreadId: not null, PartIndex: > 0 })
        {
            var previous = await db.PublishJobs.FirstOrDefaultAsync(
                j => j.ThreadId == job.ThreadId && j.PartIndex == job.PartIndex - 1, ct);

            if (previous is null || previous.Status is PublishJobStatus.Failed or PublishJobStatus.Unknown)
            {
                // The thread broke earlier. Sending this part would leave a channel with a gap in
                // the middle of a document and no way to tell what is missing.
                job.Status = PublishJobStatus.Failed;
                job.FinishedAt = DateTime.UtcNow;
                job.Error = ErrorMessages.ThreadPartAbandoned(job.PartIndex);
                await db.SaveChangesAsync(ct);
                return;
            }

            if (previous.Status != PublishJobStatus.Succeeded) return;   // still running — the sweep comes back
            replyTo = previous.RemoteId;

            // ADR-094 — Bluesky's reply record wants the thread ROOT beside the parent.
            rootRemoteId = job.PartIndex == 1
                ? previous.RemoteId
                : (await db.PublishJobs.FirstOrDefaultAsync(
                    j => j.ThreadId == job.ThreadId && j.PartIndex == 0, ct))?.RemoteId;
        }

        // The claim. Concurrency here is one process and SQLite's write lock, so a conditional
        // save is enough — but it IS the thing keeping the kick and the sweeper from both sending.
        job.Status = PublishJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.Attempts++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return;
        }

        var part = job.PartCount > 1 ? new ThreadPartRef(job.PartIndex, job.PartCount, replyTo, rootRemoteId) : null;
        var result = await PostEndpoints.PublishToTargetAsync(
            job.DraftId, job.TargetId, job.OwnerId, db, targets, job.Language, logger, part: part, ct: ct);

        job.FinishedAt = DateTime.UtcNow;
        if (result.Success)
        {
            job.Status = PublishJobStatus.Succeeded;
            job.Error = null;
            // The string id, not the int: an X tweet id overflows int and a Bluesky id is an
            // at:// URI — both parsed to null here until ADR-094, which also left PublicUrl
            // permanently empty and the "open the post" link permanently hidden.
            job.RemoteId = result.RemoteId ?? result.MessageId?.ToString();
            job.PublicUrl = result.PublicUrl;
        }
        else if (IsRetryable(result.StatusCode) && job.Attempts < MaxAttempts)
        {
            job.Status = PublishJobStatus.Pending;
            job.StartedAt = null;
            job.FinishedAt = null;
            job.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(job.Attempts - 1, Backoff.Length - 1)];
            job.Error = result.Error;
            logger.LogWarning("Publish job {JobId} failed ({Error}) — retrying, attempt {Attempts}", job.Id, result.Error, job.Attempts);
        }
        else
        {
            job.Status = PublishJobStatus.Failed;
            job.Error = result.Error;
        }

        await db.SaveChangesAsync(ct);

        if (job.Status == PublishJobStatus.Succeeded)
        {
            // Explicit OwnerId rather than the tenant filter: the runner works through the queue in
            // a platform scope, where the filter is off (see .claude/rules/telegram-bot.md).
            //
            // "Earlier succeeded job" is what makes this the first publish, and it stays right for a
            // thread: parts 2..N each find part 1 behind them. TTFP is the metric on the other end
            // of it, and it is measured from AspNetUsers.CreatedAt in the provider, not here.
            var isFirst = !await db.PublishJobs.AnyAsync(
                j => j.OwnerId == job.OwnerId
                     && j.Id != job.Id
                     && j.Status == PublishJobStatus.Succeeded
                     && j.FinishedAt < job.FinishedAt, ct);

            var properties = new Dictionary<string, object>
            {
                ["network"] = job.TargetId,
                ["language"] = job.Language,
                ["threaded"] = job.PartCount > 1,
            };
            analytics.Track(job.OwnerId, Consts.Analytics.Events.PostPublished, properties);
            if (isFirst) analytics.Track(job.OwnerId, Consts.Analytics.Events.PostPublishedFirst, properties);
        }

        // The next part waits on this one, so it is kicked here rather than left to the sweeper —
        // otherwise an eight-part thread would take two minutes of doing nothing between messages.
        if (kickSuccessor && job.Status == PublishJobStatus.Succeeded
            && job.ThreadId is { } thread && job.PartIndex + 1 < job.PartCount)
        {
            var next = await db.PublishJobs.FirstOrDefaultAsync(
                j => j.ThreadId == thread && j.PartIndex == job.PartIndex + 1 && j.Status == PublishJobStatus.Pending, ct);
            if (next is not null) Kick(next.Id);
        }
    }
}
