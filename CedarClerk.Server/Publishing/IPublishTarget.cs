using CedarClerk.Core;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// One document, resolved into one language, on its way to one target (ADR-078). The document is
/// already resolved here rather than being a draft id: which language a draft "is" and which
/// revision is current are questions `DraftRevisionService` answers, and a target has no business
/// re-deciding them.
/// </summary>
public record PublishRequest
{
    public required Guid DraftId { get; init; }
    public required string OwnerId { get; init; }
    public required string Language { get; init; }
    public required string Title { get; init; }
    public required string CedarJson { get; init; }
    public required PublishTarget Target { get; init; }

    /// <summary>
    /// The author's own text for this network (ADR-077). Null means none was written and the
    /// target derives one — that fallback is what keeps publishing from blocking on a second draft.
    /// </summary>
    public string? AuthorText { get; init; }

    /// <summary>
    /// T-106 — which slice of a threaded publication this is, and what to hang it off. Null for an
    /// ordinary single message, which is every publication that fits.
    /// </summary>
    public ThreadPartRef? Part { get; init; }

    /// <summary>
    /// How hard to compress media before sending: "small" / "standard" / "high". General rather
    /// than a Telegram detail — every network caps image bytes (Telegram ~10MB by URL fetch,
    /// Bluesky 1MB by blob upload), so "how much quality may be traded for fitting" is a choice
    /// the author makes once and every target honours in its own units.
    /// </summary>
    public string CompressionLevel { get; init; } = "standard";
}

/// <summary>
/// What the network created. <paramref name="PublicUrl"/> is null where the network has no public
/// address for the post — a Telegram channel without a @username is the case that already exists
/// in production, and ADR-065 found code treating its absence as "never published".
/// </summary>
public record PublishReceipt(string RemoteId, string? PublicUrl);

/// <param name="ReplyToRemoteId">
/// The previous part's message id. Telegram renders a reply chain, which is what turns eight
/// messages into one thread a reader can follow instead of eight posts in a row.
/// </param>
/// <param name="RootRemoteId">
/// The FIRST part's id (ADR-094) — Bluesky's reply record wants the thread root beside the
/// parent, not just the message being replied to. Null on part 0 and on networks that chain by
/// parent alone.
/// </param>
public record ThreadPartRef(int Index, int Count, string? ReplyToRemoteId, string? RootRemoteId = null);

/// <summary>
/// Deliberately the same shape as the `PostEndpoints.PublishResult` it will replace in T-085 —
/// success or a readable reason plus the status code to answer with — so that refactor stays a
/// move rather than a redesign. A target reports failure this way instead of throwing, because
/// every current caller (the export endpoint and a Quartz job) has to turn it into a response or
/// a stored error, and a job that dies on an unhandled exception loses the reason.
/// </summary>
public record PublishOutcome(PublishReceipt? Receipt, string? Error, int StatusCode = StatusCodes.Status400BadRequest)
{
    public bool Success => Error is null;

    public static PublishOutcome Ok(PublishReceipt receipt) => new(receipt, null);

    public static PublishOutcome Fail(string error, int statusCode = StatusCodes.Status400BadRequest) =>
        new(null, error, statusCode);
}

/// <summary>
/// A network Cedar Clerk can publish to (ADR-078). The obligation is three things and no more:
/// name the network, describe what it accepts, and send. It does not fetch statistics, does not
/// delete or edit, and does not run its own connect flow — see the ADR for why each of those was
/// left out rather than forgotten.
///
/// Registered per network in `Program.cs` and resolved by `Network`, the same way
/// `ITranslationProvider` implementations are.
/// </summary>
public interface IPublishTarget
{
    /// <summary>One of <see cref="PublishNetworks"/>.</summary>
    string Network { get; }

    /// <summary>Static description of the network's limits, read by the editor before a send (T-086).</summary>
    PublishCapabilities Capabilities { get; }

    Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default);
}
