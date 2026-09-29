using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Platform contexts bypass tenant filters: every delete must scope its owner explicitly.
// Retain AdminAuditEntry so account deletion cannot erase its audit record.
public static class AccountDeletion
{
    public static async Task<bool> DeleteAsync(CedarDbContext db, string ownerId, string? mediaDir,
        TenantOwnerCache.ForHosts hosts)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        if (user is null) return false;

        var files = await db.Assets.Where(a => a.OwnerId == ownerId)
            .Select(a => a.LocalPath).ToListAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Delete rows before files so a partial failure cannot remove a live account's media.
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
        // Memberships in other owners' projects must also lose access.
        await db.ProjectMembers.Where(x => x.MemberUserId == ownerId).ExecuteDeleteAsync();
        await db.TeamMembers.Where(x => x.OwnerId == ownerId || x.MemberUserId == ownerId).ExecuteDeleteAsync();
        await db.Teams.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Assets.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Folders.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Series.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();

        // After their children, since both are pointed at by rows above.
        await db.Drafts.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Projects.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Channels.Where(x => x.OwnerId == ownerId).ExecuteDeleteAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // Invalidate the subdomain before file cleanup, which can fail independently.
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

            // Stored paths are untrusted and must remain inside the media directory.
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
