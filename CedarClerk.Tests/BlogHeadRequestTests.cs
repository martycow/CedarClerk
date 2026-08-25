using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// 13.08.2026: the blog answered 404 to HEAD while answering 200 to GET, so the first uptime monitor
// pointed at it reported the site as down from the minute it was created. A monitor that cries wolf
// on day one is worse than no monitor — it teaches you to ignore it.
public class BlogHeadRequestTests
{
    [Theory]
    [InlineData("HEAD")]
    [InlineData("GET")]
    public async Task The_index_answers_reads(string method)
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var ctx = BlogTestHost.Request(method, "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        Assert.NotEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task A_write_to_a_page_is_still_refused()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var ctx = BlogTestHost.Request("POST", "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task The_index_shows_the_primary_language()
    {
        // T-186 — the card badge said "RU" for every post while the primary language has been
        // per-draft since ADR-064.
        using var db = BlogTestHost.EmptyDatabase();
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", IsAdmin = true });
        var draft = new Draft
        {
            Title = "Hello",
            CedarJson = """{"type":"doc","content":[]}""",
            OwnerId = "o1",
            PrimaryLanguage = "en",
            BlogSlug = "hello",
            IsBlogPublished = true,
            BlogPublishedAt = DateTime.UtcNow,
        };
        db.Drafts.Add(draft);
        db.DraftTranslations.Add(new DraftTranslation { DraftId = draft.Id, OwnerId = draft.OwnerId, Language = "ru", Title = "Привет" });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/", db);
        await BlogEndpoints.HandleRequest(ctx);
        var body = BlogTestHost.Body(ctx);

        Assert.Contains("post-card-langs\">EN · RU<", body);
        Assert.DoesNotContain("post-card-langs\">RU", body);
    }
}
