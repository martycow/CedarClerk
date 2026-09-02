using CedarClerk.Server;

namespace CedarClerk.Tests;

public class BlogAuthorLinksTests
{
    private static async Task<string> GetIndex(CedarDbContext db, string ownerId = "o1")
    {
        var ctx = BlogTestHost.Request("GET", "/", db, tenantOwnerId: ownerId);
        await BlogEndpoints.HandleRequest(ctx);
        return BlogTestHost.Body(ctx);
    }

    [Fact]
    public async Task Configured_http_links_render_in_the_shared_header()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var owner = db.Users.Single(u => u.Id == "o1");
        owner.ProfileUrl = "https://maker.example/about?one=1&two=2";
        owner.SocialGithubUrl = "https://github.com/maker";
        owner.SocialItchUrl = "https://maker.itch.io/game";
        db.SaveChanges();

        var body = await GetIndex(db);

        Assert.Contains("id=\"authorLinksBtn\"", body);
        Assert.Contains("aria-controls=\"authorLinksMenu\"", body);
        Assert.Contains("Author links", body);
        Assert.Contains("href=\"https://maker.example/about?one=1&amp;two=2\"", body);
        Assert.Contains(">GitHub</a>", body);
        Assert.Contains(">itch.io</a>", body);
        Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", body);
        Assert.Contains("mouseenter", body);
    }

    [Fact]
    public async Task Unsafe_and_relative_profile_values_are_not_public_links()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var owner = db.Users.Single(u => u.Id == "o1");
        owner.SocialTwitterUrl = "javascript:alert(1)";
        owner.SocialInstagramUrl = "/maker";
        owner.SocialGithubUrl = "https://github.com/safe";
        db.SaveChanges();

        var body = await GetIndex(db);

        Assert.DoesNotContain("javascript:", body);
        Assert.DoesNotContain("href=\"/maker\"", body);
        Assert.Contains("https://github.com/safe", body);
    }

    [Fact]
    public async Task Empty_or_another_owners_links_do_not_create_the_control()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Users.Add(new ApplicationUser
        {
            Id = "o2",
            UserName = "o2",
            SocialGithubUrl = "https://github.com/not-this-blog",
        });
        db.SaveChanges();

        var body = await GetIndex(db);

        Assert.DoesNotContain("id=\"authorLinksBtn\"", body);
        Assert.DoesNotContain("not-this-blog", body);
    }
}
