using CedarClerk.Core;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// ADR-094 — the pieces of a micro-thread plan the queue and the targets must agree on: the blog
/// URL (both short-post targets carried an identical copy of this) and how much of the last
/// part's budget it needs. Same inputs → same parts, which is the T-106 principle that lets the
/// split be recomputed instead of stored.
/// </summary>
public static class MicroThreadPlan
{
    public static string? BlogUrl(Draft draft, string language, IConfiguration cfg) =>
        draft.IsBlogPublished && draft.BlogSlug is not null
            ? $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}"
              + (language == draft.PrimaryLanguage ? "" : $"?lang={language}")
            : null;

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
