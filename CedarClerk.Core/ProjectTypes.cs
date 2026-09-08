namespace CedarClerk.Core;

// The values live in SQLite (Project.CreatedFromPreset) and the API — byte-exact zone. A type is a
// creation preset (ADR-293): it picks the icon, the vocabulary, the starter document and the
// modules a project is born with, and decides nothing after that.
public static class ProjectTypes
{
    public const string Empty = "empty";
    public const string Blog = "blog";
    // "fullgame" is the stored key of what the UI calls just "Game": renaming a stored value
    // means a data migration for nothing a user can see.
    public const string FullGame = "fullgame";
    public const string Product = "product";
    public const string Work = "work";
    public const string Vault = "vault";

    /// <summary>What the New-project dialog offers, in its display order.</summary>
    public static readonly IReadOnlyList<string> All = [Empty, Blog, FullGame, Product, Work, Vault];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    // The title is the client's to write: it is user-facing text and the client has both languages,
    // where the server would manage only one.
    public static string StarterDocumentType(string? projectType) => projectType switch
    {
        // A bare container still has to produce something (ADR-103) — the lightest document there is.
        Empty or Work or Vault => DocumentTypes.Note,
        // The master reference the whole project hangs off.
        FullGame => DocumentTypes.Design,
        // Post-launch support of anything: patches, releases, press.
        Product => DocumentTypes.Changelog,
        Blog => DocumentTypes.Post,
        // An unknown or absent type still has to produce something — a project without a document
        // cannot exist (ADR-103), so the fallback is the type every draft already is.
        _ => DocumentTypes.Post,
    };
}
