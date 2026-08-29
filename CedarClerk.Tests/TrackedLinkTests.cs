using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Wave 2 item 16 — codes, the reuse-or-create contract, collision retry against the global unique
// index, and the click counting the /l/{code} redirect does. Owner scoping rides the tenant
// filter, asserted here for the new table.
public class TrackedLinkTests
{
    private const string A = "owner-a";
    private const string B = "owner-b";

    private static Microsoft.Data.Sqlite.SqliteConnection SharedDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var seed = AsPlatform(connection);
        seed.Database.EnsureCreated();
        seed.Users.Add(new ApplicationUser { Id = A, UserName = "a@x.test", Email = "a@x.test" });
        seed.Users.Add(new ApplicationUser { Id = B, UserName = "b@x.test", Email = "b@x.test" });
        seed.SaveChanges();
        return connection;
    }

    private static CedarDbContext Open(Microsoft.Data.Sqlite.SqliteConnection connection, TenantProvider tenant) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, tenant);

    private static CedarDbContext AsPlatform(Microsoft.Data.Sqlite.SqliteConnection c) => Open(c, TenantProvider.Platform());
    private static CedarDbContext AsTenant(Microsoft.Data.Sqlite.SqliteConnection c, string owner) => Open(c, TenantProvider.For(owner));

    [Fact]
    public void Codes_are_eight_base62_characters()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = TrackedLinkEndpoints.NewCode();
            Assert.Equal(TrackedLinkEndpoints.CodeLength, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Theory]
    [InlineData("https://example.test/post", true)]
    [InlineData("http://example.test", true)]
    [InlineData("ftp://example.test", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    [InlineData("not a url", false)]
    public void Only_absolute_http_urls_are_accepted(string url, bool valid)
    {
        Assert.Equal(valid, TrackedLinkEndpoints.IsValidUrl(url));
    }

    [Fact]
    public async Task Asking_twice_for_the_same_thing_reuses_the_link()
    {
        using var connection = SharedDatabase();
        using var db = AsTenant(connection, A);
        var draftId = Guid.NewGuid();

        var first = await TrackedLinkEndpoints.CreateOrReuseAsync(db, A, "https://x.test/p", draftId, "telegram");
        var again = await TrackedLinkEndpoints.CreateOrReuseAsync(db, A, "https://x.test/p", draftId, "telegram");
        var other = await TrackedLinkEndpoints.CreateOrReuseAsync(db, A, "https://x.test/p", draftId, "bluesky");

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(2, db.TrackedLinks.Count());
    }

    [Fact]
    public async Task A_code_collision_with_another_owner_is_retried()
    {
        using var connection = SharedDatabase();
        using (var platform = AsPlatform(connection))
        {
            platform.TrackedLinks.Add(new TrackedLink { OwnerId = A, Code = "AAAAAAAA", Url = "https://a.test" });
            platform.SaveChanges();
        }

        // Owner B's context cannot even see A's row — the retry has to look past the filter.
        using var db = AsTenant(connection, B);
        var codes = new Queue<string>(["AAAAAAAA", "BBBBBBBB"]);
        var link = await TrackedLinkEndpoints.CreateOrReuseAsync(db, B, "https://b.test", null, null, codes.Dequeue);

        Assert.Equal("BBBBBBBB", link.Code);
    }

    [Fact]
    public async Task A_click_increments_the_counter_and_the_day_row()
    {
        using var connection = SharedDatabase();
        using (var seed = AsPlatform(connection))
        {
            seed.TrackedLinks.Add(new TrackedLink { OwnerId = A, Code = "CODE0001", Url = "https://x.test/p" });
            seed.SaveChanges();
        }

        using var db = AsPlatform(connection);
        Assert.True(await TrackedLinkEndpoints.RecordClickAndRedirectAsync(db, "CODE0001"));
        Assert.True(await TrackedLinkEndpoints.RecordClickAndRedirectAsync(db, "CODE0001"));

        var link = db.TrackedLinks.Single();
        Assert.Equal(2, link.ClickCount);
        Assert.NotNull(link.LastClickAt);
        var daily = Assert.Single(db.TrackedLinkClickDailies.ToList());
        Assert.Equal(2, daily.Clicks);
        Assert.Equal(A, daily.OwnerId);
    }

    [Fact]
    public async Task An_unknown_code_resolves_nothing()
    {
        using var connection = SharedDatabase();
        using var db = AsPlatform(connection);
        Assert.False(await TrackedLinkEndpoints.RecordClickAndRedirectAsync(db, "NOPE0000"));
    }

    [Fact]
    public async Task Listing_never_crosses_owners()
    {
        using var connection = SharedDatabase();
        using (var platform = AsPlatform(connection))
        {
            platform.TrackedLinks.Add(new TrackedLink { OwnerId = A, Code = "AAAA0001", Url = "https://a.test" });
            platform.TrackedLinks.Add(new TrackedLink { OwnerId = B, Code = "BBBB0001", Url = "https://b.test" });
            platform.SaveChanges();
        }

        using var db = AsTenant(connection, A);
        Assert.Equal(["AAAA0001"], await db.TrackedLinks.Select(l => l.Code).ToListAsync());
    }
}
