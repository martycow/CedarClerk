namespace CedarClerk.Core;

/// <summary>
/// The networks a document can be published to (ADR-078). A plain string key rather than an enum:
/// it is stored in the database, sent over the API and read by the Angular client, and an enum
/// would be an int in the first, a string in the second and a magic number in the third.
/// </summary>
public static class PublishNetworks
{
    public const string Telegram = "telegram";
    public const string Bluesky = "bluesky";

    public static readonly IReadOnlyList<string> All = [Telegram, Bluesky];

    public static bool IsKnown(string? network) => network is not null && All.Contains(network);
}

/// <summary>
/// What a network can accept, as data rather than behaviour (ADR-078, point 2). The editor reads
/// this to warn the author *before* a send (T-086), and an editor cannot display a method call.
///
/// Every limit here is a real published constraint of the network, not a guess — the values live
/// with each target implementation, and the ones that are unlimited say so with null rather than
/// with int.MaxValue, so "no limit" and "a very large limit" stay distinguishable.
/// </summary>
public record PublishCapabilities
{
    public required string Network { get; init; }

    /// <summary>Characters of body text the network accepts; null = no practical ceiling.</summary>
    public int? MaxCharacters { get; init; }

    /// <summary>
    /// Character budget for one part of a thread (ADR-086); null = use <see cref="MaxCharacters"/>.
    /// Distinct from it on purpose: "what the network refuses" and "what a subscriber reads as one
    /// message" are different numbers — Telegram accepts 32,768 but collapses a post behind
    /// "Show more" long before that.
    /// </summary>
    public int? ThreadPartCharacters { get; init; }

    /// <summary>Media items in one post; 0 = the network takes no media at all.</summary>
    public int MaxMediaItems { get; init; }

    /// <summary>Bytes per image. Telegram fetches by URL and rejects above ~10MB; Bluesky uploads a blob capped at 1MB.</summary>
    public long? MaxImageBytes { get; init; }

    public bool SupportsVideo { get; init; }
    public bool SupportsAudio { get; init; }

    /// <summary>Bold/italic/etc. Bluesky has none — its posts are plain text plus facets (links, mentions, tags).</summary>
    public bool SupportsRichText { get; init; }

    public bool SupportsHeadings { get; init; }
    public bool SupportsLists { get; init; }
    public bool SupportsTables { get; init; }
    public bool SupportsCodeBlocks { get; init; }
    public bool SupportsMath { get; init; }

    /// <summary>A link rendered with a title/description/thumbnail card, as opposed to a bare URL.</summary>
    public bool SupportsLinkPreview { get; init; }

    /// <summary>Alt text per image. Absent on Telegram's Blocks photos, present and expected on Bluesky.</summary>
    public bool SupportsAltText { get; init; }

    /// <summary>Whether a long document can be split across linked posts instead of being cut.</summary>
    public bool SupportsThreads { get; init; }

    /// <summary>
    /// True when a post carries a public URL that can be linked back to. A Telegram channel without
    /// a @username does not — which is exactly the case ADR-065 found silently skipping a guard,
    /// so it is stated here rather than assumed by every caller.
    /// </summary>
    public bool PostsHavePublicUrls { get; init; }
}
