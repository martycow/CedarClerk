using CedarClerk.Server;

namespace CedarClerk.Tests;

// ADR-287 — the index and the post share one stylesheet that opts into cross-document view
// transitions; the index names the clicked cover from a click handler, the post names its first
// own picture right after the article. Both halves have to be present or the browser silently
// falls back to a plain navigation, so this checks the strings actually reach both pages.
public class BlogViewTransitionTests
{
    private const string ImageJson =
        """{"type":"doc","content":[{"type":"image","attrs":{"src":"/media/cover.png"}},{"type":"paragraph","content":[{"type":"text","text":"Body."}]}]}""";

    private static void Seed(CedarDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", IsAdmin = true });
        db.Drafts.Add(new Draft
        {
            Title = "Devlog 1",
            CedarJson = ImageJson,
            OwnerId = "o1",
            PrimaryLanguage = "en",
            BlogSlug = "devlog-1",
            IsBlogPublished = true,
            BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private static async Task<string> Get(CedarDbContext db, string path)
    {
        var ctx = BlogTestHost.Request("GET", path, db);
        await BlogEndpoints.HandleRequest(ctx);
        return BlogTestHost.Body(ctx);
    }

    [Fact]
    public async Task The_index_opts_in_and_names_the_clicked_cover_only()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var body = await Get(db, "/");

        Assert.Contains("@view-transition { navigation: auto; }", body);
        Assert.Contains("@view-transition { navigation: none; }", body);
        Assert.Contains("class=\"post-card-cover\"", body);
        Assert.Contains("closest('a.index-card.has-cover')", body);
        Assert.Contains("addEventListener('pageshow'", body);
        Assert.DoesNotContain("view-transition-name", body);
    }

    [Fact]
    public async Task The_post_names_its_first_own_picture_after_the_article()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var body = await Get(db, "/devlog-1");

        Assert.Contains("@view-transition { navigation: auto; }", body);
        var article = body.IndexOf("</article>", StringComparison.Ordinal);
        var script = body.IndexOf("h.style.viewTransitionName='post-cover'", StringComparison.Ordinal);
        Assert.True(article > 0 && script > article);
        Assert.Contains(".post-sheet img[src*=\"/media/\"]", body);
    }
}
