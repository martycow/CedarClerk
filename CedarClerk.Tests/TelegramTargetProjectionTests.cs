using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using CedarClerk.Server.Publishing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// T-085 / ADR-078. The refactor itself is proved unchanged by the smoke suite; this is the one
// piece with new logic — the row that stands for a Telegram channel in the general table. What can
// go wrong with it is duplication (the unique index turns that into a 500 on connect) and a
// backfill that is not safe to run on every start.
public class TelegramTargetProjectionTests
{
    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts, TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    private static Channel SeedChannel(CedarDbContext db, string ownerId = "owner-1", long chatId = -1001234567890, string title = "Dev Diary")
    {
        if (!db.Users.Any(u => u.Id == ownerId))
            db.Users.Add(new ApplicationUser { Id = ownerId, UserName = ownerId, Email = $"{ownerId}@test.local" });

        var channel = new Channel { OwnerId = ownerId, TelegramChatId = chatId, Title = title, Username = "devdiary" };
        db.Channels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    [Fact]
    public async Task Connecting_a_channel_projects_one_target()
    {
        using var db = NewDb();
        var channel = SeedChannel(db);

        var target = await TelegramTargetProjection.EnsureAsync(db, channel);
        await db.SaveChangesAsync();

        Assert.Equal(PublishNetworks.Telegram, target.Network);
        Assert.Equal("owner-1", target.OwnerId);
        Assert.Equal("Dev Diary", target.DisplayName);
        Assert.Equal("-1001234567890", target.RemoteId);
        Assert.Equal(channel.Id, target.ChannelId);
        Assert.True(target.IsActive);
        // Telegram posts through Cedar Clerk's own bot — there is nothing of the tenant's to keep.
        Assert.Null(target.CredentialsProtected);
    }

    [Fact]
    public async Task Ensuring_twice_updates_rather_than_duplicating()
    {
        using var db = NewDb();
        var channel = SeedChannel(db);
        await TelegramTargetProjection.EnsureAsync(db, channel);
        await db.SaveChangesAsync();

        channel.Title = "Dev Diary (renamed)";
        await TelegramTargetProjection.EnsureAsync(db, channel);
        await db.SaveChangesAsync();

        var targets = await db.PublishTargets.ToListAsync();
        Assert.Single(targets);
        Assert.Equal("Dev Diary (renamed)", targets[0].DisplayName);
    }

    // The sequence that would otherwise hit the unique index: disconnect, then connect the same
    // channel again. Free-tier cooldown explicitly allows reconnecting the same channel, so this is
    // an ordinary path, not an edge case.
    [Fact]
    public async Task Reconnecting_a_disconnected_channel_reactivates_the_same_row()
    {
        using var db = NewDb();
        var channel = SeedChannel(db);
        var first = await TelegramTargetProjection.EnsureAsync(db, channel);
        first.LastPublishedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        await TelegramTargetProjection.DeactivateAsync(db, channel);
        db.Channels.Remove(channel);
        await db.SaveChangesAsync();

        Assert.False((await db.PublishTargets.SingleAsync()).IsActive);

        var again = new Channel { OwnerId = "owner-1", TelegramChatId = channel.TelegramChatId, Title = "Dev Diary", Username = "devdiary" };
        db.Channels.Add(again);
        var target = await TelegramTargetProjection.EnsureAsync(db, again);
        await db.SaveChangesAsync();

        var all = await db.PublishTargets.ToListAsync();
        Assert.Single(all);
        Assert.True(target.IsActive);
        Assert.Equal(again.Id, target.ChannelId);
        // The history survived the round trip — which is why disconnect deactivates rather than deletes.
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), target.LastPublishedAt);
    }

    [Fact]
    public async Task Two_owners_with_the_same_chat_id_get_their_own_rows()
    {
        using var db = NewDb();
        var mine = SeedChannel(db, "owner-1");
        var theirs = SeedChannel(db, "owner-2", mine.TelegramChatId);

        await TelegramTargetProjection.EnsureAsync(db, mine);
        await TelegramTargetProjection.EnsureAsync(db, theirs);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.PublishTargets.CountAsync());
    }

    [Fact]
    public async Task Backfill_covers_existing_channels_and_is_safe_to_run_again()
    {
        // A shared connection with a SECOND context for the re-run, because that is what a restart
        // is: the same database, a change tracker that knows nothing. The first version of this
        // test reused one context, where an existing row and a new one both look like "tracked",
        // and it passed against a Backfill that reported every start as if it had projected
        // everything again (found in production 01.08.2026 — the rows were right, the count lied).
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;

        using (var db = new CedarDbContext(opts, TenantProvider.Platform()))
        {
            db.Database.EnsureCreated();
            SeedChannel(db, "owner-1", -100111);
            SeedChannel(db, "owner-1", -100222, "Second");

            Assert.Equal(2, await TelegramTargetProjection.BackfillAsync(db));
            Assert.Equal(2, await db.PublishTargets.CountAsync());
        }

        using (var restarted = new CedarDbContext(opts, TenantProvider.Platform()))
        {
            Assert.Equal(0, await TelegramTargetProjection.BackfillAsync(restarted));
            Assert.Equal(2, await restarted.PublishTargets.CountAsync());
        }
    }

    [Fact]
    public async Task Backfill_on_an_account_with_no_channels_does_nothing()
    {
        using var db = NewDb();
        Assert.Equal(0, await TelegramTargetProjection.BackfillAsync(db));
        Assert.Empty(await db.PublishTargets.ToListAsync());
    }

    [Fact]
    public async Task Deactivating_a_channel_that_was_never_projected_is_a_no_op()
    {
        using var db = NewDb();
        var channel = SeedChannel(db);

        await TelegramTargetProjection.DeactivateAsync(db, channel);
        await db.SaveChangesAsync();

        Assert.Empty(await db.PublishTargets.ToListAsync());
    }
}
