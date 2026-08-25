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

    public bool IsTenantRequest => OwnerId is not null;

    public void Resolve(string username, string ownerId)
    {
        if (OwnerId is not null)
            throw new InvalidOperationException("The tenant of a request is resolved once.");

        Username = username;
        OwnerId = ownerId;
    }
}
