using CedarClerk.Localization;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// One account's blog and the host it is published at. Threaded through every blog render path
/// explicitly, in addition to the ambient owner filter: a page that names its owner in the query is
/// a page whose scoping can be tested, and the two together are what keep a slug from resolving
/// into somebody else's post.
/// </summary>
public readonly record struct BlogSite(
    string OwnerId,
    string Host,
    string TimeZoneId = Consts.General.DisplayTimeZone)
{
    public string BaseUrl => $"https://{Host}";
    public string PostUrl(string slug) => $"{BaseUrl}/{slug}";

    /// <summary>
    /// A private post's link. The token is only ever checked against the draft the host it is
    /// opened on resolves the slug to, so the wrong host makes the invite open somebody else's
    /// post — or nothing at all.
    /// </summary>
    public string InviteUrl(string slug, string token) => $"{PostUrl(slug)}?invite={token}";
}

/// <summary>
/// Whose blog a request is for. A blog lives at <c>{username}.{tenant domain}</c> and nowhere else,
/// so the host carries the answer and an account without a name has no blog to point at.
/// </summary>
public static class BlogTenant
{
    /// <summary>The blog this request renders, or null when the host names no tenant.</summary>
    public static BlogSite? SiteOf(HttpContext ctx) =>
        ctx.RequestServices.GetService<TenantContext>() is { IsTenantRequest: true } tenant
            ? new BlogSite(tenant.OwnerId!, ctx.Request.Host.Host)
            : null;

    /// <summary>The host this owner's blog answers at, or null when the account has no name yet.</summary>
    public static async Task<string?> HostForOwnerAsync(CedarDbContext db, IConfiguration cfg, string ownerId,
        CancellationToken ct = default)
    {
        var username = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => u.TenantUsername).FirstOrDefaultAsync(ct);

        return username is null ? null : Subdomain(username, cfg);
    }

    /// <summary>This owner's blog, for the paths that build URLs without a request to read it off.</summary>
    public static async Task<BlogSite?> SiteForOwnerAsync(CedarDbContext db, IConfiguration cfg,
        string ownerId, CancellationToken ct = default)
    {
        var user = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => new { u.TenantUsername, u.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        return user?.TenantUsername is { } username
            ? new BlogSite(ownerId, Subdomain(username, cfg), TimeZones.NormalizeOrDefault(user.TimeZoneId))
            : null;
    }

    /// <summary>Hosts for many owners in one query — the admin post list spans accounts.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> HostsForOwnersAsync(
        CedarDbContext db, IConfiguration cfg, IEnumerable<string> ownerIds, CancellationToken ct = default)
    {
        var ids = ownerIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var named = await db.Users.Where(u => ids.Contains(u.Id) && u.TenantUsername != null)
            .Select(u => new { u.Id, u.TenantUsername })
            .ToListAsync(ct);

        return named.ToDictionary(u => u.Id, u => Subdomain(u.TenantUsername!, cfg));
    }

    private static string Subdomain(string username, IConfiguration cfg) =>
        $"{username}.{cfg[Consts.General.TenantHostCfg] ?? Consts.URLs.TenantHost}";
}
