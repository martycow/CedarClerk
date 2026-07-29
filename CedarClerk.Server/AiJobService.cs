using System.Collections.Concurrent;

namespace CedarClerk.Server;

public enum AiJobStatus { Pending, Running, Completed, Failed }

public class AiJob
{
    public required Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public AiJobStatus Status { get; set; } = AiJobStatus.Pending;
    public object? Result { get; set; }
    public string? Error { get; set; }
    public int ErrorStatusCode { get; set; } = StatusCodes.Status502BadGateway;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public CancellationTokenSource Cts { get; } = new();
}

public record AiJobOutcome(object? Result, string? Error, int ErrorStatusCode)
{
    public static AiJobOutcome Ok(object result) => new(result, null, StatusCodes.Status200OK);
    public static AiJobOutcome Fail(string error, int statusCode) => new(null, error, statusCode);
}

// ADR-058-follow-up (29.07.2026) — auto-translate/AI-edit used to be one HTTP request held open
// for the whole Anthropic call, through Cloudflare Tunnel. For a large document that call can
// legitimately run past Cloudflare's own edge-to-origin timeout, which then returns its own 502
// to the browser — even though this server goes on to finish and save the result seconds later.
// Confirmed directly, not theorized: a real ~360-line/114KB translation was saved to the DB by
// this server, then the user (seeing no response in the UI) deleted it and retried, assuming it
// had failed. The fix: the slow part now runs as a background job; the client polls
// GET /api/ai-jobs/{id} with short, cheap requests instead of holding one connection open — no
// single request through the tunnel needs to outlive Cloudflare's own timeout, whatever it is.
//
// In-memory only, deliberately — jobs are transient and single-process. A redeploy/restart mid-job
// just means the client's next poll gets a 404 and reports a clean failure, same as any crash;
// this is not something worth persisting to the database for.
public class AiJobService(ILogger<AiJobService> logger)
{
    private readonly ConcurrentDictionary<Guid, AiJob> _jobs = new();
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    // Same day (29.07.2026): a real job ran past 14 minutes with no error anywhere — the
    // provider's own AnthropicClient.Timeout (Consts.Anthropic.RequestTimeout, 600s) never fired.
    // Root cause turned out to be unrelated (adaptive thinking's default "high" effort burning
    // enormous latency on a huge document — see AnthropicTranslationProvider.cs), but relying
    // solely on the SDK's own Timeout property to bound a call turned out not to be trustworthy in
    // practice. hardTimeout is a second, independent backstop via CancellationTokenSource.CancelAfter
    // — enforced by this service directly, regardless of what any given provider's HTTP client does
    // or doesn't honor internally.
    public Guid Start(string ownerId, Func<CancellationToken, Task<AiJobOutcome>> work, TimeSpan? hardTimeout = null)
    {
        Prune();

        var job = new AiJob { Id = Guid.NewGuid(), OwnerId = ownerId };
        if (hardTimeout is { } timeout) job.Cts.CancelAfter(timeout);
        _jobs[job.Id] = job;

        _ = RunAsync(job, work);

        return job.Id;
    }

    private async Task RunAsync(AiJob job, Func<CancellationToken, Task<AiJobOutcome>> work)
    {
        job.Status = AiJobStatus.Running;
        try
        {
            var outcome = await work(job.Cts.Token);
            if (outcome.Error is not null)
            {
                job.Status = AiJobStatus.Failed;
                job.Error = outcome.Error;
                job.ErrorStatusCode = outcome.ErrorStatusCode;
                logger.LogWarning("AI job {JobId} for {OwnerId} failed: {Error}", job.Id, job.OwnerId, outcome.Error);
            }
            else
            {
                job.Status = AiJobStatus.Completed;
                job.Result = outcome.Result;
            }
        }
        catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
        {
            // Either a user-initiated cancel (Cancel() below) or the hardTimeout backstop firing —
            // both land here identically; not worth distinguishing since a poller that's still
            // watching gets a clean "Cancelled" either way. Logged (unlike the old, silent version
            // of this method) so a hardTimeout firing is at least visible in journalctl afterward.
            job.Status = AiJobStatus.Failed;
            job.Error = "Cancelled";
            job.ErrorStatusCode = 499;
            logger.LogWarning("AI job {JobId} for {OwnerId} was cancelled or hit its hard timeout", job.Id, job.OwnerId);
        }
        catch (Exception ex)
        {
            job.Status = AiJobStatus.Failed;
            job.Error = $"Unexpected error: {ex.Message}";
            job.ErrorStatusCode = StatusCodes.Status500InternalServerError;
            logger.LogError(ex, "AI job {JobId} for {OwnerId} threw unexpectedly", job.Id, job.OwnerId);
        }
    }

    public AiJob? Get(Guid id, string ownerId)
    {
        Prune();
        return _jobs.TryGetValue(id, out var job) && job.OwnerId == ownerId ? job : null;
    }

    public bool Cancel(Guid id, string ownerId)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.OwnerId != ownerId) return false;
        job.Cts.Cancel();
        return true;
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var (key, job) in _jobs)
        {
            if (job.Status is AiJobStatus.Completed or AiJobStatus.Failed && job.CreatedAt < cutoff)
                _jobs.TryRemove(key, out _);
        }
    }
}
