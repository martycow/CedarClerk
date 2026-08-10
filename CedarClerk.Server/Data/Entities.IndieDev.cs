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
}
