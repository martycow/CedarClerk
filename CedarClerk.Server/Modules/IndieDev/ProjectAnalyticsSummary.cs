using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

public static class ProjectAnalyticsSummary
{
    public const int Days = 7;

    /// <summary>
    /// Views, likes and Telegram reactions belong to the project's documents. Members, subscribers
    /// and the blog totals are the account's: channels and the blog are not per project. A null
    /// means the nightly snapshot has not recorded that figure yet.
    /// </summary>
    public sealed record Summary(
        int Days,
        int Views, int ViewsGrowth,
        int Likes, int LikesGrowth,
        int TelegramReactions,
        int? TelegramMembers, int TelegramMembersGrowth,
        int BlogSubscribers, int BlogSubscribersGrowth,
        int? BlogViews, int? BlogViewsGrowth);

    public static async Task<Summary> ForAsync(CedarDbContext db, string ownerId, Guid projectId, DateTime now, CancellationToken ct = default)
    {
        var since = now.AddDays(-Days);
        var drafts = db.Drafts.Where(d => d.ProjectId == projectId && d.OwnerId == ownerId);
        var draftIds = drafts.Select(d => d.Id);

        var current = await drafts.Select(d => new { d.Id, d.ViewCount }).ToListAsync(ct);
        // ViewCount only grows, so the largest value up to the window's start is the newest one,
        // and the smallest inside it stands in for a document whose history starts mid-window.
        var snapshots = await db.DraftStatSnapshots
            .Where(s => s.OwnerId == ownerId && draftIds.Contains(s.DraftId))
            .GroupBy(s => s.DraftId)
            .Select(g => new
            {
                DraftId = g.Key,
                Before = g.Where(s => s.TakenAt <= since).Max(s => (int?)s.ViewCount),
                Earliest = g.Min(s => (int?)s.ViewCount),
            })
            .ToDictionaryAsync(s => s.DraftId, ct);
        var views = current.Sum(d => d.ViewCount);
        var baseline = current.Sum(d => snapshots.TryGetValue(d.Id, out var s) ? Math.Min(d.ViewCount, s.Before ?? s.Earliest ?? 0) : 0);

        var likes = db.Reactions.Where(r => r.OwnerId == ownerId && r.Kind == "like" && draftIds.Contains(r.DraftId));
        var telegramReactions = await db.ChannelPosts
            .Where(p => p.OwnerId == ownerId && draftIds.Contains(p.DraftId))
            .SumAsync(p => (int?)p.ReactionCount, ct) ?? 0;

        var memberRows = await db.ChannelStatSnapshots.Where(s => s.OwnerId == ownerId)
            .GroupBy(s => s.ChannelId)
            .Select(g => g.OrderByDescending(s => s.TakenAt).Select(s => s.MemberCount).First())
            .ToListAsync(ct);
        var memberDays = await db.ChannelMemberDailies
            .Where(d => d.OwnerId == ownerId && d.Day >= since.Date)
            .Select(d => new { d.Joins, d.Leaves }).ToListAsync(ct);

        var subscribers = db.BlogSubscribers.Where(s => s.OwnerId == ownerId && s.ConfirmedAt != null);

        var blogNow = await db.BlogStatSnapshots.Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.TakenAt).Select(s => (int?)s.ViewCount).FirstOrDefaultAsync(ct);
        var blogThen = await db.BlogStatSnapshots.Where(s => s.OwnerId == ownerId && s.TakenAt <= since)
            .OrderByDescending(s => s.TakenAt).Select(s => (int?)s.ViewCount).FirstOrDefaultAsync(ct)
            ?? await db.BlogStatSnapshots.Where(s => s.OwnerId == ownerId)
                .OrderBy(s => s.TakenAt).Select(s => (int?)s.ViewCount).FirstOrDefaultAsync(ct);

        return new Summary(
            Days,
            views, views - baseline,
            await likes.CountAsync(ct), await likes.CountAsync(r => r.CreatedAt >= since, ct),
            telegramReactions,
            memberRows.Count == 0 ? null : memberRows.Sum(), memberDays.Sum(d => d.Joins - d.Leaves),
            await subscribers.CountAsync(ct), await subscribers.CountAsync(s => s.ConfirmedAt >= since, ct),
            blogNow, blogNow is null ? null : Math.Max(0, blogNow.Value - (blogThen ?? blogNow.Value)));
    }
}
