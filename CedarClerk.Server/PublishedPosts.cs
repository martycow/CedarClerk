using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Where an owner's posts actually went (11.08.2026).
///
/// The URLs were always stored — every job row carries the one the network answered with — but
/// nothing outside the editor's own progress checklist ever read them, so closing that modal lost
/// the link for good. Marty published a 25-part X thread and could not find a single link to it
/// afterwards, which is what this exists to fix.
///
/// A separate class rather than a lambda in the endpoint because the "newest wins" rule below is
/// the part worth pinning with a test.
/// </summary>
public static class PublishedPosts
{
    public record PublishedPost(
        Guid DraftId, string Network, string Language, Guid TargetId,
        string PublicUrl, string? RemoteId, int PartCount, DateTime? FinishedAt);

    /// <summary>
    /// One row per place a version was published, newest first.
    ///
    /// **Part 0 only**: a thread's public address is its head, and twenty-five links to one thread
    /// is not a list of posts, it is a list of replies.
    ///
    /// **Newest wins per (draft, network, language, account)**: republishing replaces the post as
    /// far as a reader is concerned, so five attempts would otherwise be five links to one tab.
    /// </summary>
    public static async Task<List<PublishedPost>> LatestAsync(CedarDbContext db, string ownerId, CancellationToken ct = default)
    {
        var rows = await db.PublishJobs
            .Where(j => j.OwnerId == ownerId
                        && j.Status == PublishJobStatus.Succeeded
                        && j.PartIndex == 0
                        && j.PublicUrl != null)
            .OrderByDescending(j => j.FinishedAt)
            .Select(j => new PublishedPost(
                j.DraftId, j.Network, j.Language, j.TargetId,
                j.PublicUrl!, j.RemoteId, j.PartCount, j.FinishedAt))
            .ToListAsync(ct);

        return rows
            .GroupBy(r => (r.DraftId, r.Network, r.Language, r.TargetId))
            .Select(g => g.First())
            .OrderByDescending(r => r.FinishedAt)
            .ToList();
    }

    public const string BlogNetwork = "blog";

    /// <summary>
    /// One thing that went out: a Telegram send, a succeeded job on another network, or the blog
    /// publication. <see cref="Scheduled"/> marks a send a Sent <see cref="ScheduledPost"/> already
    /// stands for, so a calendar drawing both does not draw it twice.
    /// </summary>
    public record PublishEvent(
        Guid DraftId, string DraftTitle, string Network, string? TargetName,
        DateTime PublishedAt, string? PublicUrl, int PartCount, bool Scheduled);

    // A thread reaches the channel as one message per part, seconds apart.
    private static readonly TimeSpan ThreadWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Every publication of the owner's documents, newest first, narrowed to one project's
    /// documents when one is named.
    ///
    /// **Telegram comes from <see cref="ChannelPost"/>**, which holds every send; the draft's own
    /// LastTelegram* columns hold the latest one only. Telegram jobs are skipped for the same
    /// reason: the send they made is already a ChannelPost.
    /// </summary>
    public static async Task<List<PublishEvent>> EventsAsync(
        CedarDbContext db, string ownerId, Guid? project = null, CancellationToken ct = default)
    {
        var drafts = await db.Drafts
            .Where(d => d.OwnerId == ownerId && !d.IsTemplate && (project == null || d.ProjectId == project))
            .Select(d => new { d.Id, d.Title, d.IsBlogPublished, d.BlogPublishedAt, d.BlogSlug })
            .ToDictionaryAsync(d => d.Id, ct);
        var events = new List<PublishEvent>();

        var channels = await db.Channels.Where(c => c.OwnerId == ownerId)
            .Select(c => new { c.Id, c.Title, c.Username })
            .ToDictionaryAsync(c => c.Id, ct);
        var sentBySchedule = (await db.ScheduledPosts
                .Where(p => p.OwnerId == ownerId && p.Status == "Sent" && p.MessageId != null
                            && p.Network == PublishNetworks.Telegram)
                .Select(p => new { p.DraftId, MessageId = p.MessageId!.Value })
                .ToListAsync(ct))
            .Select(p => (p.DraftId, p.MessageId))
            .ToHashSet();
        var sends = await db.ChannelPosts.Where(p => p.OwnerId == ownerId)
            .OrderBy(p => p.PublishedAt).ThenBy(p => p.TelegramMessageId)
            .Select(p => new { p.DraftId, p.ChannelId, p.TelegramMessageId, p.PublishedAt })
            .ToListAsync(ct);
        foreach (var group in sends.Where(s => drafts.ContainsKey(s.DraftId)).GroupBy(s => (s.DraftId, s.ChannelId)))
        {
            var parts = new List<(int MessageId, DateTime At)>();
            void Flush()
            {
                if (parts.Count == 0) return;
                channels.TryGetValue(group.Key.ChannelId, out var channel);
                var head = parts[0];
                events.Add(new PublishEvent(
                    group.Key.DraftId, drafts[group.Key.DraftId].Title, PublishNetworks.Telegram, channel?.Title,
                    head.At,
                    string.IsNullOrEmpty(channel?.Username) ? null : $"https://t.me/{channel.Username}/{head.MessageId}",
                    parts.Count,
                    parts.Any(part => sentBySchedule.Contains((group.Key.DraftId, part.MessageId)))));
                parts.Clear();
            }

            foreach (var send in group)
            {
                if (parts.Count > 0 && send.PublishedAt - parts[^1].At > ThreadWindow) Flush();
                parts.Add((send.TelegramMessageId, send.PublishedAt));
            }
            Flush();
        }

        var targetNames = await db.PublishTargets.Where(t => t.OwnerId == ownerId)
            .ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);
        var jobs = await db.PublishJobs
            .Where(j => j.OwnerId == ownerId
                        && j.Status == PublishJobStatus.Succeeded
                        && j.PartIndex == 0
                        && j.FinishedAt != null
                        && j.Network != PublishNetworks.Telegram)
            .Select(j => new { j.DraftId, j.Network, j.TargetId, j.PublicUrl, j.PartCount, FinishedAt = j.FinishedAt!.Value })
            .ToListAsync(ct);
        events.AddRange(jobs.Where(j => drafts.ContainsKey(j.DraftId)).Select(j => new PublishEvent(
            j.DraftId, drafts[j.DraftId].Title, j.Network, targetNames.GetValueOrDefault(j.TargetId),
            j.FinishedAt, j.PublicUrl, j.PartCount, false)));

        events.AddRange(drafts.Values
            .Where(d => d.IsBlogPublished && d.BlogPublishedAt != null)
            .Select(d => new PublishEvent(d.Id, d.Title, BlogNetwork, null, d.BlogPublishedAt!.Value, null, 1, false)));

        return events.OrderByDescending(e => e.PublishedAt).ToList();
    }
}
