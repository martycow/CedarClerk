using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// Wave 1 item 6 — the press kit page: every press field is optional, an empty section is omitted
// rather than rendered blank, and a showcase that never filled any of them still gets a page.
public class PressPageTests
{
    private static Project Seed(CedarDbContext db, Action<Project>? mutate = null)
    {
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", IsAdmin = true });
        var project = new Project { OwnerId = "o1", Name = "Cedar Station", ShowcaseSlug = "cedar-station" };
        mutate?.Invoke(project);
        db.Projects.Add(project);
        db.SaveChanges();
        return project;
    }

    private static async Task<(int Status, string Body)> Get(CedarDbContext db, string path)
    {
        var ctx = BlogTestHost.Request("GET", path, db);
        await BlogEndpoints.HandleRequest(ctx);
        return (ctx.Response.StatusCode, BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task A_bare_showcase_still_gets_a_press_page()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var (status, body) = await Get(db, "/games/cedar-station/press");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("press kit", body);
        Assert.Contains("Cedar Station", body);
        Assert.Contains("/games/cedar-station/press/pack.zip", body);
        // No trailer, no gallery, no press contact — the sections simply are not there. Markup
        // markers, not bare class names, which also occur in the shell's stylesheet.
        Assert.DoesNotContain("<div class=\"showcase-trailer\">", body);
        Assert.DoesNotContain("<a class=\"showcase-shot\"", body);
        // The footer's report link (T-360) is a mailto on every page, so the marker is the press
        // row's label rather than the scheme.
        Assert.DoesNotContain("Press contact", body);
    }

    [Fact]
    public async Task Filled_press_fields_render_in_the_factsheet()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p =>
        {
            p.PressContactEmail = "press@example.com";
            p.PressPrice = "$9.99";
            p.PressEngine = "Godot 4";
            p.PressGenre = "Cozy factory sim";
            p.PressFactsheetRows = "Release date: 2027\nnot-a-row\nPlatforms: PC";
        });

        var (_, body) = await Get(db, "/games/cedar-station/press");

        Assert.Contains("mailto:press@example.com", body);
        Assert.Contains("$9.99", body);
        Assert.Contains("Godot 4", body);
        Assert.Contains("Cozy factory sim", body);
        Assert.Contains("Release date", body);
        Assert.Contains("Platforms", body);
        Assert.DoesNotContain("not-a-row", body);
    }

    [Fact]
    public async Task An_unknown_or_archived_showcase_is_404()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, p => p.ArchivedAt = DateTime.UtcNow);

        var (status, _) = await Get(db, "/games/cedar-station/press");
        var (missing, _) = await Get(db, "/games/nope/press");

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(StatusCodes.Status404NotFound, missing);
    }

    [Fact]
    public async Task The_showcase_page_links_to_its_press_kit()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var (_, body) = await Get(db, "/games/cedar-station");

        Assert.Contains("href=\"/games/cedar-station/press\"", body);
    }
}
