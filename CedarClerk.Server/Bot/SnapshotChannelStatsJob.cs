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
public class SnapshotChannelStatsJob(CedarDbContext db, TelegramBotService bot, MediaPaths media, ILogger<SnapshotChannelStatsJob> logger) : IJob
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
                await ChannelAvatar.RefreshAsync(bot.Client, channel, media.Dir, logger);

                var draftIds = await db.ChannelPosts.Where(p => p.ChannelId == channel.Id)
                    .Select(p => p.DraftId).Distinct().ToListAsync();
                var viewCount = draftIds.Count == 0 ? 0 : await db.Drafts.Where(d => draftIds.Contains(d.Id)).SumAsync(d => d.ViewCount);
                var likeCount = draftIds.Count == 0 ? 0 : await db.Reactions.CountAsync(r => draftIds.Contains(r.DraftId) && r.Kind == "like");
                var commentCount = draftIds.Count == 0 ? 0 : await db.Comments.CountAsync(c => draftIds.Contains(c.DraftId));

                // ADR-205 — the channel's own numbers, summed over what the bot has seen happen to
                // its posts. Beside the blog attribution above, never instead of it.
                var telegram = await db.ChannelPosts.Where(p => p.ChannelId == channel.Id)
                    .GroupBy(p => 1)
                    .Select(g => new { Reactions = g.Sum(p => p.ReactionCount), Comments = g.Sum(p => p.CommentCount) })
                    .FirstOrDefaultAsync();

                db.ChannelStatSnapshots.Add(new ChannelStatSnapshot
                {
                    ChannelId = channel.Id,
                    MemberCount = count,
                    ViewCount = viewCount,
                    LikeCount = likeCount,
                    CommentCount = commentCount,
                    TelegramReactionCount = telegram?.Reactions ?? 0,
                    TelegramCommentCount = telegram?.Comments ?? 0,
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

        // Per-draft series (8.6). Every draft that is out somewhere — on the blog or in a channel —
        // gets one row a night. Drafts nobody can see are skipped: a flat line at zero for an
        // unpublished draft is noise in the table and a chart with nothing in it on screen.
        var publishedDraftIds = await db.Drafts.Where(d => d.IsBlogPublished).Select(d => d.Id).ToListAsync();
        var channelDraftIds = await db.ChannelPosts.Select(p => p.DraftId).Distinct().ToListAsync();
        var trackedIds = publishedDraftIds.Union(channelDraftIds).ToList();
        if (trackedIds.Count > 0)
        {
            try
            {
                var views = await db.Drafts.Where(d => trackedIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.ViewCount }).ToListAsync();
                var reactions = await db.Reactions.Where(r => trackedIds.Contains(r.DraftId))
                    .GroupBy(r => new { r.DraftId, r.Kind })
                    .Select(g => new { g.Key.DraftId, g.Key.Kind, Count = g.Count() }).ToListAsync();
                var comments = await db.Comments.Where(c => trackedIds.Contains(c.DraftId))
                    .GroupBy(c => c.DraftId)
                    .Select(g => new { DraftId = g.Key, Count = g.Count() }).ToListAsync();

                foreach (var draft in views)
                {
                    db.DraftStatSnapshots.Add(new DraftStatSnapshot
                    {
                        DraftId = draft.Id,
                        ViewCount = draft.ViewCount,
                        LikeCount = reactions.FirstOrDefault(r => r.DraftId == draft.Id && r.Kind == "like")?.Count ?? 0,
                        DislikeCount = reactions.FirstOrDefault(r => r.DraftId == draft.Id && r.Kind == "dislike")?.Count ?? 0,
                        CommentCount = comments.FirstOrDefault(c => c.DraftId == draft.Id)?.Count ?? 0,
                        TakenAt = now,
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to snapshot per-draft stats");
            }
        }

        if (channels.Count > 0 || blogOwnerIds.Count > 0 || trackedIds.Count > 0)
            await db.SaveChangesAsync();
    }
}
