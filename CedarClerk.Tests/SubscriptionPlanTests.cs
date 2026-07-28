using CedarClerk.Server;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// DB3 (28.07.2026): publishing with no channel connected left the frontend's chatId at a
// leftover dev default ("@testingandfun"), which reached ResolveOwnedChannelAsync's
// c.Username.Equals(username, StringComparison.CurrentCultureIgnoreCase) — untranslatable by
// EF Core's SQLite provider, so it threw at query time instead of returning null. That surfaced
// to the user as a raw 500/DB error instead of the intended clean 403. Fixed by comparing via
// ToLower() (translates to SQL LOWER()) instead of StringComparison.
public class SubscriptionPlanTests
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

    [Fact]
    public async Task ResolveOwnedChannelAsync_by_username_does_not_throw_and_returns_null_when_unowned()
    {
        using var db = NewDb();

        var result = await SubscriptionPlan.ResolveOwnedChannelAsync(db, "some-user-id", "@testingandfun");

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOwnedChannelAsync_by_username_matches_case_insensitively()
    {
        using var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1", Email = "owner-1@test.local" });
        db.Channels.Add(new Channel
        {
            Id = Guid.NewGuid(),
            OwnerId = "owner-1",
            Username = "MyChannel",
            TelegramChatId = 12345,
            Title = "My Channel"
        });
        await db.SaveChangesAsync();

        var result = await SubscriptionPlan.ResolveOwnedChannelAsync(db, "owner-1", "@mychannel");

        Assert.NotNull(result);
        Assert.Equal("MyChannel", result!.Username);
    }
}
