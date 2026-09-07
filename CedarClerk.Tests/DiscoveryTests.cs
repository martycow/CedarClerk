using CedarClerk.Localization;
using CedarClerk.Core;
using CedarClerk.Server;
using Microsoft.Extensions.Configuration;

namespace CedarClerk.Tests;

public class DiscoveryTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cedar:Modules:IndieDev"] = "true",
            [Consts.General.TenantHostCfg] = "blogs.test",
        })
        .Build();

    [Fact]
    public async Task Only_opted_in_fully_public_posts_are_loaded()
    {
        using var db = BlogTestHost.EmptyDatabase();
        db.Users.AddRange(
            new ApplicationUser { Id = "in", UserName = "in", TenantUsername = "inside", DiscoveryOptIn = true },
            new ApplicationUser { Id = "out", UserName = "out", TenantUsername = "outside", DiscoveryOptIn = false });
        db.Drafts.AddRange(
            Post("in", "public", isPrivate: false),
            Post("in", "listed-private", isPrivate: true, listed: true),
            Post("out", "no-consent", isPrivate: false));
        await db.SaveChangesAsync();

        var result = await DiscoveryEndpoints.LoadAsync(db, Configuration());

        var item = Assert.Single(result.Blogs);
        Assert.Equal("public", item.Title);
        Assert.Equal("https://inside.blogs.test/public", item.Url);
        Assert.DoesNotContain(result.Blogs, i => i.Title == "listed-private");
        Assert.DoesNotContain(result.Blogs, i => i.Title == "no-consent");
    }

    [Fact]
    public async Task Independent_blog_and_project_devlog_remain_distinct()
    {
        using var db = BlogTestHost.EmptyDatabase();
        db.Users.Add(new ApplicationUser
        {
            Id = "owner", UserName = "owner", TenantUsername = "maker", DiscoveryOptIn = true,
        });
        var project = new Project
        {
            OwnerId = "owner", Name = "Mosslight", ShowcaseSlug = "mosslight",
            DiscoveryCategory = DiscoveryCategories.Games,
        };
        db.Projects.Add(project);
        db.Drafts.AddRange(Post("owner", "Notebook", false), Post("owner", "Build 12", false, project.Id));
        await db.SaveChangesAsync();

        var result = await DiscoveryEndpoints.LoadAsync(db, Configuration());

        Assert.Single(result.Projects);
        Assert.Contains(result.Blogs, i => i.Kind == "blog" && i.Title == "Notebook");
        Assert.Contains(result.Blogs, i => i.Kind == "devlog" && i.ProjectName == "Mosslight");
    }

    [Fact]
    public async Task ScreenshotSaturday_requires_an_exact_tag_and_public_cover()
    {
        using var db = BlogTestHost.EmptyDatabase();
        db.Users.Add(new ApplicationUser
        {
            Id = "owner", UserName = "owner", TenantUsername = "maker", DiscoveryOptIn = true,
        });
        var tagged = Post("owner", "Saturday", false);
        tagged.Tags = "#ScreenshotSaturday, pixel-art";
        tagged.CoverImagePath = "/media/asset_aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.png";
        var almost = Post("owner", "Almost", false);
        almost.Tags = "ScreenshotSaturdays";
        almost.CoverImagePath = "/media/asset_bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb.png";
        db.Drafts.AddRange(tagged, almost);
        await db.SaveChangesAsync();

        var result = await DiscoveryEndpoints.LoadAsync(db, Configuration());

        Assert.Equal("Saturday", result.Stage?.Title);
        Assert.True(result.Stage?.ScreenshotSaturday);
    }

    [Fact]
    public void Empty_discovery_is_one_honest_invitation()
    {
        var snapshot = Snapshot([], [], eligible: 0);

        var body = Body(DiscoveryEndpoints.Render(false, snapshot, "all", null, ""));

        Assert.Equal(1, Count(body, "class=\"stage-empty discovery-empty\""));
        Assert.DoesNotContain("class=\"feed ", body);
        Assert.DoesNotContain("class=\"category-card\"", body);
        Assert.DoesNotContain("class=\"shuffle\"", body);
        Assert.DoesNotContain("/og-default.png", body);
        Assert.Contains("for=\"discovery-search\"", body);
        Assert.Contains("Publish the first project", body);
        Assert.Equal(1, Count(body, "href=\"/welcome#waitlist\""));
        Assert.Contains("publicly published work", body);
        Assert.Contains("explicitly opt in to Discovery", body);

        var russian = Body(DiscoveryEndpoints.Render(true, snapshot, "all", null, ""));
        Assert.Equal(1, Count(russian, "href=\"/welcome#waitlist\""));
        Assert.Contains("публично опубликованные работы", russian);
        Assert.Contains("сами включили показ в Discovery", russian);
    }

    [Fact]
    public void An_empty_lens_offers_one_way_back_without_fake_sections()
    {
        var snapshot = Snapshot([], [Item("blog", "A real blog")], eligible: 1);

        var body = Body(DiscoveryEndpoints.Render(false, snapshot, "projects", null, ""));

        Assert.Equal(1, Count(body, "class=\"stage-empty discovery-empty\""));
        Assert.Contains("Nothing matches this view", body);
        Assert.Contains("href=\"/discovery?lang=en\"", body);
        Assert.DoesNotContain("class=\"shuffle\"", body);
        Assert.DoesNotContain("Project Showcase", body);
    }

    [Fact]
    public void Blog_and_devlog_lenses_do_not_mix_their_cards()
    {
        var snapshot = Snapshot([], [Item("blog", "Independent notes"), Item("devlog", "Build diary")], eligible: 2);

        var blogs = Body(DiscoveryEndpoints.Render(false, snapshot, "blogs", null, ""));
        var devlogs = Body(DiscoveryEndpoints.Render(false, snapshot, "devlogs", null, ""));

        Assert.Contains("Independent notes", blogs);
        Assert.DoesNotContain("Build diary", blogs);
        Assert.Contains("Build diary", devlogs);
        Assert.DoesNotContain("Independent notes", devlogs);
        Assert.Contains("type=blogs&amp;lang=en", blogs.Replace("&", "&amp;"));
        Assert.Contains("type=devlogs&amp;lang=en", devlogs.Replace("&", "&amp;"));
    }

    [Fact]
    public void Populated_sections_are_numbered_without_gaps_and_zero_categories_are_absent()
    {
        var snapshot = Snapshot(
            [Item("project", "Mosslight", DiscoveryCategories.Games)],
            [Item("devlog", "Build diary", DiscoveryCategories.Games)],
            eligible: 2,
            screenshotSaturday: false);

        var body = Body(DiscoveryEndpoints.Render(false, snapshot, "all", null, ""));

        Assert.Contains("<span>01</span><h2>Project Showcase</h2>", body);
        Assert.Contains("<span>02</span><h2>Project devlogs</h2>", body);
        Assert.Contains("<span>03</span><h2>Project categories</h2>", body);
        Assert.DoesNotContain("<span>04</span>", body);
        Assert.Contains("category=games", body);
        Assert.DoesNotContain("category=hardware", body);
        Assert.DoesNotContain("category=other", body);
    }

    [Fact]
    public void Search_lenses_and_shuffle_share_one_stateful_control_band()
    {
        var snapshot = Snapshot(
            [
                Item("project", "Mosslight One", DiscoveryCategories.Games),
                Item("project", "Mosslight Two", DiscoveryCategories.Games),
            ],
            [],
            eligible: 2,
            screenshotSaturday: false);

        var body = Body(DiscoveryEndpoints.Render(
            false, snapshot, "projects", DiscoveryCategories.Games, "Mosslight"));
        var controlsStart = body.IndexOf("<div class=\"stage-controls\">", StringComparison.Ordinal);

        Assert.True(controlsStart >= 0);
        var controlsEnd = body.IndexOf("</div>", controlsStart, StringComparison.Ordinal);
        Assert.True(controlsEnd > controlsStart);
        var controls = body[controlsStart..(controlsEnd + "</div>".Length)];
        Assert.Equal(1, Count(body, "class=\"stage-controls\""));
        Assert.Equal(1, Count(body, "class=\"search\""));
        Assert.Contains("<form class=\"search\" method=\"get\" action=\"/discovery\">", controls);
        Assert.Contains("<label for=\"discovery-search\">Search Discovery</label>", controls);
        Assert.Contains("name=\"q\" value=\"Mosslight\"", controls);
        Assert.Contains("name=\"lang\" value=\"en\"", controls);
        Assert.Contains("name=\"type\" value=\"projects\"", controls);
        Assert.Contains("name=\"category\" value=\"games\"", controls);
        Assert.Contains("type=projects&lang=en&q=Mosslight\" aria-current=\"page\"", controls);
        Assert.Contains("class=\"shuffle\"", controls);
        Assert.Contains("&lang=en&type=projects&category=games&q=Mosslight", controls);
        Assert.True(controls.IndexOf("class=\"search\"", StringComparison.Ordinal)
            < controls.IndexOf("class=\"segments\"", StringComparison.Ordinal));
        Assert.True(controls.IndexOf("class=\"segments\"", StringComparison.Ordinal)
            < controls.IndexOf("class=\"shuffle\"", StringComparison.Ordinal));

        var headerEnd = body.IndexOf("</header>", StringComparison.Ordinal);
        Assert.InRange(headerEnd, 0, controlsStart - 1);
        Assert.DoesNotContain("class=\"search\"", body[..headerEnd]);
        Assert.Contains("/discovery?lang=ru&type=projects&category=games&q=Mosslight", body[..headerEnd]);
    }

    [Fact]
    public void Discovery_declares_the_editorial_measure_contract()
    {
        var html = DiscoveryEndpoints.Render(false, Snapshot([], [], eligible: 0), "all", null, "");

        Assert.Contains("<main data-layout=\"editorial\">", html);
        Assert.Contains("body { min-height:100vh;", html);
        Assert.Contains("main[data-layout=\"editorial\"] { flex:1 0 auto; }", html);
        Assert.Contains(".wrap { width:min(1380px, calc(100% - 40px));", html);
        Assert.Contains("calc((100vw - 1380px)/2)", html);
        Assert.DoesNotContain("1180px", html);
    }

    private static DiscoveryEndpoints.Snapshot Snapshot(
        IReadOnlyList<DiscoveryEndpoints.Item> projects,
        IReadOnlyList<DiscoveryEndpoints.Item> blogs,
        int eligible,
        bool screenshotSaturday = true)
    {
        var settings = new DiscoveryEndpoints.Settings(true, screenshotSaturday, true, true,
            new LandingText("Discovery", "Discovery"), new LandingText("Intro", "Intro"));
        var stage = screenshotSaturday
            ? blogs.FirstOrDefault(i => i.ScreenshotSaturday) ?? projects.FirstOrDefault()
            : projects.FirstOrDefault();
        return new DiscoveryEndpoints.Snapshot(settings, projects, blogs, stage, 1, eligible);
    }

    private static DiscoveryEndpoints.Item Item(string kind, string title,
        string category = DiscoveryCategories.Other) => new(
        kind, title, $"{title} summary", $"https://maker.test/{Uri.EscapeDataString(title)}",
        "/media/cover.png", "Maker", null, new DateTime(2026, 9, 1), [], category,
        kind == "devlog" ? "Mosslight" : null, null, false);

    private static string Body(string html) => html[(html.IndexOf("<body>", StringComparison.Ordinal) + 6)..];

    private static int Count(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;

    private static Draft Post(string owner, string title, bool isPrivate, Guid? projectId = null, bool listed = false) => new()
    {
        OwnerId = owner,
        Title = title,
        BlogSlug = title.ToLowerInvariant().Replace(' ', '-'),
        IsBlogPublished = true,
        IsPrivate = isPrivate,
        IsListedWhilePrivate = listed,
        ProjectId = projectId,
        BlogPublishedAt = DateTime.UtcNow,
        CedarJson = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"A public preview"}]}]}""",
    };

}
