using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// Wave 1 item 8 — the token is the whole capability: it renders the current body without auth,
// a revoked or wrong one is a plain 404, and robots are told to stay out. Not on BlogTestHost:
// the page answers on the app host and opens its own platform scope, so the harness here registers
// exactly what that scope resolves.
public class DraftPreviewPageTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Секретный черновик."}]}]}""";

    private static (HttpContext Ctx, CedarDbContext Db) Harness(string path, string? routeToken = null)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;

        var setup = new CedarDbContext(options, TenantProvider.Platform());
        setup.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddScoped(sp => new CedarDbContext(options, sp.GetRequiredService<TenantProvider>()));

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        if (routeToken is not null)
            ctx.Request.RouteValues["token"] = routeToken;
        ctx.Response.Body = new MemoryStream();
        return (ctx, setup);
    }

    private static string Body(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static Draft SeedDraft(CedarDbContext db, string? previewToken)
    {
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1" });
        var draft = new Draft
        {
            Title = "Working title",
            CedarJson = Doc,
            OwnerId = "o1",
            PreviewToken = previewToken,
        };
        db.Drafts.Add(draft);
        db.SaveChanges();
        return draft;
    }

    [Fact]
    public async Task A_valid_token_renders_the_body_read_only_with_noindex()
    {
        var (ctx, db) = Harness("/preview/tok-1", "tok-1");
        using var _ = db;
        SeedDraft(db, "tok-1");

        await BlogEndpoints.HandleDraftPreviewAsync(ctx);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        var body = Body(ctx);
        Assert.Contains("Секретный черновик.", body);
        Assert.Contains("noindex, nofollow", body);
        Assert.Contains("preview-banner", body);
        // Read-only: no reactions, comments or annotation controls anywhere on the page. Markup
        // markers, not bare class names — those also occur in the shell's own stylesheet.
        Assert.DoesNotContain("<div class=\"annotation", body);
        Assert.DoesNotContain("<form class=\"comment-form\"", body);
    }

    [Fact]
    public async Task A_wrong_token_is_a_plain_404()
    {
        var (ctx, db) = Harness("/preview/wrong", "wrong");
        using var _ = db;
        SeedDraft(db, "tok-1");

        await BlogEndpoints.HandleDraftPreviewAsync(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task A_revoked_link_stops_working_immediately()
    {
        var (ctx, db) = Harness("/preview/tok-1", "tok-1");
        using var _ = db;
        var draft = SeedDraft(db, "tok-1");

        draft.PreviewToken = null;
        db.SaveChanges();

        await BlogEndpoints.HandleDraftPreviewAsync(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task The_page_needs_no_signed_in_user_and_no_tenant_host()
    {
        // The harness resolves no tenant at all — exactly the app host's anonymous state. Rendering
        // proves the handler's own platform scope does the cross-owner lookup.
        var (ctx, db) = Harness("/preview/tok-1", "tok-1");
        using var _ = db;
        SeedDraft(db, "tok-1");

        await BlogEndpoints.HandleDraftPreviewAsync(ctx);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }
}
