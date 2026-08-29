using CedarClerk.Server;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Tests;

// The chat_member ingestion, driven with fabricated ChatMemberUpdated objects and no bot client —
// the telegram-bot.md testing shape. What must hold: joins through a named link land on that
// link's daily row, everything else lands on the organic (null-link) row, and only aggregates are
// ever written.
public class ChannelMemberIngestTests
{
    private const string Owner = "owner-a";
    private const long ChatId = -100420;
    private const string LinkUrl = "https://t.me/+abc123";

    private static readonly DateTime Day = new(2026, 8, 29, 15, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ChatMemberStatus.Left, ChatMemberStatus.Member, ChannelMemberIngest.MemberChange.Join)]
    [InlineData(ChatMemberStatus.Kicked, ChatMemberStatus.Member, ChannelMemberIngest.MemberChange.Join)]
    [InlineData(ChatMemberStatus.Left, ChatMemberStatus.Administrator, ChannelMemberIngest.MemberChange.Join)]
    [InlineData(ChatMemberStatus.Member, ChatMemberStatus.Left, ChannelMemberIngest.MemberChange.Leave)]
    [InlineData(ChatMemberStatus.Administrator, ChatMemberStatus.Kicked, ChannelMemberIngest.MemberChange.Leave)]
    [InlineData(ChatMemberStatus.Member, ChatMemberStatus.Administrator, ChannelMemberIngest.MemberChange.None)]
    [InlineData(ChatMemberStatus.Left, ChatMemberStatus.Kicked, ChannelMemberIngest.MemberChange.None)]
    public void Classify_only_counts_crossings_of_the_membership_line(
        ChatMemberStatus oldStatus, ChatMemberStatus newStatus, ChannelMemberIngest.MemberChange expected)
    {
        Assert.Equal(expected, ChannelMemberIngest.Classify(Of(oldStatus), Of(newStatus)));
    }

    // Restricted is not a side of the membership line by itself — ChatMemberRestricted carries its
    // own IsMember flag. A mute (member → restricted-but-in) and its lifting are not churn; a
    // restriction that also removes the person from the chat is.
    [Fact]
    public void A_mute_and_an_unmute_are_not_churn()
    {
        Assert.Equal(ChannelMemberIngest.MemberChange.None,
            ChannelMemberIngest.Classify(Member(), Restricted(isMember: true)));
        Assert.Equal(ChannelMemberIngest.MemberChange.None,
            ChannelMemberIngest.Classify(Restricted(isMember: true), Member()));
    }

    [Fact]
    public void A_restriction_that_removes_from_the_chat_still_counts()
    {
        Assert.Equal(ChannelMemberIngest.MemberChange.Leave,
            ChannelMemberIngest.Classify(Member(), Restricted(isMember: false)));
        Assert.Equal(ChannelMemberIngest.MemberChange.Leave,
            ChannelMemberIngest.Classify(Restricted(isMember: true), Left()));
        Assert.Equal(ChannelMemberIngest.MemberChange.Join,
            ChannelMemberIngest.Classify(Restricted(isMember: false), Member()));
    }

    private static ChatMember Of(ChatMemberStatus status) => status switch
    {
        ChatMemberStatus.Left => new ChatMemberLeft { User = SomeUser() },
        ChatMemberStatus.Kicked => new ChatMemberBanned { User = SomeUser() },
        ChatMemberStatus.Member => new ChatMemberMember { User = SomeUser() },
        ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = SomeUser() },
        ChatMemberStatus.Creator => new ChatMemberOwner { User = SomeUser() },
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static ChatMember Restricted(bool isMember) =>
        new ChatMemberRestricted { User = SomeUser(), IsMember = isMember };

    private static User SomeUser() => new() { Id = 999, FirstName = "Someone" };

    private static Microsoft.Data.Sqlite.SqliteConnection SharedDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var seed = Open(connection);
        seed.Database.EnsureCreated();
        return connection;
    }

    private static CedarDbContext Open(Microsoft.Data.Sqlite.SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());

    private static (Microsoft.Data.Sqlite.SqliteConnection Connection, Guid ChannelId, Guid LinkId) Seeded()
    {
        var connection = SharedDatabase();
        using var db = Open(connection);
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "a@x.test", Email = "a@x.test" });
        var channel = new Channel { OwnerId = Owner, Title = "Chan", TelegramChatId = ChatId };
        db.Channels.Add(channel);
        var link = new ChannelInviteLink { OwnerId = Owner, ChannelId = channel.Id, Name = "launch", InviteLink = LinkUrl };
        db.ChannelInviteLinks.Add(link);
        db.SaveChanges();
        return (connection, channel.Id, link.Id);
    }

    private static ChatMemberUpdated Update(ChatMember oldMember, ChatMember newMember, string? inviteUrl = null, long chatId = ChatId) => new()
    {
        Chat = new Chat { Id = chatId, Type = ChatType.Channel },
        From = new User { Id = 999, FirstName = "Someone" },
        Date = Day,
        OldChatMember = oldMember,
        NewChatMember = newMember,
        InviteLink = inviteUrl is null ? null : new ChatInviteLink { InviteLink = inviteUrl, Creator = new User { Id = 1, FirstName = "Bot" } },
    };

    private static ChatMember Left() => new ChatMemberLeft { User = new User { Id = 999, FirstName = "Someone" } };
    private static ChatMember Member() => new ChatMemberMember { User = new User { Id = 999, FirstName = "Someone" } };

    [Fact]
    public async Task Join_via_a_named_link_lands_on_that_links_day_row()
    {
        var (connection, channelId, linkId) = Seeded();
        using var db = Open(connection);

        Assert.True(await ChannelMemberIngest.ApplyAsync(db, Update(Left(), Member(), LinkUrl)));
        await db.SaveChangesAsync();

        var row = Assert.Single(db.ChannelMemberDailies.ToList());
        Assert.Equal(channelId, row.ChannelId);
        Assert.Equal(linkId, row.InviteLinkId);
        Assert.Equal(Day.Date, row.Day);
        Assert.Equal(1, row.Joins);
        Assert.Equal(0, row.Leaves);
    }

    [Fact]
    public async Task Second_join_the_same_day_increments_the_same_row()
    {
        var (connection, _, _) = Seeded();
        using var db = Open(connection);

        await ChannelMemberIngest.ApplyAsync(db, Update(Left(), Member(), LinkUrl));
        await db.SaveChangesAsync();
        await ChannelMemberIngest.ApplyAsync(db, Update(Left(), Member(), LinkUrl));
        await db.SaveChangesAsync();

        var row = Assert.Single(db.ChannelMemberDailies.ToList());
        Assert.Equal(2, row.Joins);
    }

    [Fact]
    public async Task Unknown_link_and_every_leave_land_on_the_organic_row()
    {
        var (connection, _, _) = Seeded();
        using var db = Open(connection);

        await ChannelMemberIngest.ApplyAsync(db, Update(Left(), Member(), "https://t.me/+not-ours"));
        await db.SaveChangesAsync();
        await ChannelMemberIngest.ApplyAsync(db, Update(Member(), Left()));
        await db.SaveChangesAsync();

        var row = Assert.Single(db.ChannelMemberDailies.ToList());
        Assert.Null(row.InviteLinkId);
        Assert.Equal(1, row.Joins);
        Assert.Equal(1, row.Leaves);
    }

    [Fact]
    public async Task A_promotion_or_an_unknown_chat_changes_nothing()
    {
        var (connection, _, _) = Seeded();
        using var db = Open(connection);

        Assert.False(await ChannelMemberIngest.ApplyAsync(db,
            Update(new ChatMemberMember { User = new User { Id = 999, FirstName = "S" } },
                new ChatMemberAdministrator { User = new User { Id = 999, FirstName = "S" } })));
        Assert.False(await ChannelMemberIngest.ApplyAsync(db, Update(Member(), Restricted(isMember: true))));
        Assert.False(await ChannelMemberIngest.ApplyAsync(db, Update(Restricted(isMember: true), Member())));
        Assert.False(await ChannelMemberIngest.ApplyAsync(db, Update(Left(), Member(), chatId: -1)));
        Assert.Empty(db.ChannelMemberDailies.ToList());
    }

    [Fact]
    public async Task A_shared_channel_tallies_for_every_owner_that_connected_it()
    {
        var (connection, _, _) = Seeded();
        using (var seed = Open(connection))
        {
            seed.Users.Add(new ApplicationUser { Id = "owner-b", UserName = "b@x.test", Email = "b@x.test" });
            seed.Channels.Add(new Channel { OwnerId = "owner-b", Title = "Same chan", TelegramChatId = ChatId });
            seed.SaveChanges();
        }

        using var db = Open(connection);
        await ChannelMemberIngest.ApplyAsync(db, Update(Left(), Member()));
        await db.SaveChangesAsync();

        var rows = db.ChannelMemberDailies.ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Joins));
        Assert.Equal(2, rows.Select(r => r.OwnerId).Distinct().Count());
    }
}
