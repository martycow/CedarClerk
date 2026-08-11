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
}
