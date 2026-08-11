namespace CedarClerk.Core;

/// <summary>
/// What an <c>EntityLink</c> can point at (T-141). A string rather than an enum for the same reason
/// <c>PublishNetworks</c> is one: a new kind of linkable thing should be a constant and a UI, not a
/// migration.
/// </summary>
public static class LinkTargets
{
    public const string Document = "document";
    public const string Asset = "asset";
    public const string Task = "task";

    /// <summary>T-126 — a released version. Documents attach to one; tasks use a column instead
    /// (ADR-112), because "which build did this ship in" is a single answer worth filtering by.</summary>
    public const string Build = "build";

    public static readonly IReadOnlyList<string> All = [Asset, Build, Document, Task];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    /// <summary>
    /// Puts a pair in a fixed order so that "this asset and that document" is one row however it
    /// was created. Without it, linking A→B and later B→A makes two rows describing one fact, and
    /// no unique index can tell them apart.
    ///
    /// Ordered by type name, then by id — arbitrary but stable, which is all that is required.
    /// </summary>
    public static (string FromType, Guid FromId, string ToType, Guid ToId) Order(
        string typeA, Guid idA, string typeB, Guid idB)
    {
        var byType = string.CompareOrdinal(typeA, typeB);
        var swap = byType > 0 || (byType == 0 && idA.CompareTo(idB) > 0);
        return swap ? (typeB, idB, typeA, idA) : (typeA, idA, typeB, idB);
    }
}
