namespace CedarClerk.Core;

/// <summary>
/// What kind of document a <c>Draft</c> is (ADR-102). A column on the existing entity rather than a
/// new one: a Draft is really "a TipTap document with autosave, revision history, translations, tags
/// and a folder", and every type below needs all of that verbatim.
///
/// <see cref="Post"/> is the default, so every row written before types existed is already correct.
/// </summary>
public static class DocumentTypes
{
    /// <summary>A devlog entry — the only type that existed before ADR-102.</summary>
    public const string Post = "post";
    public const string Design = "design";
    public const string Script = "script";
    public const string Plot = "plot";
    public const string Changelog = "changelog";
    public const string Note = "note";

    public static readonly IReadOnlyList<string> All = [Post, Design, Script, Plot, Changelog, Note];

    public static bool IsKnown(string? type) => type is not null && All.Contains(type);

    /// <summary>
    /// Whether this kind of document is meant to leave the app — a Telegram send, a blog page, a
    /// cross-post. A game-design document or a plot outline is working material: publishing one is
    /// never intentional, and the honest answer to the request is a refusal rather than a post.
    ///
    /// A changelog IS publishable: shipping notes are exactly the thing an author wants to announce.
    ///
    /// Nothing changes for existing content — every row is <see cref="Post"/> until someone changes
    /// its type on purpose.
    /// </summary>
    public static bool IsPublishable(string? type) => type switch
    {
        null => true,           // a row written before the column existed
        Post => true,
        Changelog => true,
        _ => false,
    };
}
