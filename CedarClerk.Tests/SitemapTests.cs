using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// Wave 1 item 4 — the sitemap lists exactly the pages a stranger can open, as absolute URLs on the
// tenant's own host. Private posts are absent whether listed or not: an invitation to crawl is not
// the index card the owner opted into.
public class SitemapTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Текст."}]}]}""";

    private static async Task<(int Status, string Body)> Get(CedarDbContext db)
    {
        var ctx = BlogTestHost.Request("GET", "/sitemap.xml", db);
        await BlogEndpoints.HandleRequest(ctx);
        return (ctx.Response.StatusCode, BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task Public_posts_series_showcases_and_press_pages_are_listed_absolutely()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft
        {
            Title = "Open", CedarJson = Doc, OwnerId = "o1", BlogSlug = "open",
            IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.Series.Add(new Series { OwnerId = "o1", Name = "Devlog", Slug = "devlog" });
        db.Projects.Add(new Project { OwnerId = "o1", Name = "Game", ShowcaseSlug = "game" });
        db.SaveChanges();

        var (status, body) = await Get(db);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.StartsWith("<?xml", body);
        Assert.Contains("<loc>https://tenant.", body);
        Assert.Contains("/open</loc>", body);
        Assert.Contains("/series/devlog</loc>", body);
        Assert.Contains("/showcase/game</loc>", body);
        Assert.Contains("/showcase/game/press</loc>", body);
        Assert.Contains("<lastmod>", body);
    }

    [Fact]
    public async Task Private_posts_are_absent_listed_or_not()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft
        {
            Title = "Hidden", CedarJson = Doc, OwnerId = "o1", BlogSlug = "hidden",
            IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow, IsPrivate = true,
        });
        db.Drafts.Add(new Draft
        {
            Title = "Listed", CedarJson = Doc, OwnerId = "o1", BlogSlug = "listed",
            IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
            IsPrivate = true, IsListedWhilePrivate = true,
        });
        db.SaveChanges();

        var (_, body) = await Get(db);

        Assert.DoesNotContain("hidden", body);
        Assert.DoesNotContain("listed", body);
    }

    [Fact]
    public async Task Another_owners_posts_never_leak_in()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Users.Add(new ApplicationUser { Id = "o2", UserName = "o2" });
        db.Drafts.Add(new Draft
        {
            Title = "Mine", CedarJson = Doc, OwnerId = "o1", BlogSlug = "mine",
            IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.Drafts.Add(new Draft
        {
            Title = "Theirs", CedarJson = Doc, OwnerId = "o2", BlogSlug = "theirs",
            IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var (_, body) = await Get(db);

        Assert.Contains("/mine</loc>", body);
        Assert.DoesNotContain("theirs", body);
    }
}
