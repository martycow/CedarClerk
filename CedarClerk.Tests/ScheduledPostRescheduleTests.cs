using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Wave 2 item 9 — the calendar drag: only a Pending post moves; Sent and Failed answer 409, a
// foreign or unknown id answers 404 and leaks nothing.
public class ScheduledPostRescheduleTests
{
    private const string A = "owner-a";
    private const string B = "owner-b";

    private static readonly DateTime OldDate = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewDate = new(2026, 9, 3, 18, 30, 0, DateTimeKind.Utc);

    private static Microsoft.Data.Sqlite.SqliteConnection SharedDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var seed = Open(connection);
        seed.Database.EnsureCreated();
        seed.Users.Add(new ApplicationUser { Id = A, UserName = "a@x.test", Email = "a@x.test" });
        seed.Users.Add(new ApplicationUser { Id = B, UserName = "b@x.test", Email = "b@x.test" });
        seed.SaveChanges();
        return connection;
    }

    private static CedarDbContext Open(Microsoft.Data.Sqlite.SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());

    private static Guid SeedPost(Microsoft.Data.Sqlite.SqliteConnection connection, string status)
    {
        using var db = Open(connection);
        var post = new ScheduledPost
        {
            OwnerId = A, DraftId = Guid.NewGuid(), ChatId = "-1001", ScheduledAtUtc = OldDate, Status = status,
        };
        db.ScheduledPosts.Add(post);
        db.SaveChanges();
        return post.Id;
    }

    [Fact]
    public async Task A_pending_post_moves_and_the_move_persists()
    {
        using var connection = SharedDatabase();
        var id = SeedPost(connection, "Pending");

        using (var db = Open(connection))
        {
            var (status, post) = await ScheduledPostEndpoints.RescheduleAsync(db, A, id, NewDate);
            Assert.Equal(StatusCodes.Status200OK, status);
            Assert.Equal(NewDate, post!.ScheduledAtUtc);
        }

        using var check = Open(connection);
        Assert.Equal(NewDate, check.ScheduledPosts.Single(p => p.Id == id).ScheduledAtUtc);
    }

    [Theory]
    [InlineData("Sent")]
    [InlineData("Failed")]
    public async Task A_settled_post_answers_conflict_and_stays_put(string settled)
    {
        using var connection = SharedDatabase();
        var id = SeedPost(connection, settled);

        using var db = Open(connection);
        var (status, _) = await ScheduledPostEndpoints.RescheduleAsync(db, A, id, NewDate);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal(OldDate, db.ScheduledPosts.Single(p => p.Id == id).ScheduledAtUtc);
    }

    [Fact]
    public async Task Another_owners_id_is_a_plain_not_found()
    {
        using var connection = SharedDatabase();
        var id = SeedPost(connection, "Pending");

        using var db = Open(connection);
        var (status, _) = await ScheduledPostEndpoints.RescheduleAsync(db, B, id, NewDate);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(OldDate, db.ScheduledPosts.Single(p => p.Id == id).ScheduledAtUtc);
    }

    [Fact]
    public async Task The_calendar_list_narrows_to_one_projects_documents()
    {
        using var connection = SharedDatabase();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        using (var seed = Open(connection))
        {
            var inMine = new Draft { OwnerId = A, Title = "in", ProjectId = mine };
            var inOther = new Draft { OwnerId = A, Title = "out", ProjectId = other };
            seed.Drafts.AddRange(inMine, inOther);
            seed.ScheduledPosts.Add(new ScheduledPost { OwnerId = A, DraftId = inMine.Id, ChatId = "-1", ScheduledAtUtc = OldDate, Status = "Pending" });
            seed.ScheduledPosts.Add(new ScheduledPost { OwnerId = A, DraftId = inOther.Id, ChatId = "-1", ScheduledAtUtc = OldDate, Status = "Pending" });
            seed.SaveChanges();
        }

        using var db = Open(connection);
        Assert.Equal(2, await ScheduledPostEndpoints.Owned(db, A, null).CountAsync());
        var scoped = await ScheduledPostEndpoints.Owned(db, A, mine).ToListAsync();
        Assert.Single(scoped);
        Assert.Equal(0, await ScheduledPostEndpoints.Owned(db, B, mine).CountAsync());
    }
}
