using CedarClerk.Core;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// ADR-094 — the pieces of a micro-thread plan the queue and the targets must agree on: the blog
/// URL (both short-post targets carried an identical copy of this) and how much of the last
/// part's budget it needs. Same inputs → same parts, which is the T-106 principle that lets the
/// split be recomputed instead of stored.
/// </summary>
public static class MicroThreadPlan
{
    /// <summary>
    /// The link the last part carries, on the host that actually serves this draft. Slugs are
    /// per-owner, so the legacy host would resolve another account's post under the same name —
    /// and this link goes out into post history that cannot be edited afterwards.
    /// </summary>
    public static async Task<string?> BlogUrlAsync(Draft draft, string language, CedarDbContext db,
        IConfiguration cfg, CancellationToken ct = default)
    {
        if (!draft.IsBlogPublished || draft.BlogSlug is null) return null;
        if (await BlogTenant.HostForOwnerAsync(db, cfg, draft.OwnerId, ct) is not { } host) return null;

        return $"https://{host}/{draft.BlogSlug}"
               + (language == draft.PrimaryLanguage ? "" : $"?lang={language}");
    }

    /// <summary>The last part's budget for "\n\n" plus the link, in the network's own units.</summary>
    public static int LinkReserve(string network, string? blogUrl) => blogUrl is null ? 0 : network switch
    {
        PublishNetworks.X => XPostBuilder.UrlWeight + 2,
        PublishNetworks.Bluesky => BlueskyPostBuilder.GraphemeCount(blogUrl) + 2,
        _ => 0,
    };

    public static IReadOnlyList<string> Parts(string cedarJson, string network, string? blogUrl) =>
        MicroThreadSplitter.Split(cedarJson, network, LinkReserve(network, blogUrl));
}
