using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

public class AccountDeletionTests
{
    private const string Keep = "keeper";
    private const string Drop = "doomed";

    private static Microsoft.Data.Sqlite.SqliteConnection Seeded()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();

        db.Users.Add(new ApplicationUser { Id = Keep, UserName = "k@x.test", Email = "k@x.test" });
        db.Users.Add(new ApplicationUser { Id = Drop, UserName = "d@x.test", Email = "d@x.test" });
        db.SaveChanges();

        foreach (var owner in new[] { Keep, Drop })
        {
            var draft = new Draft { OwnerId = owner, Title = owner, CedarJson = "{}" };
            var channel = new Channel { OwnerId = owner, Title = owner, TelegramChatId = owner.Length };
            db.Drafts.Add(draft);
            db.Channels.Add(channel);
            db.SaveChanges();

            db.DraftTranslations.Add(new DraftTranslation { DraftId = draft.Id, OwnerId = owner, Language = "en", Title = owner });
            db.DraftRevisions.Add(new DraftRevision { DraftId = draft.Id, OwnerId = owner, Title = owner, CedarJson = "{}" });
            db.Comments.Add(new Comment { DraftId = draft.Id, OwnerId = owner, Text = "c" });
            db.Reactions.Add(new Reaction { DraftId = draft.Id, OwnerId = owner, Kind = "like" });
            db.PollVotes.Add(new PollVote { DraftId = draft.Id, OwnerId = owner, PollId = "p", Option = "a" });
            db.PostInvites.Add(new PostInvite { DraftId = draft.Id, OwnerId = owner, Email = "r@x.test", Token = owner });
            db.PostRegistrations.Add(new PostRegistration { DraftId = draft.Id, OwnerId = owner, Email = "r@x.test" });
            db.DraftStatSnapshots.Add(new DraftStatSnapshot { DraftId = draft.Id, OwnerId = owner });
            db.DraftTargetTexts.Add(new DraftTargetText { DraftId = draft.Id, OwnerId = owner, Network = "x", Language = "en", Text = "t" });
            db.ChannelPosts.Add(new ChannelPost { ChannelId = channel.Id, OwnerId = owner, DraftId = draft.Id, TelegramMessageId = 1 });
            db.ChannelStatSnapshots.Add(new ChannelStatSnapshot { ChannelId = channel.Id, OwnerId = owner });
            db.Assets.Add(new Asset { OwnerId = owner, FileName = owner + ".png", LocalPath = owner + ".png" });
            db.Folders.Add(new Folder { OwnerId = owner, Name = owner });
            db.Series.Add(new Series { OwnerId = owner, Name = owner, Slug = owner });
            db.Payments.Add(new Payment { OwnerId = owner, Provider = "stripe", Plan = "pro", ExternalId = owner, Amount = 1, Currency = "usd" });
            db.SaveChanges();
        }

