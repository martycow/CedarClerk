using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

public static class ProjectDeletion
{
    public sealed record Counts(int Documents, int Assets);

    public static async Task<Counts> CountsAsync(CedarDbContext db, string ownerId, Guid projectId) => new(
        await db.Drafts.CountAsync(d => d.ProjectId == projectId && d.OwnerId == ownerId),
        await db.Assets.CountAsync(a => a.ProjectId == projectId && a.OwnerId == ownerId));

    /// <summary>
    /// Null when the project is not the caller's. Otherwise the media files the deletion orphaned,
    /// for the caller to remove once the transaction has committed.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> DeleteAsync(CedarDbContext db, string ownerId, Guid id)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId);
        if (project is null) return null;

        await using var deletion = await db.Database.BeginTransactionAsync();
        IReadOnlyList<string> files = [];
        if (id == DocumentProjects.PersonalId(ownerId))
            files = await DeleteContentsAsync(db, ownerId, id);
        else
            await MoveContentsToPersonalAsync(db, ownerId, id);

        // None of these has a navigation property, so EF cascades none of them.
        await db.AssetEntries.Where(a => a.ProjectId == id && a.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.GameTasks.Where(t => t.ProjectId == id && t.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.EntityLinks.Where(l => l.ProjectId == id && l.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Builds.Where(b => b.ProjectId == id && b.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.Sprints.Where(sp => sp.ProjectId == id && sp.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ShowcaseFollowers.Where(f => f.ProjectId == id && f.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.ShowcaseStatDailies.Where(st => st.ProjectId == id && st.OwnerId == ownerId).ExecuteDeleteAsync();
        // Items go by project rather than by board: sweeping board by board would leave the items
        // of a board that was already gone, which is why CanvasItem carries ProjectId at all.
        await db.CanvasItems.Where(i => i.ProjectId == id && i.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.CanvasBoards.Where(b => b.ProjectId == id && b.OwnerId == ownerId).ExecuteDeleteAsync();
        // A membership outliving its project would keep granting access to an id nothing answers for.
        await db.ProjectMembers.Where(m => m.ProjectId == id && m.OwnerId == ownerId).ExecuteDeleteAsync();

        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        await deletion.CommitAsync();
        return files;
    }

    private static async Task MoveContentsToPersonalAsync(CedarDbContext db, string ownerId, Guid id)
    {
        var drafts = db.Drafts.Where(d => d.ProjectId == id && d.OwnerId == ownerId);
        var assets = db.Assets.Where(a => a.ProjectId == id && a.OwnerId == ownerId);
        var terms = db.GlossaryTerms.Where(t => t.ProjectId == id && t.OwnerId == ownerId);
        // ADR-319 — Personal comes back only when there is something to put in it.
        if (!await drafts.AnyAsync() && !await assets.AnyAsync() && !await terms.AnyAsync()) return;

        var personalId = await DocumentProjects.PersonalAsync(db, ownerId);
        await db.SaveChangesAsync();
        await drafts.ExecuteUpdateAsync(s => s.SetProperty(d => d.ProjectId, personalId));
        await assets.ExecuteUpdateAsync(s => s.SetProperty(a => a.ProjectId, personalId));
        await terms.ExecuteUpdateAsync(s => s.SetProperty(t => t.ProjectId, personalId));
    }

    private static async Task<IReadOnlyList<string>> DeleteContentsAsync(CedarDbContext db, string ownerId, Guid id)
    {
        var doomed = await db.Drafts.Where(d => d.ProjectId == id && d.OwnerId == ownerId)
            .Select(d => d.Id).ToListAsync();

        // A child filed in another project survives its parent as a root (ADR-128).
        await db.Drafts
            .Where(d => d.OwnerId == ownerId && d.ProjectId != id && d.ParentDraftId != null && doomed.Contains(d.ParentDraftId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ParentDraftId, (Guid?)null));
        await db.Drafts.Where(d => d.ProjectId == id && d.OwnerId == ownerId).ExecuteDeleteAsync();
        foreach (var draftId in doomed)
        {
            await DraftDeletion.CascadeAsync(db, ownerId, draftId);
            await ProjectLinks.RemoveAllForAsync(db, ownerId, LinkTargets.Document, draftId);
        }

        // A file a surviving document still shows is unfiled rather than removed: the same rule the
        // asset library's own delete follows (ADR-127).
        var survivors = await db.Drafts.AsNoTracking().Where(d => d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.CedarJson }).ToListAsync();
        var survivorIds = survivors.Select(d => d.Id).ToList();
        var translations = await db.DraftTranslations.AsNoTracking()
            .Where(t => survivorIds.Contains(t.DraftId)).Select(t => t.CedarJson).ToListAsync();
        var inUse = survivors.Select(d => d.CedarJson).Concat(translations)
            .SelectMany(CedarPackage.FindReferencedMediaPathsSafe)
            .ToHashSet(StringComparer.Ordinal);

        var files = new List<string>();
        var assets = await db.Assets.Where(a => a.ProjectId == id && a.OwnerId == ownerId).ToListAsync();
        foreach (var asset in assets)
        {
            if (inUse.Contains(asset.LocalPath))
            {
                asset.ProjectId = null;
                continue;
            }
            await ProjectLinks.RemoveAllForAsync(db, ownerId, LinkTargets.Attachment, asset.Id);
            var publicUrl = $"/media/{asset.LocalPath}";
            await db.Projects.Where(p => p.OwnerId == ownerId && p.CoverUrl == publicUrl)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CoverUrl, (string?)null));
            await db.Projects.Where(p => p.OwnerId == ownerId && p.BannerUrl == publicUrl)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.BannerUrl, (string?)null));
            files.Add(asset.LocalPath);
            if (asset.TelegramLocalPath is not null) files.Add(asset.TelegramLocalPath);
            db.Assets.Remove(asset);
        }

        await db.GlossaryTerms.Where(t => t.ProjectId == id && t.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ProjectId, (Guid?)null));
        await db.SaveChangesAsync();
        return files;
    }
}
