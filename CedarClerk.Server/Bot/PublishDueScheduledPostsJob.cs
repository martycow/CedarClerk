using CedarClerk.Server.Publishing;
using Microsoft.EntityFrameworkCore;
using Quartz;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Server;

/// <summary>
/// A job that checks if schedule posts should be posted
/// </summary>
[DisallowConcurrentExecution]
public class PublishDueScheduledPostsJob(CedarDbContext db, TenantProvider tenant, IEnumerable<IPublishTarget> targets, ILogger<PublishDueScheduledPostsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // Runs on a timer, not in a request: there is no Host and no signed-in user, and the
        // work is over every owner's rows at once.
        tenant.UsePlatform();

        var now = DateTime.UtcNow;
        var due = await db.ScheduledPosts
            .Where(p => p.Status == "Pending" && p.ScheduledAtUtc <= now)
            .ToListAsync();

        foreach (var post in due)
        {
            // ADR-099 — a target id addresses any network; a row without one predates the column
            // and can only be a Telegram chat. Published inline rather than through the publish
            // queue on purpose: nothing here is waiting on an HTTP request, and a direct call is
            // what keeps this row's Sent/Failed status the network's real answer.
            var result = post.TargetId is { } targetId
                ? await PostEndpoints.PublishToTargetAsync(post.DraftId, targetId, post.OwnerId, db, targets, post.Language, logger)
                : await PostEndpoints.PublishAsync(post.DraftId, post.ChatId, post.OwnerId, db, targets, post.Format, post.Language, logger);
            if (result.Success)
            {
                post.Status = "Sent";
                post.MessageId = result.MessageId;
            }
            else
            {
                post.Status = "Failed";
                post.Error = result.Error;
                logger.LogWarning("Scheduled post {Id} failed: {Error}", post.Id, result.Error);
            }
        }

        if (due.Count > 0)
            await db.SaveChangesAsync();
    }
}
