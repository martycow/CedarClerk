using CedarClerk.Core;
using CedarClerk.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// ADR-216 — the showcase as a site, driven through the real HandleRequest: the ways in, the things
// to look at, the counters, the feed and the address a reader can follow it by.
public class ShowcaseSiteTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Текст."}]}]}""";

    private static Project Seed(CedarDbContext db, Action<Project>? mutate = null, bool withPost = true)
    {
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", IsAdmin = true, TenantUsername = "tenant" });
        var project = new Project
        {
            OwnerId = "o1",
            Name = "Cedar Station",
            Description = "A game about a station.",
            ShowcaseSlug = "cedar-station",
        };
        mutate?.Invoke(project);
        db.Projects.Add(project);
        if (withPost)
            db.Drafts.Add(new Draft
            {
                Title = "Devlog 1",
                CedarJson = Doc,
                OwnerId = "o1",
                ProjectId = project.Id,
                BlogSlug = "devlog-1",
                IsBlogPublished = true,
                BlogPublishedAt = DateTime.UtcNow,
            });
        db.SaveChanges();
        return project;
    }

    private static async Task<HttpContext> Send(CedarDbContext db, string method, string path,
        string query = "", string? showcaseDomainSlug = null, string? host = null)
    {
        var ctx = BlogTestHost.Request(method, path, db, query, host: host, showcaseDomainSlug: showcaseDomainSlug);
        await BlogEndpoints.HandleRequest(ctx);
        return ctx;
    }

    // ---- T-294: the ways in -------------------------------------------------

    [Fact]
    public async Task Index_links_every_public_game()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var body = BlogTestHost.Body(await Send(db, "GET", "/"));

        Assert.Contains("/games/cedar-station", body);
        Assert.Contains("Cedar Station", body);
    }

    [Fact]
    public async Task Index_says_nothing_about_a_project_with_no_page()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseSlug = null);

        Assert.DoesNotContain("Cedar Station", BlogTestHost.Body(await Send(db, "GET", "/")));
    }

    [Fact]
    public async Task A_devlog_post_links_back_to_its_game()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        Assert.Contains("/games/cedar-station", BlogTestHost.Body(await Send(db, "GET", "/devlog-1")));
    }

    [Fact]
    public async Task A_post_of_a_project_with_no_page_links_nowhere()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseSlug = null);

        Assert.DoesNotContain("class=\"post-game\"", BlogTestHost.Body(await Send(db, "GET", "/devlog-1")));
    }

    // ---- T-295: gallery and trailer ----------------------------------------

    [Fact]
    public async Task Renders_the_trailer_through_the_nocookie_embed()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseTrailerUrl = "https://youtu.be/dQw4w9WgXcQ");

        var body = BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station"));

        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", body);
    }

    [Fact]
    public async Task Renders_gallery_images_and_skips_foreign_ones()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseGallery = "/media/shot-1.png\nhttps://elsewhere.example/x.png");

        var body = BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station"));

        Assert.Contains("/media/shot-1.png", body);
        Assert.DoesNotContain("elsewhere.example", body);
    }

    // ---- T-296: counters ----------------------------------------------------

    [Fact]
    public async Task A_view_is_counted_once_per_reader()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var project = Seed(db);

        var first = await Send(db, "GET", "/games/cedar-station");
        Assert.Equal(1, await db.ShowcaseStatDailies.Where(s => s.Kind == ShowcaseStatKinds.View).SumAsync(s => s.Count));

        // The cookie the first response set is what a refreshing reader carries back.
        var cookie = first.Response.Headers.SetCookie.ToString();
        Assert.Contains(Consts.Showcase.ViewedCookiePrefix + project.Id, cookie);

        var again = BlogTestHost.Request("GET", "/games/cedar-station", db);
        again.Request.Headers.Cookie = $"{Consts.Showcase.ViewedCookiePrefix}{project.Id}=1";
        await BlogEndpoints.HandleRequest(again);

        Assert.Equal(1, await db.ShowcaseStatDailies.Where(s => s.Kind == ShowcaseStatKinds.View).SumAsync(s => s.Count));
    }

    [Fact]
    public async Task A_store_link_is_drawn_through_the_counting_path()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseLinks = "Wishlist|https://store.steampowered.com/app/123");

        var body = BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station"));

        Assert.Contains("/games/cedar-station/go/0", body);
    }

    [Fact]
    public async Task A_click_is_counted_by_label_and_redirected()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseLinks = "Wishlist|https://store.steampowered.com/app/123");

        var ctx = await Send(db, "GET", "/games/cedar-station/go/0");

        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode);
        Assert.Equal("https://store.steampowered.com/app/123", ctx.Response.Headers.Location);
        var row = await db.ShowcaseStatDailies.SingleAsync(s => s.Kind == ShowcaseStatKinds.LinkClick);
        Assert.Equal("Wishlist", row.Label);
        Assert.Equal(1, row.Count);
    }

    [Fact]
    public async Task An_index_no_link_carries_is_not_found()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ShowcaseLinks = "Wishlist|https://store.steampowered.com/app/123");

        var ctx = await Send(db, "GET", "/games/cedar-station/go/7");

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Empty(db.ShowcaseStatDailies);
    }

    // ---- T-298: the project's feed -----------------------------------------

    [Fact]
    public async Task The_game_has_a_feed_of_its_own()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var ctx = await Send(db, "GET", "/games/cedar-station/rss.xml");

        Assert.Equal("application/rss+xml; charset=utf-8", ctx.Response.ContentType);
        var body = BlogTestHost.Body(ctx);
        Assert.Contains("<title>Cedar Station</title>", body);
        Assert.Contains("/devlog-1", body);
    }

    [Fact]
    public async Task The_feed_leaves_out_a_post_of_another_project()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);
        db.Drafts.Add(new Draft
        {
            Title = "Other", CedarJson = Doc, OwnerId = "o1", BlogSlug = "other",
            IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        Assert.DoesNotContain("/other", BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station/rss.xml")));
    }

    [Fact]
    public async Task A_listed_private_post_is_a_locked_card_but_never_a_feed_item()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var project = Seed(db);
        db.Drafts.Add(new Draft
        {
            Title = "Patrons only", CedarJson = Doc, OwnerId = "o1", ProjectId = project.Id,
            BlogSlug = "patrons", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
            IsPrivate = true, IsListedWhilePrivate = true,
        });
        db.SaveChanges();

        Assert.Contains("Patrons only", BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station")));
        Assert.DoesNotContain("Patrons only", BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station/rss.xml")));
    }

    // ---- T-299: downloads ---------------------------------------------------

    [Fact]
    public async Task Offers_a_public_build_and_hides_the_rest()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var project = Seed(db);
        db.Builds.AddRange(
            new Build
            {
                OwnerId = "o1", ProjectId = project.Id, Version = "0.4.2",
                IsPublic = true, DownloadUrl = "https://mooexe.itch.io/cedar-station",
                ReleasedAt = DateTime.UtcNow,
            },
            new Build { OwnerId = "o1", ProjectId = project.Id, Version = "0.5.0-wip" });
        db.SaveChanges();

        var body = BlogTestHost.Body(await Send(db, "GET", "/games/cedar-station"));

        Assert.Contains("mooexe.itch.io/cedar-station", body);
        Assert.Contains("0.4.2", body);
        Assert.DoesNotContain("0.5.0-wip", body);
    }

    // ---- T-297: following ---------------------------------------------------

    [Fact]
    public async Task Following_writes_an_unconfirmed_row_and_says_so()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var ctx = await Follow(db, "Reader@Example.com");

        Assert.Equal(StatusCodes.Status303SeeOther, ctx.Response.StatusCode);
        Assert.Contains("follow=sent", ctx.Response.Headers.Location.ToString());
        var row = await db.ShowcaseFollowers.SingleAsync();
        Assert.Equal("reader@example.com", row.Email);
        Assert.Null(row.ConfirmedAt);
        Assert.NotNull(row.ConfirmToken);
    }

    [Fact]
    public async Task An_address_that_is_not_one_is_refused()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var ctx = await Follow(db, "not-an-address");

        Assert.Contains("follow=invalid", ctx.Response.Headers.Location.ToString());
        Assert.Empty(db.ShowcaseFollowers);
    }

    [Fact]
    public async Task Confirming_turns_the_subscription_on_and_spends_the_token()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);
        await Follow(db, "reader@example.com");
        var token = (await db.ShowcaseFollowers.SingleAsync()).ConfirmToken;

        var ctx = await Send(db, "GET", "/games/cedar-station/confirm", $"?token={token}");

        Assert.Contains("follow=confirmed", ctx.Response.Headers.Location.ToString());
        var row = await db.ShowcaseFollowers.SingleAsync();
        Assert.NotNull(row.ConfirmedAt);
        Assert.Null(row.ConfirmToken);
    }

    [Fact]
    public async Task A_token_that_means_nothing_confirms_nothing()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);
        await Follow(db, "reader@example.com");

        var ctx = await Send(db, "GET", "/games/cedar-station/confirm", "?token=invented");

        Assert.Contains("follow=expired", ctx.Response.Headers.Location.ToString());
        Assert.Null((await db.ShowcaseFollowers.SingleAsync()).ConfirmedAt);
    }

    [Fact]
    public async Task Asking_twice_refreshes_the_token_rather_than_adding_a_row()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);
        await Follow(db, "reader@example.com");
        var first = (await db.ShowcaseFollowers.SingleAsync()).ConfirmToken;

        await Follow(db, "reader@example.com");

        var row = await db.ShowcaseFollowers.SingleAsync();
        Assert.NotEqual(first, row.ConfirmToken);
    }

    [Fact]
    public async Task Unsubscribing_deletes_the_row()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);
        await Follow(db, "reader@example.com");
        var token = (await db.ShowcaseFollowers.SingleAsync()).UnsubscribeToken;

        var ctx = await Send(db, "GET", "/games/cedar-station/unsubscribe", $"?token={token}");

        Assert.Contains("follow=left", ctx.Response.Headers.Location.ToString());
        Assert.Empty(db.ShowcaseFollowers);
    }

    [Fact]
    public async Task One_visitor_cannot_follow_endlessly()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        for (var i = 0; i < Consts.Showcase.MaxFollowsPerVisitor; i++)
            await Follow(db, $"reader{i}@example.com");

        var ctx = await Follow(db, "one-too-many@example.com");

        Assert.Contains("follow=toomany", ctx.Response.Headers.Location.ToString());
        Assert.Equal(Consts.Showcase.MaxFollowsPerVisitor, await db.ShowcaseFollowers.CountAsync());
    }

    // ---- T-300: the project's own domain ------------------------------------

    [Fact]
    public async Task A_custom_domain_serves_the_showcase_at_its_root()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.CustomDomain = "cedarstation.example");

        var body = BlogTestHost.Body(await Send(db, "GET", "/",
            showcaseDomainSlug: "cedar-station", host: "cedarstation.example"));

        Assert.Contains("Cedar Station", body);
        // The blog's own back-link would send the reader to a site this domain does not have.
        Assert.DoesNotContain("class=\"back-link\"", body);
    }

    [Fact]
    public async Task On_a_custom_domain_the_pages_paths_lose_the_games_prefix()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p =>
        {
            p.CustomDomain = "cedarstation.example";
            p.ShowcaseLinks = "Wishlist|https://store.steampowered.com/app/123";
        });

        var body = BlogTestHost.Body(await Send(db, "GET", "/",
            showcaseDomainSlug: "cedar-station", host: "cedarstation.example"));

        Assert.Contains("href=\"/go/0\"", body);
        Assert.Contains("href=\"/rss.xml\"", body);
        Assert.DoesNotContain("/games/cedar-station/go/0", body);
    }

    [Fact]
    public async Task A_custom_domains_feed_is_the_projects_feed()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.CustomDomain = "cedarstation.example");

        var ctx = await Send(db, "GET", "/rss.xml",
            showcaseDomainSlug: "cedar-station", host: "cedarstation.example");

        Assert.Contains("<title>Cedar Station</title>", BlogTestHost.Body(ctx));
    }

    // ---- shared -------------------------------------------------------------

    private static async Task<HttpContext> Follow(CedarDbContext db, string email)
    {
        var ctx = BlogTestHost.Request("POST", "/games/cedar-station/follow", db);
        ctx.Request.Form = new FormCollection(new() { ["email"] = email });
        await BlogEndpoints.HandleRequest(ctx);
        return ctx;
    }
}
