using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Turns the Host header into a resolved tenant, or into a 404.
///
/// Runs before everything else: the landing page answers "/" on any host that is not the blog, so
/// without this an unregistered subdomain would return a marketing page with a 200 and let
/// Cloudflare cache it that way.
///
/// Every stylesheet, font and image a blog page asks for comes back through here, which is why the
/// lookup goes through <see cref="TenantOwnerCache"/> rather than straight to the database.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, string tenantDomain, string mainHost,
    TenantOwnerCache.ForHosts owners, TenantOwnerCache.ForDomains domains)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var result = TenantHost.Resolve(ctx.Request.Host.Host, tenantDomain);

        switch (result.Kind)
        {
            case TenantHostKind.Invalid:
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;

            case TenantHostKind.Tenant:
                var ownerId = await owners.GetAsync(result.Username!, token =>
                        ctx.RequestServices.GetRequiredService<CedarDbContext>().Users
                            .Where(u => u.TenantUsername == result.Username)
                            .Select(u => u.Id)
                            .FirstOrDefaultAsync(token),
                    ctx.RequestAborted);

                if (ownerId is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ctx.RequestServices.GetRequiredService<TenantContext>().Resolve(result.Username!, ownerId);
                break;

            // T-300 — a host that is neither the tenant domain nor the application's own may be a
            // project's own domain. Asked of the database only after those two are ruled out: the
            // application host is every request the app makes of itself, and a lookup there would
            // be a query per request forever.
            case TenantHostKind.NotTenantDomain when !IsOwnHost(ctx.Request.Host.Host):
                var claim = await domains.GetAsync(ctx.Request.Host.Host.ToLowerInvariant(), async token =>
                {
                    // A platform scope, not the request's own context: the tenant is what this
                    // lookup is trying to find, so under the filter it would answer "no project"
                    // every time — and fail closed, which is the failure nobody notices.
                    using var scope = ctx.RequestServices.CreatePlatformScope();
                    var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
                    var project = await db.Projects
                        .Where(p => p.CustomDomain == ctx.Request.Host.Host.ToLowerInvariant()
                                    && p.ShowcaseSlug != null && p.ArchivedAt == null)
                        .Select(p => new { p.OwnerId, p.ShowcaseSlug })
                        .FirstOrDefaultAsync(token);
                    return project is null ? null : $"{project.OwnerId}|{project.ShowcaseSlug}";
                }, ctx.RequestAborted);

                // A miss is not a 404: an unknown host is the application's own traffic under a
                // name this server has not been told about — a proxy, a health check, a local
                // alias — and answering those with "not found" would take the app down for them.
                if (claim is not null)
                {
                    var split = claim.IndexOf('|');
                    ctx.RequestServices.GetRequiredService<TenantContext>()
                        .ResolveShowcaseDomain(claim[..split], claim[(split + 1)..]);
                }
                break;
        }

        await next(ctx);
    }

    private bool IsOwnHost(string host) =>
        host.Equals(mainHost, StringComparison.OrdinalIgnoreCase)
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host is "127.0.0.1" or "::1";
}

public static class TenantResolutionExtensions
{
    public static void UseTenantResolution(this WebApplication app)
    {
        var domain = app.Configuration[Consts.General.TenantHostCfg] ?? Consts.URLs.TenantHost;
        var main = new Uri(app.Configuration[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost).Host;
        app.UseMiddleware<TenantResolutionMiddleware>(domain, main,
            app.Services.GetRequiredService<TenantOwnerCache.ForHosts>(),
            app.Services.GetRequiredService<TenantOwnerCache.ForDomains>());
    }
}
