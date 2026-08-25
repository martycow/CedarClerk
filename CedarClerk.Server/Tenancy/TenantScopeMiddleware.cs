using System.Security.Claims;
using CedarClerk.Core;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Decides, before authentication, which model this request's context gets: filtered to one owner,
/// or unfiltered because the path is the platform's own work.
/// </summary>
public sealed class TenantScopeMiddleware(RequestDelegate next, string blogHost, TenantOwnerCache owners)
{
    // The legacy host has no name in the cache's sense, and no username is ever empty.
    private const string LegacyOwnerKey = "";

    public TenantScopeMiddleware(RequestDelegate next, string blogHost)
        : this(next, blogHost, new TenantOwnerCache()) { }

    public async Task InvokeAsync(
        HttpContext ctx, TenantProvider tenant, TenantContext resolved,
        IConfiguration cfg, ILogger<TenantScopeMiddleware> log)
    {
        // A tenant subdomain already knows whose blog it is, and the reader is anonymous.
        if (resolved.IsTenantRequest)
        {
            tenant.UseTenant(resolved.OwnerId!);
            await next(ctx);
            return;
        }

        // The legacy blog host is one account's blog as well — it just cannot name the account in
        // its own hostname, so the owner is looked up instead of read off the Host header.
        if (string.Equals(ctx.Request.Host.Host, blogHost, StringComparison.OrdinalIgnoreCase))
        {
            var ownerId = await owners.GetAsync(LegacyOwnerKey, token =>
                    BlogTenant.ResolveOwnerIdAsync(
                        ctx.RequestServices.GetRequiredService<CedarDbContext>(), cfg, token),
                ctx.RequestAborted);

            if (ownerId is null)
            {
                // 503, not 404: the blog is not gone, this server cannot tell whose it is. A 404
                // here is cacheable and reads to a stranger as a blog that was taken down.
                log.LogError("Blog host {Host} has no resolvable owner.", blogHost);
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync("Blog owner is not configured.");
                return;
            }

            tenant.UseTenant(ownerId);
            await next(ctx);
            return;
        }

        if (PlatformPaths.IsPlatform(ctx.Request.Path))
            tenant.UsePlatform();

        await next(ctx);
    }
}

/// <summary>
/// Fills in the tenant of an ordinary signed-in request, and takes the identity away where the
/// tenant was already read off the Host header. Separate from the middleware above, and placed
/// after <c>UseAuthentication</c>, because that is the first moment either is known.
///
/// A host that renders a blog is somebody's public site, and its reader is a stranger. A sign-in
/// surviving there is not a second opinion about whose request this is — the tenant is already the
/// host owner, so every row that account writes lands stamped with the host owner's id, passing
/// every check on the way. The identity is dropped instead, and said out loud.
/// </summary>
public sealed class TenantFromUserMiddleware(RequestDelegate next, string blogHost)
{
    public async Task InvokeAsync(
        HttpContext ctx, TenantProvider tenant, ILogger<TenantFromUserMiddleware> log)
    {
        if (TenantRouting.ServesBlog(ctx, blogHost))
        {
            if (ctx.User.Identity?.IsAuthenticated == true)
            {
                log.LogWarning("Dropped a signed-in identity on blog host {Host}.", ctx.Request.Host.Host);
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
        }
        else if (tenant is { IsPlatform: false, TenantId: null }
                 && ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId)
        {
            tenant.UseTenant(userId);
        }

        await next(ctx);
    }
}

public static class TenantScopeExtensions
{
    public static void UseTenantScope(this WebApplication app) =>
        app.UseMiddleware<TenantScopeMiddleware>(BlogHostOf(app), new TenantOwnerCache());

    public static void UseTenantFromUser(this WebApplication app) =>
        app.UseMiddleware<TenantFromUserMiddleware>(BlogHostOf(app));

    private static string BlogHostOf(WebApplication app) =>
        app.Configuration[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;
}
