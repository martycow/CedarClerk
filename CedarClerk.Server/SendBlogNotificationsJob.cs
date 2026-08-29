using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace CedarClerk.Server;

/// <summary>
/// The notify queue's backstop (Wave 1 item 7): picks up Pending rows whose kick never ran —
/// a restart between SaveChanges and the fire-and-forget task — and fails Sending rows that were
/// interrupted mid-burst rather than re-mailing a list that may be half done.
/// </summary>
[DisallowConcurrentExecution]
public class SendBlogNotificationsJob(
    BlogSubscriberNotifier notifier,
    CedarDbContext db,
    TenantProvider tenant,
    ILogger<SendBlogNotificationsJob> logger) : IJob
{
    private const int BatchSize = 20;

    public async Task Execute(IJobExecutionContext context)
    {
        // Runs on a timer, not in a request — the work spans every owner's rows.
        tenant.UsePlatform();

        var cutoff = DateTime.UtcNow - BlogSubscriberNotifier.AbandonedAfter;
        var abandoned = await db.BlogNotifyJobs
            .Where(j => j.Status == BlogNotifyJobStatus.Sending && j.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BlogNotifyJobStatus.Failed)
                .SetProperty(j => j.Error, "The server restarted while this was being sent — some subscribers may already have the mail."));
        if (abandoned > 0)
            logger.LogWarning("{Count} blog notify job(s) were abandoned mid-send and are left as Failed", abandoned);

        var due = await db.BlogNotifyJobs
            .Where(j => j.Status == BlogNotifyJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var id in due)
            await notifier.RunOneAsync(id, context.CancellationToken);
    }
}
