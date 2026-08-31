namespace CedarClerk.Core;

// The values live in SQLite (Project.ProjectType) and the API — byte-exact zone. The offered set
// was cut to four on 31.08.2026 (sprint v0.2.0, Marty's pick): Empty, Blog, Game, Product. "jam"
// and "prototype" left the offer but stay recognized — projects created with them keep working.
public static class ProjectTypes
{
    public const string Empty = "empty";
    public const string Blog = "blog";
    // "fullgame" is the stored key of what the UI now calls just "Game": renaming a stored value
    // means a data migration for nothing a user can see.
    public const string FullGame = "fullgame";
    public const string Product = "product";

    public const string Jam = "jam";
    public const string Prototype = "prototype";
    public const string Released = "released";

    /// <summary>What the New-project dialog offers, in its display order.</summary>
    public static readonly IReadOnlyList<string> All = [Empty, Blog, FullGame, Product];

    private static readonly IReadOnlyList<string> Legacy = [Jam, Prototype, Released];

    public static bool IsKnown(string? type) =>
        type is not null && (All.Contains(type) || Legacy.Contains(type));

    // The title is the client's to write: it is user-facing text and the client has both languages,
    // where the server would manage only one.
    public static string StarterDocumentType(string? projectType) => projectType switch
    {
        // A bare container still has to produce something (ADR-103) — the lightest document there is.
        Empty => DocumentTypes.Note,
        // The master reference the whole project hangs off.
        FullGame => DocumentTypes.Design,
        // Post-launch support of anything: patches, releases, press.
        Product => DocumentTypes.Changelog,
        Blog => DocumentTypes.Post,
        // Legacy types keep the starter they always had.
        Jam => DocumentTypes.Design,
        Prototype => DocumentTypes.Note,
        Released => DocumentTypes.Changelog,
        // An unknown or absent type still has to produce something — a project without a document
        // cannot exist (ADR-103), so the fallback is the type every draft already is.
        _ => DocumentTypes.Post,
    };
}
