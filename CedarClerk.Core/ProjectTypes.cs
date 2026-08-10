namespace CedarClerk.Core;

/// <summary>
/// What kind of work a <c>Project</c> is. Its one job is answering "what document does this project
/// start with" — ADR-103 requires a project to hold at least one document from the moment it exists,
/// and the type is how the create dialog knows which one to make.
///
/// Restored by Marty's design handoff (10.08.2026). ADR-103 had narrowed the create dialog to a bare
/// document-type picker because the project-type taxonomy in it was invented rather than asked for;
/// the handoff supplies the real four, with a starter document each, so the taxonomy is back — as a
/// product decision this time.
/// </summary>
public static class ProjectTypes
{
    /// <summary>Everything: documents, sprints, an asset index, a press kit. Starts with the GDD.</summary>
    public const string FullGame = "fullgame";

    /// <summary>Deadline first — one sprint, a plan, a submission checklist.</summary>
    public const string Jam = "jam";

    /// <summary>A question to answer. Notes and tasks, no ceremony.</summary>
    public const string Prototype = "prototype";

    /// <summary>Post-launch: patches, changelogs, press.</summary>
    public const string Released = "released";

    public static readonly IReadOnlyList<string> All = [FullGame, Jam, Prototype, Released];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    /// <summary>
    /// The kind of document a new project of this type is created with. The document's *title* is
    /// not decided here: it is user-facing text, and the client already has both languages — the
    /// server would only be able to write it in one.
    /// </summary>
    public static string StarterDocumentType(string? projectType) => projectType switch
    {
        // The master reference the whole project hangs off.
        FullGame => DocumentTypes.Design,
        // A jam plan is the same master document at jam scale — scope, schedule, what ships.
        Jam => DocumentTypes.Design,
        // "Hypothesis note": the question being answered, before it deserves a design document.
        Prototype => DocumentTypes.Note,
        Released => DocumentTypes.Changelog,
        // An unknown or absent type still has to produce something — a project without a document
        // cannot exist (ADR-103), so the fallback is the type every draft already is.
        _ => DocumentTypes.Post,
    };
}
