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
public sealed class TenantResolutionMiddleware(RequestDelegate next, string tenantDomain, TenantOwnerCache.ForHosts owners)
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
        }

        await next(ctx);
    }
}

public static class TenantResolutionExtensions
{
    public static void UseTenantResolution(this WebApplication app)
    {
        var domain = app.Configuration[Consts.General.TenantHostCfg] ?? Consts.URLs.TenantHost;
        app.UseMiddleware<TenantResolutionMiddleware>(domain,
            app.Services.GetRequiredService<TenantOwnerCache.ForHosts>());
    }
}
