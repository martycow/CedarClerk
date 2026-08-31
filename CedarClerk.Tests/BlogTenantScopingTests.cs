using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// Phase 4 — a blog belongs to one account. Two owners hold the same slug here on purpose: before
// this, every lookup resolved by slug alone, so whichever row came back first was the page every
// blog surface showed. The database is a platform context (no owner filter in the model at all),
// so anything that stays scoped below does so because the query says the owner, not because the
// filter caught it.
public class BlogTenantScopingTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Text."}]}]}""";

    private const string A = "owner-a";
    private const string B = "owner-b";

    private static CedarDbContext TwoBlogs()
    {
        var db = BlogTestHost.EmptyDatabase();
        db.Users.Add(new ApplicationUser { Id = A, UserName = "a", TenantUsername = "a" });
        db.Users.Add(new ApplicationUser { Id = B, UserName = "b", TenantUsername = "b" });
        db.SaveChanges();

        foreach (var (owner, title) in new[] { (A, "A's post"), (B, "B's post") })
        {
            db.Drafts.Add(new Draft
            {
                Title = title,
                CedarJson = Doc,
                OwnerId = owner,
                BlogSlug = "shared-slug",
                Tags = owner == A ? "alpha" : "bravo",
                IsBlogPublished = true,
                BlogPublishedAt = DateTime.UtcNow,
            });
        }
        db.SaveChanges();
        return db;
    }

    private static async Task<(int Status, string Body)> Get(CedarDbContext db, string path, string owner, string host)
    {
        var ctx = BlogTestHost.Request("GET", path, db, host: host, tenantOwnerId: owner);
        await BlogEndpoints.HandleRequest(ctx);
        return (ctx.Response.StatusCode, BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task One_slug_owned_twice_renders_the_requesting_tenants_post()
    {
        using var db = TwoBlogs();

        var (statusA, bodyA) = await Get(db, "/shared-slug", A, "a.cedarclerk.app");
        var (statusB, bodyB) = await Get(db, "/shared-slug", B, "b.cedarclerk.app");

        Assert.Equal(StatusCodes.Status200OK, statusA);
        Assert.Equal(StatusCodes.Status200OK, statusB);
        Assert.Contains("A&#39;s post", bodyA);
        Assert.DoesNotContain("B&#39;s post", bodyA);
        Assert.Contains("B&#39;s post", bodyB);
        Assert.DoesNotContain("A&#39;s post", bodyB);
    }

    [Fact]
    public async Task A_post_url_is_built_on_the_tenants_own_host()
    {
        using var db = TwoBlogs();

        var (_, body) = await Get(db, "/shared-slug", A, "a.cedarclerk.app");

        Assert.Contains("https://a.cedarclerk.app/shared-slug", body);
        Assert.DoesNotContain("blog.mooexe.dev", body);
    }

    [Fact]
    public async Task The_index_lists_only_the_tenants_posts_and_tags()
    {
        using var db = TwoBlogs();

        var (status, body) = await Get(db, "/", A, "a.cedarclerk.app");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("A&#39;s post", body);
        Assert.DoesNotContain("B&#39;s post", body);
        Assert.Contains("alpha", body);
        Assert.DoesNotContain("bravo", body);
    }

    [Fact]
    public async Task The_feed_carries_only_the_tenants_posts()
    {
        using var db = TwoBlogs();

        var (status, body) = await Get(db, "/rss.xml", B, "b.cedarclerk.app");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("B&#39;s post", body);
        Assert.DoesNotContain("A&#39;s post", body);
        Assert.Contains("https://b.cedarclerk.app/", body);
    }

    [Fact]
    public async Task Another_tenants_engagement_is_not_counted_on_the_index()
    {
        using var db = TwoBlogs();
        var theirs = db.Drafts.Single(d => d.OwnerId == B);
        db.Reactions.Add(new Reaction { DraftId = theirs.Id, OwnerId = B, Kind = "like", VisitorHash = "v1" });
        db.Comments.Add(new Comment { DraftId = theirs.Id, OwnerId = B, Text = "Nice", AuthorName = "Someone" });
        db.SaveChanges();

        var (_, mine) = await Get(db, "/", A, "a.cedarclerk.app");
        var (_, hers) = await Get(db, "/", B, "b.cedarclerk.app");

        // The stats row renders eye/thumb/chat counts in that order; A's post has no engagement.
        var mineStats = System.Text.RegularExpressions.Regex.Match(mine, "<div class=\"post-card-stats\">(.*?)</div>").Value;
        var herStats = System.Text.RegularExpressions.Regex.Match(hers, "<div class=\"post-card-stats\">(.*?)</div>").Value;
        Assert.NotEmpty(mineStats);
        Assert.NotEmpty(herStats);
        Assert.Contains("""<span class="num">0</span>""", mineStats);
        Assert.DoesNotContain("""<span class="num">1</span>""", mineStats);
        Assert.Contains("""<span class="num">1</span>""", herStats);
    }

    [Fact]
    public async Task A_series_slug_is_not_resolvable_from_another_tenants_blog()
    {
        using var db = TwoBlogs();
        db.Series.Add(new Series { OwnerId = B, Name = "B's series", Slug = "devlog" });
        db.SaveChanges();

        var (mine, _) = await Get(db, "/series/devlog", A, "a.cedarclerk.app");
        var (hers, hersBody) = await Get(db, "/series/devlog", B, "b.cedarclerk.app");

        Assert.Equal(StatusCodes.Status404NotFound, mine);
        Assert.Equal(StatusCodes.Status200OK, hers);
        Assert.Contains("B&#39;s series", hersBody);
    }

    [Fact]
    public async Task A_showcase_slug_is_not_resolvable_from_another_tenants_blog()
    {
        using var db = TwoBlogs();
        db.Projects.Add(new Project { OwnerId = B, Name = "B's game", ShowcaseSlug = "roguelike" });
        db.SaveChanges();

        var (mine, _) = await Get(db, "/games/roguelike", A, "a.cedarclerk.app");
        var (hers, hersBody) = await Get(db, "/games/roguelike", B, "b.cedarclerk.app");

        Assert.Equal(StatusCodes.Status404NotFound, mine);
        Assert.Equal(StatusCodes.Status200OK, hers);
        Assert.Contains("B&#39;s game", hersBody);
    }

    [Fact]
    public async Task The_header_identity_is_the_resolved_owners_channel()
    {
        using var db = TwoBlogs();
        db.Channels.Add(new Channel { OwnerId = A, Title = "A's channel", TelegramChatId = 1 });
        db.Channels.Add(new Channel { OwnerId = B, Title = "B's channel", TelegramChatId = 2 });
        db.SaveChanges();

        var (_, body) = await Get(db, "/", B, "b.cedarclerk.app");

        Assert.Contains("B&#39;s channel", body);
        Assert.DoesNotContain("A&#39;s channel", body);
    }

    [Fact]
    public async Task A_comment_cannot_be_posted_onto_a_slug_the_tenant_does_not_own()
    {
        using var db = TwoBlogs();
        db.Drafts.Add(new Draft
        {
            Title = "B only", CedarJson = Doc, OwnerId = B,
            BlogSlug = "b-only", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("POST", "/api/posts/b-only/comments", db,
            host: "a.cedarclerk.app", tenantOwnerId: A);
        ctx.Request.Body = new MemoryStream("""{"text":"hello","authorName":"Reader"}"""u8.ToArray());
        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Empty(db.Comments);
    }

    [Fact]
    public async Task Annotations_of_another_tenants_post_are_not_reachable()
    {
        using var db = TwoBlogs();
        var theirs = new Draft
        {
            Title = "B only", CedarJson = Doc, OwnerId = B,
            BlogSlug = "b-only", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow,
        };
        db.Drafts.Add(theirs);
        db.Comments.Add(new Comment { DraftId = theirs.Id, OwnerId = B, Text = "B's comment", AuthorName = "Someone" });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/api/posts/b-only/annotations", db,
            host: "a.cedarclerk.app", tenantOwnerId: A);
        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.DoesNotContain("B's comment", BlogTestHost.Body(ctx));
    }
}
