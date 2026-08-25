using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Deleting a draft used to clear five of its thirteen child tables, which is how production ended
// up with 16 rows whose parent is gone. These cover both halves: the cascade that stops new ones,
// and the sweep that clears the ones already there.
public class OrphanCleanupTests
{
    private const string Owner = "owner-1";

    private static CedarDbContext Database()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "o@x.test", Email = "o@x.test" });
        db.SaveChanges();
        return db;
    }

    private static (Guid DraftId, Guid ChannelId) SeedWithChildren(CedarDbContext db)
    {
        var draft = new Draft { OwnerId = Owner, Title = "T", CedarJson = "{}" };
        var channel = new Channel { OwnerId = Owner, Title = "C", TelegramChatId = 42 };
        db.Drafts.Add(draft);
        db.Channels.Add(channel);
        db.SaveChanges();

        db.Reactions.Add(new Reaction { DraftId = draft.Id, OwnerId = Owner, Kind = "like" });
        db.Comments.Add(new Comment { DraftId = draft.Id, OwnerId = Owner, Text = "c" });
        db.PollVotes.Add(new PollVote { DraftId = draft.Id, OwnerId = Owner, PollId = "p", Option = "a" });
        db.PostInvites.Add(new PostInvite { DraftId = draft.Id, OwnerId = Owner, Email = "r@x.test", Token = "t" });
        db.PostRegistrations.Add(new PostRegistration { DraftId = draft.Id, OwnerId = Owner, Email = "r@x.test" });
        db.DraftStatSnapshots.Add(new DraftStatSnapshot { DraftId = draft.Id, OwnerId = Owner });
        db.DraftTargetTexts.Add(new DraftTargetText { DraftId = draft.Id, OwnerId = Owner, Network = "x", Language = "en", Text = "t" });
        db.DraftRevisions.Add(new DraftRevision { DraftId = draft.Id, OwnerId = Owner, Title = "T", CedarJson = "{}" });
        db.ChannelPosts.Add(new ChannelPost { ChannelId = channel.Id, OwnerId = Owner, DraftId = draft.Id, TelegramMessageId = 1 });
        db.ChannelStatSnapshots.Add(new ChannelStatSnapshot { ChannelId = channel.Id, OwnerId = Owner });
        db.SaveChanges();

        return (draft.Id, channel.Id);
    }

    [Fact]
    public async Task Deleting_a_draft_takes_all_of_its_children_with_it()
    {
        using var db = Database();
        var (draftId, _) = SeedWithChildren(db);

        await DraftDeletion.CascadeAsync(db, Owner, draftId);
        await db.Drafts.Where(d => d.Id == draftId).ExecuteDeleteAsync();

        Assert.Empty(db.Reactions.Where(x => x.DraftId == draftId));
        Assert.Empty(db.Comments.Where(x => x.DraftId == draftId));
        Assert.Empty(db.PollVotes.Where(x => x.DraftId == draftId));
        Assert.Empty(db.PostInvites.Where(x => x.DraftId == draftId));
        Assert.Empty(db.PostRegistrations.Where(x => x.DraftId == draftId));
        Assert.Empty(db.DraftStatSnapshots.Where(x => x.DraftId == draftId));
        Assert.Empty(db.DraftTargetTexts.Where(x => x.DraftId == draftId));
        Assert.Empty(db.DraftRevisions.Where(x => x.DraftId == draftId));
        Assert.Empty(db.ChannelPosts.Where(x => x.DraftId == draftId));
    }

    [Fact]
    public async Task Deleting_a_channel_takes_its_posts_and_snapshots()
    {
        using var db = Database();
        var (_, channelId) = SeedWithChildren(db);

        await ChannelDeletion.CascadeAsync(db, Owner, channelId);

        Assert.Empty(db.ChannelPosts.Where(x => x.ChannelId == channelId));
        Assert.Empty(db.ChannelStatSnapshots.Where(x => x.ChannelId == channelId));
    }

    [Fact]
    public async Task The_sweep_counts_and_removes_rows_whose_parent_is_gone()
    {
        using var db = Database();
        var (draftId, channelId) = SeedWithChildren(db);

        // Straight to the parent tables, reproducing exactly what the old delete path left behind.
        await db.Drafts.Where(d => d.Id == draftId).ExecuteDeleteAsync();
        await db.Channels.Where(c => c.Id == channelId).ExecuteDeleteAsync();

        var found = await OrphanSweep.CountAsync(db);
        Assert.Equal(9, found.Values.Sum());

        var removed = await OrphanSweep.RemoveAsync(db);
        Assert.Equal(9, removed.Values.Sum());
        Assert.Equal(0, (await OrphanSweep.CountAsync(db)).Values.Sum());
    }

    [Fact]
    public async Task The_sweep_leaves_rows_whose_parent_is_alive()
    {
        using var db = Database();
        SeedWithChildren(db);

        Assert.Equal(0, (await OrphanSweep.CountAsync(db)).Values.Sum());
        Assert.Equal(0, (await OrphanSweep.RemoveAsync(db)).Values.Sum());
        Assert.Single(db.Reactions);
        Assert.Single(db.ChannelPosts);
    }
}
