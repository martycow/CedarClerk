namespace CedarClerk.Core;

// What an EntityLink can point at (T-141). Strings, like PublishNetworks: a new kind of linkable
// thing should be a constant and a UI, not a migration.
public static class LinkTargets
{
    public const string Document = "document";
    public const string Asset = "asset";
    public const string Task = "task";

    // T-126 — documents attach to a released version through a link; tasks use a column instead
    // (ADR-112), because "which build did this ship in" is one answer worth filtering by.
    public const string Build = "build";

    public static readonly IReadOnlyList<string> All = [Asset, Build, Document, Task];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    // A fixed order so "this asset and that document" is one row however it was created: without it,
    // linking A→B and later B→A makes two rows for one fact and no unique index can tell them apart.
    // By type name then id — arbitrary but stable, which is all that is required.
    public static (string FromType, Guid FromId, string ToType, Guid ToId) Order(
        string typeA, Guid idA, string typeB, Guid idB)
    {
        var byType = string.CompareOrdinal(typeA, typeB);
        var swap = byType > 0 || (byType == 0 && idA.CompareTo(idB) > 0);
        return swap ? (typeB, idB, typeA, idA) : (typeA, idA, typeB, idB);
    }
}
