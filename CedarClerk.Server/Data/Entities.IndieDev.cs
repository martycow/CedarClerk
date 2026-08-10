namespace CedarClerk.Server;

// Entities of the indie-gamedev module (Phase 13, ADR-101). A second file rather than more rows in
// Entities.cs: the "one flat file" convention exists to avoid a file per entity, and a file per
// module does not violate it — it is also what lets the module be read (or deleted) in one piece.
//
// The schema itself stays shared, deliberately: a second DbContext over the same SQLite file would
// mean two independent Database.Migrate() calls on startup, and the context boundary would fall
// exactly across the Draft/ApplicationUser links the module exists for. See ADR-101.

/// <summary>
/// A game, or whatever else a body of work is called — the container documents live in (ADR-102).
///
/// A project always holds at least one document, and that is enforced at the endpoint rather than
/// in the schema (ADR-103): as a constraint it would be violated by its own first INSERT, since
/// neither row can reference the other before both exist.
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public ApplicationUser? Owner { get; set; }

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// One of <see cref="CedarClerk.Core.ProjectTypes"/>. Decides which document the project is
    /// created with, and nothing else after that — a project is not locked out of any document type
    /// by how it started.
    /// </summary>
    public string ProjectType { get; set; } = CedarClerk.Core.ProjectTypes.FullGame;

    /// <summary>
    /// A /media/... path from the ordinary asset upload — same whitelist, same quota, same public
    /// serving as ApplicationUser.AvatarUrl and GlossaryTerm.ImageUrl. Null = no cover.
    /// </summary>
    public string? CoverUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Archived, not deleted: the project drops out of the default list while its documents keep
    /// their ProjectId. Null = active.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// T-122 — the folder whose contents are indexed for this project, as an absolute path on the
    /// machine running the server. Null = no folder chosen yet, which is what the asset screen's
    /// pick-a-folder state means.
    ///
    /// One root per project rather than a list: "Change folder" in the design replaces the root and
    /// re-indexes, and a per-row copy of the root would repeat the same string across tens of
    /// thousands of rows (narrowing of ADR-107's sketch, which put RootPath on the entry).
    /// </summary>
    public string? AssetRootPath { get; set; }

    /// <summary>When the last completed scan finished. Null = never scanned.</summary>
    public DateTime? AssetsIndexedAt { get; set; }
}

/// <summary>
/// One indexed file (T-122, ADR-107) — a path and metadata. **The bytes are never copied.** This is
/// deliberately not <see cref="Asset"/>: that one is media uploaded into <c>CEDAR_DATA_DIR</c>,
/// counted against the plan's storage quota and served publicly over <c>/media/*</c>. A game
/// project's asset weighs gigabytes, lives in the engine's own folder, changes without Cedar Clerk
/// ever knowing, and must never become publicly reachable.
/// </summary>
public class AssetEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ProjectId { get; set; }

    /// <summary>Path relative to <see cref="Project.AssetRootPath"/>, with '/' separators.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>The last segment of RelativePath, stored so search and sort do not parse on read.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Lowercase, no dot. Empty for a file without one.</summary>
    public string Extension { get; set; } = "";

    /// <summary>One of <see cref="CedarClerk.Core.AssetKinds"/> — a guess from the extension.</summary>
    public string Kind { get; set; } = CedarClerk.Core.AssetKinds.Other;

    public long SizeBytes { get; set; }

    /// <summary>The file's own last-write time, not ours — what the author will recognise.</summary>
    public DateTime ModifiedAt { get; set; }

    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When a scan first failed to find this file. **Missing is not deleted** — an unplugged
    /// external drive would otherwise erase an entire index, and the row is what lets the UI say
    /// "not found at path" instead of quietly forgetting the file ever existed. Cleared when a
    /// later scan finds it again.
    /// </summary>
    public DateTime? MissingSince { get; set; }
}
