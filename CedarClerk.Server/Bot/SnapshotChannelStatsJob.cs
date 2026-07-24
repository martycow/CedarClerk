using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server;

/// <summary>
/// A job which is used to collect statistics about channels
/// </summary>
[DisallowConcurrentExecution]
public class SnapshotChannelStatsJob(CedarDbContext db, TelegramBotService bot, ILogger<SnapshotChannelStatsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!bot.IsRunning) 
            return;

        var channels = await db.Channels.ToListAsync();
        var now = DateTime.UtcNow;

        foreach (var channel in channels)
        {
            try
            {
                var count = await bot.Client.GetChatMemberCount(new ChatId(channel.TelegramChatId));

                var draftIds = await db.ChannelPosts.Where(p => p.ChannelId == channel.Id)
                    .Select(p => p.DraftId).Distinct().ToListAsync();
                var viewCount = draftIds.Count == 0 ? 0 : await db.Drafts.Where(d => draftIds.Contains(d.Id)).SumAsync(d => d.ViewCount);
                var likeCount = draftIds.Count == 0 ? 0 : await db.Reactions.CountAsync(r => draftIds.Contains(r.DraftId) && r.Kind == "like");
                var commentCount = draftIds.Count == 0 ? 0 : await db.Comments.CountAsync(c => draftIds.Contains(c.DraftId));

                db.ChannelStatSnapshots.Add(new ChannelStatSnapshot
                {
                    ChannelId = channel.Id,
                    MemberCount = count,
                    ViewCount = viewCount,
                    LikeCount = likeCount,
                    CommentCount = commentCount,
                    TakenAt = now,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to snapshot member count for channel {ChannelId} ({Title})", channel.Id, channel.Title);
            }
        }

        // Blog totals are channel-agnostic (a view on the blog isn't tied to any one Telegram
        // channel) — one snapshot row per owner who has at least one blog-published draft,
        // summing across ALL of that owner's drafts rather than joining through ChannelPost.
        var blogOwnerIds = await db.Drafts.Where(d => d.IsBlogPublished).Select(d => d.OwnerId).Distinct().ToListAsync();
        foreach (var ownerId in blogOwnerIds)
        {
            try
            {
                var draftIds = await db.Drafts.Where(d => d.OwnerId == ownerId).Select(d => d.Id).ToListAsync();
                var viewCount = await db.Drafts.Where(d => d.OwnerId == ownerId).SumAsync(d => d.ViewCount);
                var likeCount = await db.Reactions.CountAsync(r => draftIds.Contains(r.DraftId) && r.Kind == "like");
                var commentCount = await db.Comments.CountAsync(c => draftIds.Contains(c.DraftId));

                db.BlogStatSnapshots.Add(new BlogStatSnapshot
                {
                    OwnerId = ownerId,
                    ViewCount = viewCount,
                    LikeCount = likeCount,
                    CommentCount = commentCount,
                    TakenAt = now,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to snapshot blog stats for owner {OwnerId}", ownerId);
            }
        }

        if (channels.Count > 0 || blogOwnerIds.Count > 0)
            await db.SaveChangesAsync();
    }
}
