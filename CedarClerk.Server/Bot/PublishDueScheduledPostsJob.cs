using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace CedarClerk.Server.Bot;

[DisallowConcurrentExecution]
public class PublishDueScheduledPostsJob(CedarDbContext db, TelegramBotService bot, IConfiguration cfg, ILogger<PublishDueScheduledPostsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = DateTime.UtcNow;
        var due = await db.ScheduledPosts
            .Where(p => p.Status == "Pending" && p.ScheduledAtUtc <= now)
            .ToListAsync();

        foreach (var post in due)
        {
            var result = await PostEndpoints.PublishAsync(post.DraftId, post.ChatId, post.OwnerId, db, bot, cfg);
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
