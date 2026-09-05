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
    /// <summary>
    /// T-358 — the team whose members may act on this project, or null for a project only its owner
    /// and its per-project invitees reach. A team is the owner's own, so this never crosses tenants:
    /// the project and the team have the same <c>OwnerId</c> by construction.
    /// </summary>
    public Guid? TeamId { get; set; }

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
    /// ADR-243 — the fixed public category used by Discovery. It has no effect until the owner
    /// opts in and the Project has a live Showcase.
    /// </summary>
    public string DiscoveryCategory { get; set; } = CedarClerk.Core.DiscoveryCategories.Other;

    /// <summary>
    /// A /media/... path from the ordinary asset upload — same whitelist, same quota, same public
    /// serving as ApplicationUser.AvatarUrl and GlossaryTerm.ImageUrl. Null = no cover.
    /// </summary>
    public string? CoverUrl { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.ProjectEngines"/>, or empty for unset. Separate from
    /// <see cref="PressEngine"/>, which is the press page's free text.</summary>
    public string Engine { get; set; } = "";

    /// <summary>
    /// Comma-delimited keys from <see cref="CedarClerk.Core.ProjectPlatforms"/>, in that list's order,
    /// no spaces (<c>"windows,switch"</c>). Not JSON: the keys carry no commas, so <c>Split(',')</c>
    /// round-trips and a SQLite <c>LIKE</c> can filter on one — the same shape as ShowcaseLinks and
    /// Draft.Tags.
    /// </summary>
    public string TargetPlatforms { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Archived, not deleted: the project drops out of the default list while its documents keep
    /// their ProjectId. Null = active.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// T-122 — the folder whose contents are indexed for this project, as an absolute path on
    /// <see cref="AssetRootMachineId"/>. Null = no folder chosen yet, which is what the asset
    /// screen's pick-a-folder state means.
    ///
    /// One root per project rather than a list: "Change folder" in the design replaces the root and
    /// re-indexes, and a per-row copy of the root would repeat the same string across tens of
    /// thousands of rows (narrowing of ADR-107's sketch, which put RootPath on the entry).
    /// </summary>
    public string? AssetRootPath { get; set; }

    /// <summary>
    /// ADR-117 — which machine holds the folder above. A stable id the desktop shell generates once
    /// and keeps in <c>%APPDATA%\CedarClerk\machine.json</c>.
    ///
    /// **This is what makes "is this a file or a fingerprint?" answerable.** Since the index is
    /// pushed up from a machine and read back from anywhere, every asset screen has to decide whether
    /// the bytes are within reach — and the only honest way to decide is to compare the machine that
    /// indexed them against the machine asking. A browser has no machine at all, so it always gets
    /// the fingerprint answer, which is correct rather than merely safe.
    ///
    /// Null on projects indexed before ADR-117: their root came from a machine nobody recorded, so
    /// "unknown machine" is the truth about them.
    /// </summary>
    public string? AssetRootMachineId { get; set; }

    /// <summary>The machine's own name, for saying "the files are on MARTY-PC" rather than a GUID.</summary>
    public string? AssetRootMachineName { get; set; }

    /// <summary>When the last completed scan finished. Null = never scanned.</summary>
    public DateTime? AssetsIndexedAt { get; set; }

    /// <summary>
    /// T-159 (ADR-134/245) — the public Project page's slug on the blog host
    /// (<c>/showcase/{slug}</c>), globally
    /// unique. Null = no public page, and the page answers 404. Archiving the project hides the
    /// page the same way.
    /// </summary>
    public string? ShowcaseSlug { get; set; }

    /// <summary>Store links, one `Label|https://url` per line. A wishlist button is a Steam link
    /// with a label, not a mechanism (ADR-134).</summary>
    public string ShowcaseLinks { get; set; } = "";

    /// <summary>Gallery images, one `/media/...` path per line, at most
    /// <see cref="CedarClerk.Core.Consts.Showcase.GalleryMaxImages"/>. Uploaded assets, never
    /// <see cref="AssetEntry"/> rows: an indexed file is a fingerprint of somebody's disk and has
    /// no bytes to serve (ADR-134, narrowed by ADR-216).</summary>
    public string ShowcaseGallery { get; set; } = "";

    /// <summary>The ordered safe-block composition for the public Showcase. Empty uses the
    /// canonical default so existing Projects need no data rewrite.</summary>
    public string ShowcaseBlocksJson { get; set; } = "";

    /// <summary>A YouTube link, rendered through the same nocookie embed the blog renderer emits.
    /// Null = no trailer.</summary>
    public string? ShowcaseTrailerUrl { get; set; }

    /// <summary>
    /// T-300 — a host of the owner's own that serves this showcase at its root. Null = the page
    /// lives only under the blog subdomain. DNS and the certificate are done by hand on the
    /// Cloudflare side; this column is how the server knows to answer for the name (ADR-216).
    /// </summary>
    public string? CustomDomain { get; set; }

    // The /showcase/{slug}/press page's own fields, every one optional: an empty
    // section is omitted from the page, never rendered blank. Flat columns like the Showcase*
    // fields above, and for the same reason — a fixed field set, not a growable bag.
    public string? PressContactEmail { get; set; }
    public string? PressPrice { get; set; }
    public string? PressEngine { get; set; }
    public string? PressGenre { get; set; }

    /// <summary>Extra factsheet rows, newline-separated <c>Label: value</c> lines.</summary>
    public string? PressFactsheetRows { get; set; }

    /// <summary>
    /// T-124 — the number the next sprint of this project will get, then incremented.
    ///
    /// A counter rather than <c>MAX(Number) + 1</c>, because that expression **reuses the highest
    /// number as soon as the sprint holding it is deleted** — caught by running it, not by reading
    /// it. ADR-111 wants a gap instead: a second "S3" standing for a different stretch of time is
    /// exactly the confusion the number exists to prevent.
    /// </summary>
    public int NextSprintNumber { get; set; } = 1;
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

    // T-140 — what the file's own header says. Null means "not read" or "this kind does not say":
    // an image has no duration, and only WAV reports one at all (see CedarClerk.Core.WavHeader).
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationMs { get; set; }
    public int? SampleRate { get; set; }

    /// <summary>
    /// The <see cref="ModifiedAt"/> the metadata above was read for. Re-reading only when this no
    /// longer matches is what keeps a re-scan of a hundred thousand files from opening every one of
    /// them again: the first scan pays for the headers, later scans pay only for what changed.
    /// </summary>
    public DateTime? MetadataForModifiedAt { get; set; }

    /// <summary>
    /// The <see cref="ModifiedAt"/> the stored thumbnail was made from. Null = none yet.
    ///
    /// **The same column, a different source since ADR-117.** It used to mean "the server generated a
    /// preview from this version of the file"; it now means "the agent uploaded one". The question it
    /// answers did not change — is the preview on disk still of the current file? — so a second column
    /// would have been a second way to ask one thing.
    ///
    /// It is also what makes the preview pass resumable: everything where this does not equal
    /// <see cref="ModifiedAt"/> is exactly the work still outstanding, so an upload interrupted at the
    /// eight-thousandth file continues rather than restarts.
    /// </summary>
    public DateTime? ThumbnailForModifiedAt { get; set; }
}

/// <summary>
/// One task of the tracker (T-123, ADR-106) — a fixed set of fields, not a document.
///
/// The boundary ADR-106 draws: if the content is free text and the fields around it are metadata,
/// it is a <see cref="Draft"/> with a type. If the content is a set of fields and text is only one
/// of them, it is its own entity. Hence <see cref="Description"/> is **plain text, not TipTap** —
/// a task that needs tables and media is really a document, and should be created as one and
/// linked. The empty state in the UI says exactly that.
///
/// Named <c>GameTask</c> rather than <c>Task</c> for the obvious reason: every async signature in
/// the codebase already uses that name.
/// </summary>
public class GameTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;

    /// <summary>Required — a task outside a project has nowhere to be shown (unlike a document,
    /// which may live on its own; every draft written before the module does).</summary>
    public Guid ProjectId { get; set; }

    public string Title { get; set; } = "";

    /// <summary>One of <see cref="CedarClerk.Core.TaskStatuses"/>.</summary>
    public string Status { get; set; } = CedarClerk.Core.TaskStatuses.Backlog;

    /// <summary>1–3, see <see cref="CedarClerk.Core.TaskPriorities"/>.</summary>
    public int Priority { get; set; } = CedarClerk.Core.TaskPriorities.Normal;

    /// <summary>Plain text — see the type's summary for why this is not a document body.</summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Free text, deliberately. The product is single-user (INDIEDEV.md): a picker over a list of
    /// one, or people-management UI for a person working alone, would be ceremony around nothing.
    /// It exists at all because Marty's roles are many even when the person is one — "composer",
    /// "marketing" is a useful thing to write here.
    /// </summary>
    public string Assignee { get; set; } = "";

    /// <summary>
    /// T-124's sprint. The column exists before the <c>Sprint</c> entity does so that the planner
    /// adds a table rather than also altering this one; until then it is always null. A plain
    /// scalar with no FK, like <c>Draft.FolderId</c> and <c>Draft.ProjectId</c>.
    /// </summary>
    public Guid? SprintId { get; set; }

    /// <summary>
    /// T-126 — the build this task shipped in, or null. A column rather than a link because the
    /// question is "which one", singular, and it gets filtered and counted (ADR-112). A document's
    /// relationship to a build is looser and uses <see cref="EntityLink"/> instead.
    /// </summary>
    public Guid? BuildId { get; set; }

    /// <summary>Null = no deadline. Overdue is derived, never stored — it changes with the clock.</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>
    /// T-159 (ADR-134) — ticked tasks appear on the project's public showcase roadmap, title and
    /// status only. Opt-in per task: a tracker is working material by default.
    /// </summary>
    public bool IsPublicRoadmap { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the task became <see cref="CedarClerk.Core.TaskStatuses.Done"/>, cleared if it is
    /// reopened. Stored rather than derived from UpdatedAt because editing the title of a finished
    /// task would otherwise move the day it was finished.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Archived, not deleted — the same choice <see cref="Project.ArchivedAt"/> makes. A task that
    /// turned out to be wrong is part of what happened; the board just stops showing it.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }
}

/// <summary>
/// A stretch of planning over tasks (T-124, ADR-106/111) — a name, a number and two dates.
///
/// **There is no status column.** "Current / planned / finished" is decided by the dates every
/// time it is asked, because a stored status is wrong the second the clock passes <see
/// cref="EndsAt"/> and then needs machinery to keep it true. See ADR-111.
/// </summary>
public class Sprint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Sequential within the project, assigned at creation, **never reused**. This is what makes
    /// the `S14` chip on a task card real data rather than a number parsed out of a name — the
    /// number answers "which one", the name answers "about what" ("Ferry Terminal").
    ///
    /// A gap after a deletion is honest; a second sprint 7 meaning a different stretch of time
    /// is not.
    /// </summary>
    public int Number { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Dates, not instants — a sprint is a run of days, and half of one is not a thing.</summary>
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A released (or planned) version of the game (T-126, ADR-112).
///
/// A real entity rather than a tag: a version has a number, a release date, notes and a set of
/// things that went into it, and a flat <c>Draft.Tags</c> string holds none of those and cannot
/// answer "what is in 0.4.2" except by scanning and parsing. Same line ADR-106 drew for tasks —
/// a set of fields is an entity, free text is a tag.
///
/// **It knows nothing about git.** No repository tags, no CI, no build artefacts: this is a record
/// the author keeps, and pretending to be an integration that does not exist would be worse than
/// honestly being a record.
/// </summary>
public class Build
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ProjectId { get; set; }

    /// <summary>Whatever the author calls it — "0.4.2", "Demo 3", "Steam Next Fest build".</summary>
    public string Version { get; set; } = "";

    /// <summary>Free text: what this version is about, above the list of what went into it.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Null = planned but not out yet. The list shows unreleased builds first.</summary>
    public DateTime? ReleasedAt { get; set; }

    /// <summary>T-299 — this build is offered for download on the project's showcase.</summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Where the file actually is. A link the author hosts, not bytes we take: storage is already
    /// what registration waits on (T-172), and a download page that turned a 2 GB build into our
    /// disk problem would make that harder to solve, not easier (ADR-216).
    /// </summary>
    public string? DownloadUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A link between two things in a project (T-141) — today a document and an indexed asset.
///
/// **Generalises ADR-106's <c>TaskLink</c>** rather than sitting beside it: that row was going to be
/// (task → anything), and the moment a second kind of thing needed linking it would have been two
/// tables doing one job. A task link is an <see cref="EntityLink"/> whose one side is a task.
///
/// **Links are made by hand, and that is not a shortcut.** An indexed asset lives on disk outside
/// Cedar Clerk; nothing in a TipTap document can reference it, because the editor's own images are
/// uploaded media under <c>/media/</c>. There is no text to scan for, so "used in" could never be
/// discovered — only stated.
/// </summary>
public class EntityLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ProjectId { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.LinkTargets"/>.</summary>
    public string FromType { get; set; } = "";
    public Guid FromId { get; set; }
    public string ToType { get; set; } = "";
    public Guid ToId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// T-296 — what a showcase page did on one day, as counters rather than events.
///
/// One row per (project, day, kind, label): a page view carries no label, a store-link click
/// carries the link's own label. "How many people opened the Steam link on Tuesday" is answerable
/// and "who" deliberately is not — the page has no reader identity and gains none from being
/// measured (ADR-216).
/// </summary>
public class ShowcaseStatDaily
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ProjectId { get; set; }

    public DateTime Day { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.ShowcaseStatKinds"/>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The store link's label; empty for a page view.</summary>
    public string Label { get; set; } = "";

    public int Count { get; set; }
}

/// <summary>
/// T-297 — an address following one project's devlog. Not an account: a follower is an address and
/// two tokens, which is what keeps the reader-identity question (T-004) open rather than answered
/// by a side door (ADR-216).
/// </summary>
public class ShowcaseFollower
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ProjectId { get; set; }

    /// <summary>Stored lowercase, so the unique index needs no collation of its own.</summary>
    public string Email { get; set; } = "";

    /// <summary>Null once confirmed. An unconfirmed row is never mailed anything but its own
    /// confirmation, which is the whole point of holding it.</summary>
    public string? ConfirmToken { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    /// <summary>In every mail. Per row, so leaving needs no account and no reply.</summary>
    public string UnsubscribeToken { get; set; } = "";

    /// <summary>
    /// Who asked, in the same one-way form the blog's reactions and comments use. A public form
    /// that writes a row and sends a mail needs a ceiling per asker, and the ceiling needs
    /// something to count.
    /// </summary>
    public string VisitorHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One Yarn dialogue script — the node editor's whole graph as JSON (nodes with title, canvas
/// position and a Yarn-syntax body). A blob rather than a node table because the editor always
/// loads and saves the graph whole, and no query ever asks for one node of it; the queryable unit
/// is the localizable line, which lives in <see cref="DialogueLineTranslation"/>.
///
/// The server stamps a <c>#line:</c> tag onto every localizable body line at save
/// (<see cref="CedarClerk.Core.YarnDialogue"/>) — ids are what the xlsx translation sheet keys on,
/// so they must exist before the first export and survive every edit after it.
/// </summary>
public class DialogueScript
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;

    /// <summary>Required — the dialogue screen hangs off a project's rail, the same reason
    /// <see cref="GameTask.ProjectId"/> gives. A plain scalar with no FK.</summary>
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>JSON array of nodes: <c>{ id, title, x, y, body }</c>.</summary>
    public string GraphJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One translated line of one script: (line id, language) → text. Rows, not a blob, because the
/// xlsx import upserts per line and per language, and "which lines have no German yet" is a query.
/// The base-language text is not here — it lives in the script body and the sheet re-reads it on
/// every export, so the sheet can never show a stale source line.
/// </summary>
public class DialogueLineTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid DialogueScriptId { get; set; }

    /// <summary>The <c>#line:</c> id without the prefix.</summary>
    public string LineId { get; set; } = "";

    /// <summary>Lowercase two-letter code, as typed into the sheet's column header.</summary>
    public string Language { get; set; } = "";

    public string Text { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
