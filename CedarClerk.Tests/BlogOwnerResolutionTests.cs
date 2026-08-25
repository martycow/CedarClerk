using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CedarClerk.Tests;

// The legacy blog host cannot name its owner in its own hostname, so it is looked up. Getting that
// lookup wrong publishes one account's drafts under another's established domain, which is why an
// unresolvable owner is refused rather than guessed at.
public class BlogOwnerResolutionTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Text."}]}]}""";

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    private static void AddUser(CedarDbContext db, string id, bool admin = false, string? username = null)
    {
        db.Users.Add(new ApplicationUser { Id = id, UserName = id, IsAdmin = admin, TenantUsername = username });
        db.SaveChanges();
    }

    [Fact]
    public async Task The_single_admin_owns_the_legacy_blog_when_nothing_is_configured()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");
        AddUser(db, "other");

        Assert.Equal("admin", await BlogTenant.ResolveOwnerIdAsync(db, Config()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Anything_other_than_one_admin_is_unresolvable(int admins)
    {
        using var db = BlogTestHost.EmptyDatabase();
        for (var i = 0; i < admins; i++)
            AddUser(db, $"admin{i}", admin: true);

        Assert.Null(await BlogTenant.ResolveOwnerIdAsync(db, Config()));
    }

    [Fact]
    public async Task The_configured_name_wins_over_the_admin_flag()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");
        AddUser(db, "writer", username: "writer");

        var cfg = Config((Consts.General.BlogOwnerCfg, "WRITER"));

        Assert.Equal("writer", await BlogTenant.ResolveOwnerIdAsync(db, cfg));
    }

    [Fact]
    public async Task A_configured_name_nobody_holds_resolves_to_nothing()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");

        var cfg = Config((Consts.General.BlogOwnerCfg, "typo"));

        Assert.Null(await BlogTenant.ResolveOwnerIdAsync(db, cfg));
    }

    [Fact]
    public async Task The_legacy_host_renders_the_resolved_owners_posts_only()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");
        AddUser(db, "other", username: "other");
        db.Drafts.Add(new Draft
        {
            Title = "Admin's post", CedarJson = Doc, OwnerId = "admin",
            BlogSlug = "post", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.Drafts.Add(new Draft
        {
            Title = "Other's post", CedarJson = Doc, OwnerId = "other",
            BlogSlug = "post", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/post", db);
        await BlogEndpoints.HandleRequest(ctx);
        var body = BlogTestHost.Body(ctx);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Contains("Admin&#39;s post", body);
        Assert.DoesNotContain("Other&#39;s post", body);
        Assert.Contains($"https://{Consts.URLs.BlogHost}/post", body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task An_unresolvable_blog_owner_renders_nothing(int admins)
    {
        using var db = BlogTestHost.EmptyDatabase();
        for (var i = 0; i < admins; i++)
            AddUser(db, $"admin{i}", admin: true);

        var ctx = BlogTestHost.Request("GET", "/", db);
        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        Assert.Equal("", BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task A_mistyped_blog_owner_does_not_fall_back_to_the_admin()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");
        db.Drafts.Add(new Draft
        {
            Title = "Admin's post", CedarJson = Doc, OwnerId = "admin",
            BlogSlug = "post", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/post", db,
            config: new Dictionary<string, string?> { [Consts.General.BlogOwnerCfg] = "typo" });
        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
        Assert.DoesNotContain("Admin", BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task The_legacy_owners_urls_never_move_to_a_subdomain()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");

        Assert.Equal(Consts.URLs.BlogHost, await BlogTenant.HostForOwnerAsync(db, Config(), "admin"));
    }

    [Fact]
    public async Task Another_account_gets_its_own_subdomain()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");
        AddUser(db, "writer", username: "writer");

        Assert.Equal($"writer.{Consts.URLs.TenantHost}", await BlogTenant.HostForOwnerAsync(db, Config(), "writer"));
    }

    [Fact]
    public async Task An_account_with_no_name_has_no_blog_host()
    {
        using var db = BlogTestHost.EmptyDatabase();
        AddUser(db, "admin", admin: true, username: "martycow");
        AddUser(db, "nameless");

        Assert.Null(await BlogTenant.HostForOwnerAsync(db, Config(), "nameless"));
    }
}
