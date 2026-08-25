using Microsoft.Extensions.Configuration;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace CedarClerk.Tests;

// The pipeline, not the renderer. BlogTenantScopingTests call BlogEndpoints.HandleRequest directly,
// which is exactly why a subdomain could serve a marketing page for a whole phase while the blog
// rendered perfectly: nothing exercised the branch that decides who gets to render at all. So these
// build the middleware chain in Program.cs's order — the real predicates, the real static files, a
// stub in place of the blog — and drive requests through it.
public class TenantRoutingTests : IDisposable
{
    private const string AppHost = "cedarclerk.mooexe.dev";
    private const string TenantHost = "beta.cedarclerk.app";

    private const string Shell = "SPA-SHELL";
    private const string Blog = "BLOG";
    private const string Api = "API";
    private const string Font = "WOFF2";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "cedar-routing-" + Guid.NewGuid().ToString("N"));

    private readonly string _downloads = Path.Combine(Path.GetTempPath(), "cedar-downloads-" + Guid.NewGuid().ToString("N"));

    public TenantRoutingTests()
    {
        Directory.CreateDirectory(_downloads);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets", "fonts"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), Shell);
        File.WriteAllText(Path.Combine(_webRoot, "og-default.png"), "PNG");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "fonts", "inter.woff2"), Font);
    }

    public void Dispose()
    {
        Directory.Delete(_webRoot, recursive: true);
        Directory.Delete(_downloads, recursive: true);
    }

    private sealed record Answer(int Status, string Body);

    private sealed class WebRoot(string path) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CedarClerk.Tests";
        public string EnvironmentName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = path;
        public string WebRootPath { get; set; } = path;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(path);
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(path);
    }

    private async Task<Answer> RequestAsync(string host, string path, bool resolvedTenant = false, bool signedIn = false)
    {
        var tenant = new TenantContext();
        if (resolvedTenant) tenant.Resolve(host.Split('.')[0], "owner-of-" + host);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton(tenant);
        services.AddSingleton<IWebHostEnvironment>(new WebRoot(_webRoot));
        var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);

        // What UseRouting has already decided by the time any of the middleware below runs:
        // WebApplication wraps the whole pipeline, so the endpoint is picked before, and executed
        // after, everything Program.cs registers itself.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
                ctx.SetEndpoint(new Endpoint(c => c.Response.WriteAsync(Api), new EndpointMetadataCollection(), "api"));
            else if (!Path.HasExtension(ctx.Request.Path.Value))  // MapFallbackToFile's ":nonfile"
                ctx.SetEndpoint(new Endpoint(c => c.Response.WriteAsync(Shell), new EndpointMetadataCollection(), "fallback"));
            await next();
        });

        app.UseBlogOnlyHost();
        app.UseLanding();
        app.UseWhen(ctx => !TenantRouting.IsTenantRequest(ctx), appHost => appHost.UseDefaultFiles());
        app.UseStaticFiles();
        app.UseDesktopDownloadFiles(_downloads);
        app.MapWhen(ctx => TenantRouting.IsTenantRequest(ctx),
            blogApp => blogApp.Run(ctx => ctx.Response.WriteAsync($"{Blog} {ctx.Request.Path}")));

        app.Run(async ctx =>
        {
            if (ctx.GetEndpoint()?.RequestDelegate is { } endpoint) await endpoint(ctx);
            else ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        var ctx = new DefaultHttpContext { RequestServices = provider };
        ctx.Request.Method = HttpMethods.Get;
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (signedIn) ctx.Request.Headers.Cookie = ".AspNetCore.Identity.Application=x";

        await app.Build().Invoke(ctx);

        ctx.Response.Body.Position = 0;
        return new Answer(ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task A_resolved_subdomain_serves_its_blog_index_and_not_the_landing_page()
    {
        var answer = await RequestAsync(TenantHost, "/", resolvedTenant: true);

        Assert.Equal(StatusCodes.Status200OK, answer.Status);
        Assert.Equal($"{Blog} /", answer.Body);
    }

    [Fact]
    public async Task A_resolved_subdomain_serves_a_post_path()
    {
        var answer = await RequestAsync(TenantHost, "/beta-post", resolvedTenant: true);

        Assert.Equal($"{Blog} /beta-post", answer.Body);
    }

    // The whole defect in one line: the marketing page carries Cache-Control: public, so one copy
    // per registered account would have been cached at the edge and indexed under their own name.
    [Fact]
    public async Task The_landing_page_never_answers_on_a_subdomain()
    {
        var answer = await RequestAsync(TenantHost, "/", resolvedTenant: true);

        Assert.DoesNotContain("waitlist-form", answer.Body);
    }

    [Fact]
    public async Task The_application_shell_is_not_reachable_on_a_subdomain()
    {
        var answer = await RequestAsync(TenantHost, "/index.html", resolvedTenant: true);

        Assert.Equal(StatusCodes.Status404NotFound, answer.Status);
        Assert.DoesNotContain(Shell, answer.Body);
    }

    // 404, not the 401 the application's endpoints answer: the blog branch takes the request before
    // authorization sees the endpoint routing picked, and an unknown path is a missing page there.
    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/health")]
    [InlineData("/api/drafts")]
    public async Task Application_endpoints_are_unreachable_on_a_subdomain(string path)
    {
        var answer = await RequestAsync(TenantHost, path, resolvedTenant: true);

        Assert.DoesNotContain(Api, answer.Body);
        Assert.StartsWith(Blog, answer.Body);
    }

    // The blog's own reader API is on the same shape of path and must still reach the renderer.
    [Fact]
    public async Task The_blogs_own_reader_api_reaches_the_renderer_on_a_subdomain()
    {
        var answer = await RequestAsync(TenantHost, "/api/posts/beta-post/react", resolvedTenant: true);

        Assert.Equal($"{Blog} /api/posts/beta-post/react", answer.Body);
    }

    [Theory]
    [InlineData("/assets/fonts/inter.woff2", Font)]
    [InlineData("/og-default.png", "PNG")]
    public async Task The_files_the_rendered_pages_ask_for_are_still_served_on_a_subdomain(string path, string expected)
    {
        var answer = await RequestAsync(TenantHost, path, resolvedTenant: true);

        Assert.Equal(StatusCodes.Status200OK, answer.Status);
        Assert.Equal(expected, answer.Body);
    }


    [Fact]
    public async Task The_application_host_still_serves_the_landing_page()
    {
        var answer = await RequestAsync(AppHost, "/");

        Assert.Equal(StatusCodes.Status200OK, answer.Status);
        Assert.Contains("waitlist-form", answer.Body);
    }

    [Fact]
    public async Task The_application_host_still_serves_the_app_to_a_signed_in_reader()
    {
        var answer = await RequestAsync(AppHost, "/", signedIn: true);

        Assert.Equal(Shell, answer.Body);
    }

    [Theory]
    [InlineData("/drafts", Shell)]
    [InlineData("/index.html", Shell)]
    [InlineData("/api/auth/me", Api)]
    public async Task The_application_host_is_untouched(string path, string expected)
    {
        var answer = await RequestAsync(AppHost, path);

        Assert.Equal(expected, answer.Body);
    }

    // A subdomain nobody registered never reaches this far — TenantResolutionMiddleware answers 404
    // — but the predicate keys on the resolved tenant rather than on the shape of the host, and a
    // request that never resolved one is an ordinary application request.
    [Fact]
    public async Task An_unresolved_host_is_not_a_blog()
    {
        var answer = await RequestAsync("www.cedarclerk.app", "/");

        Assert.Contains("waitlist-form", answer.Body);
    }


    // T-290. The installers are public artifacts, so this is consistency rather than disclosure:
    // a host that serves one account's blog should not also be a download mirror.
    [Fact]
    public async Task A_tenant_subdomain_does_not_serve_desktop_downloads()
    {
        File.WriteAllText(Path.Combine(_downloads, "latest.yml"), "version: 1.0.0");

        var appHost = await RequestAsync("cedarclerk.app", "/downloads/latest.yml");
        Assert.Equal(StatusCodes.Status200OK, appHost.Status);
        Assert.Contains("version: 1.0.0", appHost.Body);

        // On a subdomain the request falls through to the blog, which is what serves a path it does
        // not know as a 404. What matters here is that the file itself never reached the reader.
        var tenant = await RequestAsync("a.cedarclerk.app", "/downloads/latest.yml", resolvedTenant: true);
        Assert.DoesNotContain("version: 1.0.0", tenant.Body);
        Assert.StartsWith(Blog, tenant.Body);
    }
}
