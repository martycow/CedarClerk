using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// ADR-125 — the series landing and the post page's series chrome, driven through the real
// HandleRequest. Visibility is the invariant that matters: the numbering a stranger sees must
// never be shifted by a part they cannot see.
public class BlogSeriesTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Текст."}]}]}""";

    private static (Series Series, Draft[] Parts) Seed(CedarDbContext db, int parts = 3, Action<Draft, int>? mutate = null)
    {
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1" });
        var series = new Series { OwnerId = "o1", Name = "Devlog", Slug = "devlog" };
        db.Series.Add(series);
        var drafts = new Draft[parts];
        for (var i = 0; i < parts; i++)
        {
            drafts[i] = new Draft
            {
                Title = $"Devlog {i + 1}",
                CedarJson = Doc,
                OwnerId = "o1",
                BlogSlug = $"devlog-{i + 1}",
                IsBlogPublished = true,
                BlogPublishedAt = DateTime.UtcNow.AddDays(i),
                SeriesId = series.Id,
                SeriesOrder = i + 1,
            };
            mutate?.Invoke(drafts[i], i);
            db.Drafts.Add(drafts[i]);
        }
        db.SaveChanges();
        return (series, drafts);
    }

    private static async Task<(int Status, string Body)> Get(CedarDbContext db, string path)
    {
        var ctx = BlogTestHost.Request("GET", path, db);
        await BlogEndpoints.HandleRequest(ctx);
        return (ctx.Response.StatusCode, BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task The_series_page_lists_the_parts_in_order()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var (status, body) = await Get(db, "/series/devlog");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("Devlog 1", body);
        Assert.Contains("Devlog 3", body);
        Assert.True(body.IndexOf("devlog-1") < body.IndexOf("devlog-3"));
        Assert.Contains("3 частей", body);
    }

    [Fact]
    public async Task An_unknown_series_is_404()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var (status, _) = await Get(db, "/series/nope");

        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task A_member_post_carries_the_series_line_and_neighbours()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var (_, body) = await Get(db, "/devlog-2");

        Assert.Contains("Часть 2 из 3", body);
        Assert.Contains("href=\"/series/devlog\"", body);
        Assert.Contains("href=\"/devlog-1\"", body);
        Assert.Contains("href=\"/devlog-3\"", body);
    }

    [Fact]
    public async Task An_unlisted_private_part_never_shifts_the_numbering()
    {
        using var db = BlogTestHost.EmptyDatabase();
        // Part 2 of 3 is private and unlisted: strangers see a two-part series, and part 3
        // becomes their part 2 — with prev pointing across the hole.
        Seed(db, mutate: (d, i) => { if (i == 1) d.IsPrivate = true; });

        var (_, seriesBody) = await Get(db, "/series/devlog");
        var (_, thirdBody) = await Get(db, "/devlog-3");

        Assert.DoesNotContain("devlog-2", seriesBody);
        Assert.Contains("2 частей", seriesBody);
        Assert.Contains("Часть 2 из 2", thirdBody);
        Assert.Contains("href=\"/devlog-1\"", thirdBody);
        Assert.DoesNotContain("href=\"/devlog-2\"", thirdBody);
    }
}
