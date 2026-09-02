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
