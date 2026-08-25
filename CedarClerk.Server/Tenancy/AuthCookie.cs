using CedarClerk.Core;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// The Identity cookie's settings, in a class rather than a lambda in Program.cs so that
/// <c>AuthCookieTests</c> can assert them.
/// </summary>
public static class AuthCookie
{
    public static void Configure(CookieAuthenticationOptions o)
    {
        o.Cookie.HttpOnly = true;

        // Host-only, and stated rather than left to the default. Setting a Domain here would send
        // the signed-in owner's cookie to every <name>.cedarclerk.app — which is other people's
        // content served by the same process.
        o.Cookie.Domain = null;
        o.Cookie.SameSite = SameSiteMode.Lax;

        o.Cookie.MaxAge = Consts.AuthCookieLifetime;
        // Without this the ticket inside the cookie expires after Identity's default 14 days while
        // the cookie itself lives 30 — the shorter one wins and looks like a random logout (T-062).
        o.ExpireTimeSpan = Consts.AuthCookieLifetime;
        o.SlidingExpiration = true;

        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    }
}
