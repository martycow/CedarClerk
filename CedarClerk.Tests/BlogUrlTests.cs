using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Publishing;
using CedarClerk.Server.Tenancy;
using Microsoft.Extensions.Configuration;

namespace CedarClerk.Tests;

// Slugs are per-owner, so a URL is only a URL together with the host that serves it. Built against
// somebody else's host instead, "devlog-1" resolves to that account's post for everybody — and
// these links leave the app for good: into channel history, into an invite mail, into a blog page.
public class BlogUrlTests
{
    private const string Slug = "devlog-1";

    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    private static CedarDbContext TwoOwners()
    {
        var db = BlogTestHost.EmptyDatabase();
        db.Users.Add(new ApplicationUser { Id = "legacy", UserName = "legacy", IsAdmin = true, TenantUsername = "martycow" });
        db.Users.Add(new ApplicationUser { Id = "writer", UserName = "writer", TenantUsername = "writer" });
        db.Users.Add(new ApplicationUser { Id = "nameless", UserName = "nameless" });
        db.SaveChanges();
        return db;
    }

    private static Draft Post(string ownerId, string? slug = Slug, string language = Languages.Russian) => new()
    {
        OwnerId = ownerId, Title = "Post", BlogSlug = slug,
        IsBlogPublished = slug is not null, PrimaryLanguage = language,
    };

    [Fact]
    public async Task The_same_slug_under_two_owners_is_two_different_urls()
    {
        using var db = TwoOwners();

        var legacy = await MicroThreadPlan.BlogUrlAsync(Post("legacy"), Languages.Russian, db, Config());
        var writer = await MicroThreadPlan.BlogUrlAsync(Post("writer"), Languages.Russian, db, Config());

        Assert.Equal($"https://martycow.{Consts.URLs.TenantHost}/{Slug}", legacy);
        Assert.Equal($"https://writer.{Consts.URLs.TenantHost}/{Slug}", writer);
    }

    [Fact]
    public async Task A_translated_cross_link_keeps_the_language_on_the_owners_own_host()
    {
        using var db = TwoOwners();

        var url = await MicroThreadPlan.BlogUrlAsync(Post("writer"), Languages.English, db, Config());

        Assert.Equal($"https://writer.{Consts.URLs.TenantHost}/{Slug}?lang=en", url);
    }

    // No fallback host: any default would name a domain belonging to another account.
    [Fact]
    public async Task An_owner_with_no_host_gets_no_cross_link()
    {
        using var db = TwoOwners();

        Assert.Null(await MicroThreadPlan.BlogUrlAsync(Post("nameless"), Languages.Russian, db, Config()));
    }

    [Fact]
    public async Task An_unpublished_draft_has_no_cross_link()
    {
        using var db = TwoOwners();

        Assert.Null(await MicroThreadPlan.BlogUrlAsync(Post("writer", slug: null), Languages.Russian, db, Config()));
    }

    [Fact]
    public async Task An_invite_link_points_at_the_host_that_can_check_its_token()
    {
        using var db = TwoOwners();

        var site = await BlogTenant.SiteForOwnerAsync(db, Config(), "writer");
        var url = DraftEndpoints.BuildInviteUrl(site, Post("writer"), "TOKEN");

        Assert.Equal($"https://writer.{Consts.URLs.TenantHost}/{Slug}?invite=TOKEN", url);
    }

    [Fact]
    public async Task An_invite_link_carries_its_own_owners_host()
    {
        using var db = TwoOwners();

        var site = await BlogTenant.SiteForOwnerAsync(db, Config(), "legacy");

        Assert.Equal($"https://martycow.{Consts.URLs.TenantHost}/{Slug}?invite=TOKEN",
            DraftEndpoints.BuildInviteUrl(site, Post("legacy"), "TOKEN"));
    }

    [Fact]
    public async Task An_owner_with_no_host_gets_no_invite_link()
    {
        using var db = TwoOwners();

        var site = await BlogTenant.SiteForOwnerAsync(db, Config(), "nameless");

        Assert.Null(site);
        Assert.Null(DraftEndpoints.BuildInviteUrl(site, Post("nameless"), "TOKEN"));
    }

    // The admin post list spans owners, and one query has to answer for all of them.
    [Fact]
    public async Task Hosts_for_a_mixed_set_of_owners_come_back_in_one_batch()
    {
        using var db = TwoOwners();

        var hosts = await BlogTenant.HostsForOwnersAsync(db, Config(), ["legacy", "writer", "nameless", "writer"]);

        Assert.Equal($"martycow.{Consts.URLs.TenantHost}", hosts["legacy"]);
        Assert.Equal($"writer.{Consts.URLs.TenantHost}", hosts["writer"]);
        Assert.False(hosts.ContainsKey("nameless"));
    }

    [Fact]
    public async Task No_owners_means_no_queries_and_no_hosts()
    {
        using var db = TwoOwners();

        Assert.Empty(await BlogTenant.HostsForOwnersAsync(db, Config(), []));
    }
}
