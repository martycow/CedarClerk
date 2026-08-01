using CedarClerk.Server.Publishing;
using Quartz;

namespace CedarClerk.Server;

/// <summary>
/// The backstop behind <see cref="PublishJobRunner.Kick"/> (T-090): picks up jobs whose kick was
/// lost to a restart, whose backoff has elapsed, or which were left Running by a process that died.
///
/// Every 15 seconds rather than every minute like the scheduled-post job: this one stands between
/// an author pressing Publish and anything at all happening if the in-process kick was missed, and
/// a minute of silence there reads as "it didn't work".
/// </summary>
[DisallowConcurrentExecution]
public class RunPublishJobsJob(PublishJobRunner runner, ILogger<RunPublishJobsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await runner.SweepAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            // A sweep that throws must not kill the trigger — the next one is 15 seconds away.
            logger.LogError(ex, "Publish job sweep failed");
        }
    }
}
