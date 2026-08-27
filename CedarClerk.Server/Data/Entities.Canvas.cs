namespace CedarClerk.Server;

// The reference board (T-301, ADR-218) — an endless wall a project's people put images, notes,
// frames and links on. Access to it is decided by ProjectMember (Entities.Collab.cs); the rows here
// carry the **project owner's** OwnerId whoever wrote them, so a member's write lands in the owner's
// tenant and the ordinary filter still covers every read.
//
// There is no CanvasPresence: cursors, selections and who is looking are in-memory in the hub and
// meaningless a second after the connection drops, so a row per cursor move would have been the
// heaviest write in the app in exchange for nothing.

/// <summary>
/// One board of a project. Deleted outright rather than archived — a board is a surface, not a
/// record of what happened, so it follows <see cref="Build"/>/<see cref="Sprint"/> instead of
/// <see cref="Project"/>.
/// </summary>
public class CanvasBoard
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The project owner, always — a member's board still belongs to the project.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>One of <see cref="CedarClerk.Core.CanvasBackgrounds"/>.</summary>
    public string Background { get; set; } = CedarClerk.Core.CanvasBackgrounds.Grid;

    /// <summary>May be a member rather than the owner.</summary>
    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Moved by an item write too, not only by a rename — the board list sorts by it.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string UpdatedByUserId { get; set; } = "";

    /// <summary>Last-writer-wins counter for the board row itself (name, background).</summary>
    public int Version { get; set; } = 1;
}

/// <summary>
/// One thing on a board.
///
/// <para>Geometry is columns rather than JSON because it is written on every drag and read by every
/// render, and a query cannot sort inside a string. Kind-specific data is the opposite case: a
/// frame's title and an image's natural size are not comparable, so a column per kind would be four
/// nullable columns with three of them always null — hence one <see cref="Payload"/> document,
/// replaced whole and never patched field by field.</para>
///
/// <para>Conflicts are pure last-writer-wins: the server accepts every write from a permitted writer
/// and bumps <see cref="Version"/>. It never refuses a stale one — the counter exists so a client can
/// drop an echo older than what it already holds, not so the server can lock (ADR-218).</para>
/// </summary>
public class CanvasItem
{
    /// <summary>
    /// Client-generated and sent with the add, so an optimistic item and its echo are the same item
    /// and no id has to be swapped once the server answers. A repeated add of the same id is the
    /// same item, not a second one — a retried send after a reconnect must not double the note.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The project owner, whoever drew this.</summary>
    public string OwnerId { get; set; } = default!;

    /// <summary>Denormalised from the board: account deletion and project deletion sweep by project,
    /// never board by board.</summary>
    public Guid ProjectId { get; set; }

    public Guid BoardId { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.CanvasItemKinds"/>. Immutable after creation.</summary>
    public string Kind { get; set; } = CedarClerk.Core.CanvasItemKinds.Note;

    /// <summary>World coordinates — unbounded, pixels at zoom 1.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Degrees, -180..180.</summary>
    public double Rotation { get; set; }

    /// <summary>Server-assigned <c>max(Z) + 1</c> per board. Ties under concurrency are fine: render
    /// order is (Z, CreatedAt, Id), which is total and stable everywhere.</summary>
    public int Z { get; set; }

    /// <summary>A token name from the bench palette; empty means the kind's own default.</summary>
    public string Color { get; set; } = "";

    /// <summary>
    /// The kind's own data as JSON, at most
    /// <see cref="CedarClerk.Core.Consts.Canvas.PayloadMaxChars"/> characters. Shapes and their
    /// validation live in <c>CedarClerk.Core.CanvasPayload</c>; unknown properties are rejected
    /// rather than ignored, since a payload that silently loses a field is worse than a refusal.
    /// </summary>
    public string Payload { get; set; } = "{}";

    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedByUserId { get; set; } = "";

    /// <summary>See the type's summary — a counter for the client to order echoes by, not a lock.</summary>
    public int Version { get; set; } = 1;
}
