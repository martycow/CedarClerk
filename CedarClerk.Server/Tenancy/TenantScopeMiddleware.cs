using System.Security.Claims;
using CedarClerk.Core;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Decides, before authentication, which model this request's context gets: filtered to one owner,
/// or unfiltered because the path is the platform's own work.
/// </summary>
public sealed class TenantScopeMiddleware(RequestDelegate next, string blogHost)
{
    public async Task InvokeAsync(HttpContext ctx, TenantProvider tenant, TenantContext resolved)
    {
        // A tenant subdomain already knows whose blog it is, and the reader is anonymous.
        if (resolved.IsTenantRequest)
        {
            tenant.UseTenant(resolved.OwnerId!);
            await next(ctx);
            return;
        }

        // The shared blog still serves every owner's posts from one host and picks its header from
        // whichever account it finds first. Platform is what that *is* today, stated rather than
        // achieved by an empty tenant — making it per-owner is the work the subdomain exists for.
        if (string.Equals(ctx.Request.Host.Host, blogHost, StringComparison.OrdinalIgnoreCase)
            || PlatformPaths.IsPlatform(ctx.Request.Path))
        {
            tenant.UsePlatform();
        }

        await next(ctx);
    }
}

/// <summary>
/// Fills in the tenant of an ordinary signed-in request. Separate from the middleware above, and
/// placed after <c>UseAuthentication</c>, because that is the first moment the owner is known.
/// </summary>
public sealed class TenantFromUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, TenantProvider tenant)
    {
        if (tenant is { IsPlatform: false, TenantId: null }
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
        app.UseMiddleware<TenantScopeMiddleware>(
            app.Configuration[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost);

    public static void UseTenantFromUser(this WebApplication app) =>
        app.UseMiddleware<TenantFromUserMiddleware>();
}
