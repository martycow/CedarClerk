using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

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
    ILogger<PublishJobRunner> logger)
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
                await RunOneAsync(jobId, CancellationToken.None);
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
        using var scope = scopes.CreateScope();
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

        var due = await db.PublishJobs
            .Where(j => j.Status == PublishJobStatus.Pending && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .Take(10)
            .ToListAsync(ct);

        foreach (var id in due)
            await RunOneAsync(id, ct);
    }

    private async Task RunOneAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var targets = scope.ServiceProvider.GetRequiredService<IEnumerable<IPublishTarget>>();

        var job = await db.PublishJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != PublishJobStatus.Pending) return;

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

        var result = await PostEndpoints.PublishToTargetAsync(
            job.DraftId, job.TargetId, job.OwnerId, db, targets, job.Language, logger, ct: ct);

        job.FinishedAt = DateTime.UtcNow;
        if (result.Success)
        {
            job.Status = PublishJobStatus.Succeeded;
            job.Error = null;
            job.RemoteId = result.MessageId?.ToString();
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
    }
}
