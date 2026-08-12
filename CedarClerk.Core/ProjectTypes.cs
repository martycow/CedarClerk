namespace CedarClerk.Core;

// What kind of work a Project is. Its one job is answering which document the project starts with —
// ADR-103 requires a project to hold one from the moment it exists. The four types come from Marty's
// design handoff (10.08.2026), which is why the taxonomy is a product decision rather than invented.
public static class ProjectTypes
{
    public const string FullGame = "fullgame";
    public const string Jam = "jam";
    public const string Prototype = "prototype";
    public const string Released = "released";

    public static readonly IReadOnlyList<string> All = [FullGame, Jam, Prototype, Released];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    // The title is the client's to write: it is user-facing text and the client has both languages,
    // where the server would manage only one.
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
