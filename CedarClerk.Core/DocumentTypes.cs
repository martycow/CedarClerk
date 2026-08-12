namespace CedarClerk.Core;

// What kind of document a Draft is (ADR-102). A column on the existing entity, not a new one: a
// Draft is "a TipTap document with autosave, revisions, translations, tags and a folder", and every
// type needs all of it. Post is the default, so rows written before types existed are already right.
public static class DocumentTypes
{
    public const string Post = "post";
    public const string Design = "design";
    public const string Script = "script";
    public const string Plot = "plot";
    public const string Changelog = "changelog";
    public const string Note = "note";

    public static readonly IReadOnlyList<string> All = [Post, Design, Script, Plot, Changelog, Note];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    // A design document or plot outline is working material: publishing one is never intentional, so
    // the honest answer is a refusal rather than a post. A changelog is publishable — shipping notes
    // are exactly what an author announces.
    public static bool IsPublishable(string? type) => type switch
    {
        null => true,           // a row written before the column existed
        Post => true,
        Changelog => true,
        _ => false,
    };
}
