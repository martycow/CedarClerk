using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// One account's blog and the host it is published at. Threaded through every blog render path
/// explicitly, in addition to the ambient owner filter: a page that names its owner in the query is
/// a page whose scoping can be tested, and the two together are what keep a slug from resolving
/// into somebody else's post.
/// </summary>
public readonly record struct BlogSite(string OwnerId, string Host)
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
/// Whose blog a request is for. A subdomain carries the answer in its own name; the legacy blog
/// host predates subdomains and has to be told. Told wrong, it would publish one account's drafts
/// under another's established domain — so an owner that cannot be resolved is an error rather than
/// a guess, and a configured name nothing answers to never falls through to a different account.
/// </summary>
public static class BlogTenant
{
    public static string LegacyHost(IConfiguration cfg) =>
        cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;

    /// <summary>The account the legacy blog host belongs to, or null when it cannot be told.</summary>
    public static async Task<string?> ResolveOwnerIdAsync(CedarDbContext db, IConfiguration cfg, CancellationToken ct = default)
    {
        // A name rather than an id: readable in the systemd drop-in, and it survives a restore.
        if (Usernames.Normalize(cfg[Consts.General.BlogOwnerCfg]) is { } name)
            return await db.Users.Where(u => u.TenantUsername == name).Select(u => u.Id).FirstOrDefaultAsync(ct);

        var admins = await db.Users.Where(u => u.IsAdmin).Select(u => u.Id).Take(2).ToListAsync(ct);
        return admins.Count == 1 ? admins[0] : null;
    }

    /// <summary>The blog this request renders, or null when the legacy host has no owner.</summary>
    public static async Task<BlogSite?> SiteOfAsync(HttpContext ctx, CedarDbContext db, CancellationToken ct = default)
    {
        if (ctx.RequestServices.GetService<TenantContext>() is { IsTenantRequest: true } tenant)
            return new BlogSite(tenant.OwnerId!, ctx.Request.Host.Host);

        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        return await ResolveOwnerIdAsync(db, cfg, ct) is { } ownerId
            ? new BlogSite(ownerId, LegacyHost(cfg))
            : null;
    }

    /// <summary>The host this owner's blog answers at, or null when the account has no blog yet.</summary>
    public static async Task<string?> HostForOwnerAsync(CedarDbContext db, IConfiguration cfg, string ownerId, CancellationToken ct = default)
    {
        // The legacy owner keeps the legacy host. Those URLs are published, indexed and linked from
        // channel history that cannot be edited, so moving them is a break with no undo.
        if (await ResolveOwnerIdAsync(db, cfg, ct) == ownerId)
            return LegacyHost(cfg);

        var username = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => u.TenantUsername).FirstOrDefaultAsync(ct);

        return username is null ? null : Subdomain(username, cfg);
    }

    /// <summary>This owner's blog, for the paths that build URLs without a request to read it off.</summary>
    public static async Task<BlogSite?> SiteForOwnerAsync(CedarDbContext db, IConfiguration cfg,
        string ownerId, CancellationToken ct = default) =>
        await HostForOwnerAsync(db, cfg, ownerId, ct) is { } host ? new BlogSite(ownerId, host) : null;

    /// <summary>
    /// Hosts for many owners in two queries. A list that spans accounts — the admin post list —
    /// would otherwise resolve the legacy owner once per row.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> HostsForOwnersAsync(
        CedarDbContext db, IConfiguration cfg, IEnumerable<string> ownerIds, CancellationToken ct = default)
    {
        var ids = ownerIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var legacyOwnerId = await ResolveOwnerIdAsync(db, cfg, ct);
        var named = await db.Users.Where(u => ids.Contains(u.Id) && u.TenantUsername != null)
            .Select(u => new { u.Id, u.TenantUsername })
            .ToListAsync(ct);

        var hosts = named.ToDictionary(u => u.Id, u => Subdomain(u.TenantUsername!, cfg));
        if (legacyOwnerId is not null && ids.Contains(legacyOwnerId))
            hosts[legacyOwnerId] = LegacyHost(cfg);
        return hosts;
    }

    private static string Subdomain(string username, IConfiguration cfg) =>
        $"{username}.{cfg[Consts.General.TenantHostCfg] ?? Consts.URLs.TenantHost}";
}
