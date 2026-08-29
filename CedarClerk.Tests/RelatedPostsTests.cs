using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// Wave 1 item 3 — the "read next" cards under a post: same-tag, public, newest first, capped at
// three, and never repeating what the prev/next navigation already offers.
public class RelatedPostsTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Текст."}]}]}""";

    private static Draft Post(string slug, string tags, int day, Action<Draft>? mutate = null)
    {
        var draft = new Draft
        {
            Title = $"Post {slug}",
            CedarJson = Doc,
            OwnerId = "o1",
            BlogSlug = slug,
            IsBlogPublished = true,
            BlogPublishedAt = new DateTime(2026, 1, 1).AddDays(day),
            Tags = tags,
        };
        mutate?.Invoke(draft);
        return draft;
    }

    private static async Task<string> Get(CedarDbContext db, string path)
    {
        var ctx = BlogTestHost.Request("GET", path, db);
        await BlogEndpoints.HandleRequest(ctx);
        return BlogTestHost.Body(ctx);
    }

    // The class name alone also occurs in the shell's stylesheet; only the markup means the
    // section rendered.
    private const string SectionMarker = "<div class=\"related-posts\">";

    [Fact]
    public async Task A_tag_mate_appears_and_an_unrelated_post_does_not()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(Post("mate", "alpha", 1));
        db.Drafts.Add(Post("stranger", "beta", 2));
        db.Drafts.Add(Post("neighbour", "beta", 3));
        db.Drafts.Add(Post("target", "alpha", 4));
        db.SaveChanges();

        var body = await Get(db, "/target");

        Assert.Contains(SectionMarker, body);
        // The stranger shares no tag; the neighbour is already the "older post" card and must not
        // repeat — and it shares no tag anyway.
        var related = body[body.IndexOf(SectionMarker, StringComparison.Ordinal)..];
        Assert.Contains("href=\"/mate\"", related);
        Assert.DoesNotContain("href=\"/stranger\"", related);
        Assert.DoesNotContain("href=\"/neighbour\"", related);
    }

    [Fact]
    public async Task At_most_three_newest_tag_mates_are_shown()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        for (var i = 1; i <= 5; i++)
            db.Drafts.Add(Post($"mate-{i}", "alpha", i));
        db.Drafts.Add(Post("buffer", "beta", 6));
        db.Drafts.Add(Post("target", "alpha", 7));
        db.SaveChanges();

        var body = await Get(db, "/target");
        var related = body[body.IndexOf(SectionMarker, StringComparison.Ordinal)..];

        // buffer is the older-neighbour card, so the three newest mates fill the row.
        Assert.Contains("href=\"/mate-5\"", related);
        Assert.Contains("href=\"/mate-4\"", related);
        Assert.Contains("href=\"/mate-3\"", related);
        Assert.DoesNotContain("href=\"/mate-2\"", related);
        Assert.DoesNotContain("href=\"/mate-1\"", related);
    }

    [Fact]
    public async Task Private_posts_never_appear_listed_or_not()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(Post("hidden", "alpha", 1, d => d.IsPrivate = true));
        db.Drafts.Add(Post("listed", "alpha", 2, d => { d.IsPrivate = true; d.IsListedWhilePrivate = true; }));
        db.Drafts.Add(Post("open", "alpha", 3));
        db.Drafts.Add(Post("buffer", "beta", 4));
        db.Drafts.Add(Post("target", "alpha", 5));
        db.SaveChanges();

        var body = await Get(db, "/target");
        var related = body[body.IndexOf(SectionMarker, StringComparison.Ordinal)..];

        Assert.Contains("href=\"/open\"", related);
        Assert.DoesNotContain("href=\"/hidden\"", related);
        Assert.DoesNotContain("href=\"/listed\"", related);
    }

    [Fact]
    public async Task No_shared_tags_means_no_section_at_all()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(Post("other", "beta", 1));
        db.Drafts.Add(Post("target", "alpha", 2));
        db.SaveChanges();

        var body = await Get(db, "/target");

        Assert.DoesNotContain(SectionMarker, body);
    }
}
