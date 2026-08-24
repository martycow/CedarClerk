using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// ADR-204 — a file follows the document that uses it. The rule is one sentence and it is here
// rather than at its three call sites, because "which project owns this image" must have the same
// answer whether a document was just attached, just created, or swept from the backlog.
public static class AssetFiling
{
    /// <summary>
    /// Files every unfiled asset the given drafts reference into <paramref name="projectId"/>
    /// (null detaches nothing — a draft leaving a project leaves its images where they are, since
    /// another document may still be using them). Returns how many rows moved.
    /// </summary>
    public static async Task<int> FileAsync(CedarDbContext db, string ownerId, Guid projectId, IReadOnlyCollection<Guid> draftIds)
    {
        if (draftIds.Count == 0) return 0;

        var bodies = await db.Drafts.AsNoTracking()
            .Where(d => draftIds.Contains(d.Id) && d.OwnerId == ownerId)
            .Select(d => d.CedarJson)
            .ToListAsync();

        // A translation is the same post in another language and carries the same pictures, but it
        // may also carry one the primary never had — a screenshot of the localized build.
        var translations = await db.DraftTranslations.AsNoTracking()
            .Where(t => draftIds.Contains(t.DraftId))
            .Select(t => t.CedarJson)
            .ToListAsync();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in bodies.Concat(translations))
            foreach (var path in CedarPackage.FindReferencedMediaPathsSafe(json))
                names.Add(path);

        if (names.Count == 0) return 0;

        // Only the unfiled ones: the first project to claim a file keeps it, so a picture shared
        // between two projects does not follow whichever document was edited last.
        var candidates = await db.Assets
            .Where(a => a.OwnerId == ownerId && a.ProjectId == null)
            .ToListAsync();

        var moved = 0;
        foreach (var asset in candidates)
        {
            if (!names.Contains(asset.LocalPath)) continue;
            asset.ProjectId = projectId;
            moved++;
        }

        return moved;
    }

    /// <summary>One document, for the moment its project changes.</summary>
    public static Task<int> FileAsync(CedarDbContext db, string ownerId, Guid projectId, Guid draftId) =>
        FileAsync(db, ownerId, projectId, new[] { draftId });

    /// <summary>Every document a project holds — the sweep behind the button (ADR-204).</summary>
    public static async Task<int> SweepProjectAsync(CedarDbContext db, string ownerId, Guid projectId)
    {
        var draftIds = await db.Drafts.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && d.ProjectId == projectId)
            .Select(d => d.Id)
            .ToListAsync();

        return await FileAsync(db, ownerId, projectId, draftIds);
    }
}
