using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// The auth cookie must not travel to a tenant subdomain. A Domain of "cedarclerk.app" would send
// the owner's Identity cookie to every tenant blog, including other people's.
public class AuthCookieTests
{
    private static CookieAuthenticationOptions Configured()
    {
        var options = new CookieAuthenticationOptions();
        AuthCookie.Configure(options);
        return options;
    }

    [Fact]
    public void Domain_is_never_set_so_the_cookie_stays_on_the_host_that_issued_it() =>
        Assert.Null(Configured().Cookie.Domain);

    [Fact]
    public void Cookie_is_http_only() =>
        Assert.True(Configured().Cookie.HttpOnly);

    // A cross-site POST carrying the cookie is what SameSite exists to stop, and a tenant
    // subdomain is a different site as far as the browser is concerned only for Domain — so this
    // is the second lock, not a duplicate of the first.
    [Fact]
    public void Cookie_is_same_site_lax_at_least() =>
        Assert.True(Configured().Cookie.SameSite is SameSiteMode.Lax or SameSiteMode.Strict);

    [Fact]
    public void Ticket_and_cookie_expire_together()
    {
        var options = Configured();
        Assert.Equal(Consts.AuthCookieLifetime, options.ExpireTimeSpan);
        Assert.Equal(Consts.AuthCookieLifetime, options.Cookie.MaxAge);
    }

    [Fact]
    public async Task Unauthenticated_api_calls_answer_with_a_status_not_a_redirect()
    {
        var options = Configured();
        var scheme = new AuthenticationScheme("test", null, typeof(CookieAuthenticationHandler));

        var login = new DefaultHttpContext();
        await options.Events.OnRedirectToLogin(new(login, scheme, options, new(), "/login"));
        Assert.Equal(StatusCodes.Status401Unauthorized, login.Response.StatusCode);

        var denied = new DefaultHttpContext();
        await options.Events.OnRedirectToAccessDenied(new(denied, scheme, options, new(), "/denied"));
        Assert.Equal(StatusCodes.Status403Forbidden, denied.Response.StatusCode);
    }
}
