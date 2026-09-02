namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Which tenant this request is for. Scoped, and written exactly once, by
/// <see cref="TenantResolutionMiddleware"/>.
///
/// Empty on every request that is not addressed to a tenant subdomain — the app host, the blog
/// host, a reserved subdomain, and anything running outside an HTTP request at all.
/// </summary>
public sealed class TenantContext
{
    public string? Username { get; private set; }
    public string? OwnerId { get; private set; }

    /// <summary>
    /// T-300 — the showcase this host answers with at its root, when the host is a project's own
    /// domain rather than a tenant subdomain. Null on every other request, including a showcase
    /// reached the ordinary way at <c>/showcase/{slug}</c>.
    /// </summary>
    public string? ShowcaseSlug { get; private set; }

    public bool IsTenantRequest => OwnerId is not null;

    public void Resolve(string username, string ownerId)
    {
        if (OwnerId is not null)
            throw new InvalidOperationException("The tenant of a request is resolved once.");

        Username = username;
        OwnerId = ownerId;
    }

    /// <summary>
    /// A custom domain resolves to an owner the same way a subdomain does — the difference is that
    /// the name it was matched by belongs to a project, so there is no username in it.
    /// </summary>
    public void ResolveShowcaseDomain(string ownerId, string showcaseSlug)
    {
        if (OwnerId is not null)
            throw new InvalidOperationException("The tenant of a request is resolved once.");

        OwnerId = ownerId;
        ShowcaseSlug = showcaseSlug;
    }
}
