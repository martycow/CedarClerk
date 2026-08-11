using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

/// <summary>
/// Reading and writing <see cref="EntityLink"/> rows (T-141, extended by T-123).
///
/// Shared rather than private to one endpoint file: an asset links to a document, a task links to
/// documents, assets and other tasks, and every one of those does the same three things — order the
/// pair, look from both ends, refuse a duplicate. Two copies of that would drift, and the one that
/// drifted would produce two rows describing one fact.
/// </summary>
public static class ProjectLinks
{
    /// <summary>
    /// The ids of everything of <paramref name="wantedType"/> linked to one thing. Both columns are
    /// searched because <see cref="LinkTargets.Order"/> decides which side a pair lands on, and the
    /// caller neither knows nor should have to.
    /// </summary>
    public static async Task<List<Guid>> LinkedIdsAsync(
        CedarDbContext db, string ownerId, string type, Guid id, string wantedType)
    {
        var rows = await db.EntityLinks
            .Where(l => l.OwnerId == ownerId
                        && ((l.FromType == type && l.FromId == id && l.ToType == wantedType)
                            || (l.ToType == type && l.ToId == id && l.FromType == wantedType)))
            .Select(l => new { l.FromType, l.FromId, l.ToId })
            .ToListAsync();

        return rows.Select(r => r.FromType == wantedType ? r.FromId : r.ToId).ToList();
    }

    /// <summary>
    /// Everything linked to any of <paramref name="ids"/>, in one query. The board draws link chips
    /// on every card at once; asking per card would be one round trip per task.
    /// </summary>
    public static async Task<Dictionary<Guid, List<(string Type, Guid Id)>>> LinkedIdsForManyAsync(
        CedarDbContext db, string ownerId, string type, IReadOnlyCollection<Guid> ids)
    {
        var result = ids.ToDictionary(id => id, _ => new List<(string, Guid)>());
        if (ids.Count == 0) return result;

        var rows = await db.EntityLinks
            .Where(l => l.OwnerId == ownerId
                        && ((l.FromType == type && ids.Contains(l.FromId))
                            || (l.ToType == type && ids.Contains(l.ToId))))
            .Select(l => new { l.FromType, l.FromId, l.ToType, l.ToId })
            .ToListAsync();

        foreach (var r in rows)
        {
            // A task linked to another task matches on both sides at once, and both directions are
            // real: each of the two cards should show the other.
            if (r.FromType == type && result.TryGetValue(r.FromId, out var fromSide))
                fromSide.Add((r.ToType, r.ToId));
            if (r.ToType == type && result.TryGetValue(r.ToId, out var toSide))
                toSide.Add((r.FromType, r.FromId));
        }

        return result;
    }

    /// <summary>
    /// Links two things, doing nothing if the link already exists. Returns false only when the pair
    /// is the same thing twice — nothing else here can fail that the caller has not already checked.
    /// </summary>
    public static async Task<bool> AddAsync(
        CedarDbContext db, string ownerId, Guid projectId,
        string typeA, Guid idA, string typeB, Guid idB)
    {
        if (typeA == typeB && idA == idB) return false;

        var pair = LinkTargets.Order(typeA, idA, typeB, idB);

        // Ordered first, so linking the same pair from either end finds the existing row rather
        // than tripping the unique index.
        if (await db.EntityLinks.AnyAsync(l => l.FromType == pair.FromType && l.FromId == pair.FromId
                                               && l.ToType == pair.ToType && l.ToId == pair.ToId))
            return true;

        db.EntityLinks.Add(new EntityLink
        {
            OwnerId = ownerId,
            ProjectId = projectId,
            FromType = pair.FromType,
            FromId = pair.FromId,
            ToType = pair.ToType,
            ToId = pair.ToId,
        });
        await db.SaveChangesAsync();
        return true;
    }

    public static async Task<bool> RemoveAsync(
        CedarDbContext db, string ownerId, string typeA, Guid idA, string typeB, Guid idB)
    {
        var pair = LinkTargets.Order(typeA, idA, typeB, idB);
        var deleted = await db.EntityLinks
            .Where(l => l.OwnerId == ownerId && l.FromType == pair.FromType && l.FromId == pair.FromId
                        && l.ToType == pair.ToType && l.ToId == pair.ToId)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    /// <summary>
    /// Removes every link a thing has, for when the thing itself goes away. A link to a deleted
    /// task would otherwise stay on the other side's card forever, pointing at nothing.
    /// </summary>
    public static Task<int> RemoveAllForAsync(CedarDbContext db, string ownerId, string type, Guid id) =>
        db.EntityLinks
            .Where(l => l.OwnerId == ownerId
                        && ((l.FromType == type && l.FromId == id) || (l.ToType == type && l.ToId == id)))
            .ExecuteDeleteAsync();
}
