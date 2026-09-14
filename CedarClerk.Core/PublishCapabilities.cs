namespace CedarClerk.Core;

// String keys, not an enum: the value is stored in SQLite, sent over the API and read by Angular,
// where an enum would be an int, a string and a magic number respectively (ADR-078).
public static class PublishNetworks
{
    public const string Telegram = "telegram";
    public const string Bluesky = "bluesky";
    public const string X = "x";
    public const string Discord = "discord";
    public const string LinkedIn = "linkedin";

    public static readonly IReadOnlyList<string> All = [Telegram, Bluesky, X, Discord, LinkedIn];

    public static bool IsKnown(string? network) => network is not null && All.Contains(network);
}

// Capabilities as data, not behaviour (ADR-078): the editor warns the author before a send, and it
// cannot display a method call. Unlimited is null rather than int.MaxValue, so "no limit" and "a
// very large limit" stay distinguishable.
public record PublishCapabilities
{
    public required string Network { get; init; }

    public int? MaxCharacters { get; init; }

    // Separate from MaxCharacters because "what the network refuses" and "what a subscriber reads as
    // one message" differ: Telegram accepts 32,768 but collapses behind "Show more" long before that.
    public int? ThreadPartCharacters { get; init; }

    public int MaxMediaItems { get; init; }

    // Telegram fetches by URL and rejects above ~10MB; Bluesky uploads a blob capped at 1MB.
    public long? MaxImageBytes { get; init; }

    public bool SupportsVideo { get; init; }
    public bool SupportsAudio { get; init; }
    public bool SupportsRichText { get; init; }
    public bool SupportsHeadings { get; init; }
    public bool SupportsLists { get; init; }
    public bool SupportsTables { get; init; }
    public bool SupportsCodeBlocks { get; init; }
    public bool SupportsMath { get; init; }
    public bool SupportsLinkPreview { get; init; }
    public bool SupportsAltText { get; init; }
    public bool SupportsThreads { get; init; }

    // A Telegram channel without a @username has no linkable URL — the case ADR-065 found silently
    // skipping a guard, so it is stated rather than assumed by every caller.
    public bool PostsHavePublicUrls { get; init; }

    // ADR-093 — the target derives a short post itself, so "the document is too long" describes what
    // the teaser omits rather than a reason to refuse the publish.
    public bool DerivesShortPost { get; init; }
}
