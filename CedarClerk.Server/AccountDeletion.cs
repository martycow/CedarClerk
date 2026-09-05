using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Removing an account and everything it owns.
///
/// Runs on a platform context, which is what makes it dangerous and what makes it possible: with
/// the tenant filters off, a delete written against the wrong column takes somebody else's work
/// with it. Every statement here names OwnerId explicitly for that reason.
///
/// <see cref="AdminAuditEntry"/> is deliberately left behind. It stores the actor's and target's
/// email as text precisely so it still reads once the rows it points at are gone, and an audit log
/// that loses the record of a deletion is not an audit log.
/// </summary>
public static class AccountDeletion
{
    /// <summary>
    /// Deletes the account, its rows and its media files. Returns false when there is no such
    /// account, which is not an error — the caller asked for a state that already holds.
    /// </summary>
    public static async Task<bool> DeleteAsync(CedarDbContext db, string ownerId, string? mediaDir,
        TenantOwnerCache.ForHosts hosts)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        if (user is null) return false;

        var files = await db.Assets.Where(a => a.OwnerId == ownerId)
            .Select(a => a.LocalPath).ToListAsync();

        // Rows first. If file removal fails halfway the account is still gone, and what is left is
        // unreferenced bytes on disk — recoverable. The reverse order could leave a live account
        // whose pictures have vanished.
        await db.ChannelPosts.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ChannelStatSnapshots.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.PublishTargetStatSnapshots.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Comments.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftGlossaryExclusions.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftRevisions.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftStatSeens.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftStatSnapshots.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftTargetTexts.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftTranslations.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.PollVotes.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.PostInvites.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.PostRegistrations.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Reactions.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();

        await db.AiUsages.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.AssetEntries.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.BlogStatSnapshots.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.BlogViewGeoDailies.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Builds.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.CreditEntries.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DocumentLinks.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.EntityLinks.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.FormPresets.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.GameTasks.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.GlossaryTerms.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.GlossaryTermUsages.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Payments.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.PublishJobs.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.PublishTargets.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ScheduledPosts.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ShowcaseFollowers.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ShowcaseStatDailies.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Sprints.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.CanvasItems.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.CanvasBoards.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ProjectMembers.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        // The other direction, and the one that is easy to miss: this account's memberships of other
        // people's projects. Left behind, they grant access to a user id nothing answers for.
        await db.ProjectMembers.Where(x => x.MemberUserId == ownerId).ExecuteDeleteAsync();
        await db.Assets.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Folders.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Series.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();

        // After their children, since both are pointed at by rows above.
        await db.Drafts.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Projects.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Channels.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        // Before the files, and after the rows: the subdomain must stop answering as soon as the
        // account behind it is gone, rather than serving an empty blog until the entry expires.
        if (user.TenantUsername is { } name) hosts.Forget(name);

        if (mediaDir is not null) DeleteFiles(mediaDir, files);
        return true;
    }

    private static void DeleteFiles(string mediaDir, IEnumerable<string> localPaths)
    {
        var root = Path.GetFullPath(mediaDir);

        foreach (var localPath in localPaths)
        {
            if (string.IsNullOrWhiteSpace(localPath)) continue;

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, localPath));
            }
            catch (ArgumentException) { continue; }

            // A stored path is data, and data is not trusted to stay inside the directory it is
            // supposed to describe.
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

            try
            {
                if (File.Exists(full)) File.Delete(full);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
