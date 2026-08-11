using CedarClerk.Core;
using CedarClerk.Server;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// 11.08.2026 — Marty published a 25-part thread to X and could not find a link to it anywhere
// afterwards. Every URL was in the database the whole time; nothing read them. These pin what the
// reader that now does is allowed to return.
public class PublishedPostsTests
{
    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    private static readonly DateTime Noon = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    private static PublishJob Job(
        Guid draftId, string network, int partIndex, int partCount, string? url,
        string status = PublishJobStatus.Succeeded, DateTime? finished = null,
        string owner = "u1", string language = "ru", Guid? targetId = null) => new()
        {
            OwnerId = owner,
            DraftId = draftId,
            TargetId = targetId ?? Guid.Empty,
            Network = network,
            Language = language,
            Status = status,
            PartIndex = partIndex,
            PartCount = partCount,
            PublicUrl = url,
            FinishedAt = finished ?? Noon,
        };

    [Fact]
    public async Task A_thread_is_one_row_pointing_at_its_head()
    {
        // The case that started this: 25 parts, 25 stored URLs, and exactly one of them is the
        // address of the thread. The other 24 are replies.
        using var db = NewDb();
        var draft = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
            db.PublishJobs.Add(Job(draft, PublishNetworks.X, i, 25, $"https://x.com/marty_cow/status/{2000 + i}"));
        await db.SaveChangesAsync();

        var posts = await PublishedPosts.LatestAsync(db, "u1");

        var post = Assert.Single(posts);
        Assert.Equal("https://x.com/marty_cow/status/2000", post.PublicUrl);
        Assert.Equal(25, post.PartCount);
    }

    [Fact]
    public async Task Republishing_to_the_same_place_shows_the_newest_link_only()
    {
        using var db = NewDb();
        var draft = Guid.NewGuid();
        db.PublishJobs.AddRange(
            Job(draft, PublishNetworks.X, 0, 1, "https://x.com/a/status/1", finished: Noon.AddHours(-2)),
            Job(draft, PublishNetworks.X, 0, 1, "https://x.com/a/status/2", finished: Noon));
        await db.SaveChangesAsync();

        var post = Assert.Single(await PublishedPosts.LatestAsync(db, "u1"));
        Assert.Equal("https://x.com/a/status/2", post.PublicUrl);
    }

    [Fact]
    public async Task Each_network_language_and_account_is_its_own_row()
    {
        using var db = NewDb();
        var draft = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        db.PublishJobs.AddRange(
            Job(draft, PublishNetworks.X, 0, 1, "https://x.com/a/status/1", targetId: accountA),
            Job(draft, PublishNetworks.X, 0, 1, "https://x.com/b/status/1", targetId: accountB),
            Job(draft, PublishNetworks.X, 0, 1, "https://x.com/a/status/2", language: "en", targetId: accountA),
            Job(draft, PublishNetworks.Bluesky, 0, 1, "https://bsky.app/1", targetId: accountA));
        await db.SaveChangesAsync();

        Assert.Equal(4, (await PublishedPosts.LatestAsync(db, "u1")).Count);
    }

    [Fact]
    public async Task A_failed_publish_is_not_a_post()
    {
        using var db = NewDb();
        db.PublishJobs.Add(Job(Guid.NewGuid(), PublishNetworks.X, 0, 1, null, PublishJobStatus.Failed));
        await db.SaveChangesAsync();
        Assert.Empty(await PublishedPosts.LatestAsync(db, "u1"));
    }

    [Fact]
    public async Task A_success_with_no_public_address_is_left_out()
    {
        // A Telegram channel without a @username has no public URL (ADR-065 found code treating
        // that as "never published"). It is published; it just has no link to offer, and a row
        // with an empty href would be a dead button.
        using var db = NewDb();
        db.PublishJobs.Add(Job(Guid.NewGuid(), PublishNetworks.Telegram, 0, 1, null));
        await db.SaveChangesAsync();
        Assert.Empty(await PublishedPosts.LatestAsync(db, "u1"));
    }

    [Fact]
    public async Task One_persons_posts_are_never_anothers()
    {
        using var db = NewDb();
        db.PublishJobs.Add(Job(Guid.NewGuid(), PublishNetworks.X, 0, 1, "https://x.com/a/status/1", owner: "u2"));
        await db.SaveChangesAsync();
        Assert.Empty(await PublishedPosts.LatestAsync(db, "u1"));
    }

    [Fact]
    public async Task Newest_first_across_drafts()
    {
        using var db = NewDb();
        db.PublishJobs.AddRange(
            Job(Guid.NewGuid(), PublishNetworks.X, 0, 1, "https://x.com/a/status/old", finished: Noon.AddDays(-3)),
            Job(Guid.NewGuid(), PublishNetworks.X, 0, 1, "https://x.com/a/status/new", finished: Noon));
        await db.SaveChangesAsync();

        var posts = await PublishedPosts.LatestAsync(db, "u1");
        Assert.Equal("https://x.com/a/status/new", posts[0].PublicUrl);
    }
}