        return connection;
    }

    private static CedarDbContext Platform(Microsoft.Data.Sqlite.SqliteConnection c) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(c).Options, TenantProvider.Platform());

    [Fact]
    public async Task Deleting_an_account_removes_every_row_it_owned()
    {
        using var connection = Seeded();

        using (var db = Platform(connection))
            await AccountDeletion.DeleteAsync(db, Drop, mediaDir: null);

        using var check = Platform(connection);
        Assert.Empty(check.Drafts.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Channels.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Assets.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Folders.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Series.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.DraftTranslations.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.DraftRevisions.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Comments.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Reactions.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.PollVotes.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.PostInvites.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.PostRegistrations.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.DraftStatSnapshots.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.DraftTargetTexts.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.ChannelPosts.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.ChannelStatSnapshots.Where(x => x.OwnerId == Drop));
        Assert.Empty(check.Payments.Where(x => x.OwnerId == Drop));
        Assert.Null(check.Users.FirstOrDefault(u => u.Id == Drop));
    }

    // The whole point of doing this with the filters off: a platform context sees everybody, so a
    // delete written against the wrong column takes the wrong person's work with it.
    [Fact]
    public async Task Another_account_is_left_completely_untouched()
    {
        using var connection = Seeded();

        using (var db = Platform(connection))
            await AccountDeletion.DeleteAsync(db, Drop, mediaDir: null);

        using var check = Platform(connection);
        Assert.Single(check.Drafts.Where(x => x.OwnerId == Keep));
        Assert.Single(check.Channels.Where(x => x.OwnerId == Keep));
        Assert.Single(check.Assets.Where(x => x.OwnerId == Keep));
        Assert.Single(check.DraftTranslations.Where(x => x.OwnerId == Keep));
        Assert.Single(check.DraftRevisions.Where(x => x.OwnerId == Keep));
        Assert.Single(check.Comments.Where(x => x.OwnerId == Keep));
        Assert.Single(check.Reactions.Where(x => x.OwnerId == Keep));
        Assert.Single(check.ChannelPosts.Where(x => x.OwnerId == Keep));
        Assert.Single(check.Payments.Where(x => x.OwnerId == Keep));
        Assert.NotNull(check.Users.FirstOrDefault(u => u.Id == Keep));
    }

    // The audit log is denormalized on purpose so it still reads once the rows it points at are
    // gone. Deleting an account is exactly the moment that matters.
    [Fact]
    public async Task The_audit_log_survives_the_account_it_describes()
    {
        using var connection = Seeded();
        using (var db = Platform(connection))
        {
            db.AdminAuditEntries.Add(new AdminAuditEntry
            {
                ActorId = Keep, ActorEmail = "k@x.test", Action = "plan",
                TargetUserId = Drop, TargetEmail = "d@x.test",
            });
            db.SaveChanges();
            await AccountDeletion.DeleteAsync(db, Drop, mediaDir: null);
        }

        using var check = Platform(connection);
        var entry = Assert.Single(check.AdminAuditEntries.Where(e => e.TargetUserId == Drop));
        Assert.Equal("d@x.test", entry.TargetEmail);
    }

    [Fact]
    public async Task Deleting_an_account_that_does_not_exist_is_not_an_error()
    {
        using var connection = Seeded();
        using var db = Platform(connection);

        Assert.False(await AccountDeletion.DeleteAsync(db, "nobody", mediaDir: null));
        Assert.Equal(2, db.Users.Count());
    }

    [Fact]
    public async Task An_accounts_media_files_go_with_it()
    {
        using var connection = Seeded();
        var mediaDir = Path.Combine(Path.GetTempPath(), "cedar-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mediaDir);
        File.WriteAllText(Path.Combine(mediaDir, Drop + ".png"), "x");
        File.WriteAllText(Path.Combine(mediaDir, Keep + ".png"), "x");

        try
        {
            using var db = Platform(connection);
            await AccountDeletion.DeleteAsync(db, Drop, mediaDir);

            Assert.False(File.Exists(Path.Combine(mediaDir, Drop + ".png")));
            Assert.True(File.Exists(Path.Combine(mediaDir, Keep + ".png")));
        }
        finally
        {
            Directory.Delete(mediaDir, recursive: true);
        }
    }

    // A path from the database must never reach outside the media directory, however it got there.
    [Fact]
    public async Task A_traversal_path_in_an_asset_row_cannot_delete_outside_the_media_directory()
    {
        using var connection = Seeded();
        var root = Path.Combine(Path.GetTempPath(), "cedar-del-" + Guid.NewGuid().ToString("N"));
        var mediaDir = Path.Combine(root, "media");
        Directory.CreateDirectory(mediaDir);
        var outsider = Path.Combine(root, "important.txt");
        File.WriteAllText(outsider, "keep me");

        try
        {
            using (var db = Platform(connection))
            {
                db.Assets.Add(new Asset { OwnerId = Drop, FileName = "evil", LocalPath = "../important.txt" });
                db.SaveChanges();
                await AccountDeletion.DeleteAsync(db, Drop, mediaDir);
            }

            Assert.True(File.Exists(outsider));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
