using CedarClerk.Server;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Tests;

// T-359 (audit finding 2) — the comment counter must only count a real comment: a message in the
// channel's linked discussion group (a supergroup) replying to Telegram's own automatic forward.
// The exploit it closes: a stranger forwarding a connected channel's post into their private chat
// with the shared bot and replying to inflate the owner's count.
public class TelegramEngagementTests
{
    private const string Owner = "owner-a";
    private const long ChannelChatId = -100500;
    private const long DiscussionGroupId = -100999;
    private const int PostMessageId = 42;

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

    private static (Microsoft.Data.Sqlite.SqliteConnection Connection, Guid PostId) Seeded()
    {
        var connection = SharedDatabase();
        using var db = Open(connection);
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "a@x.test", Email = "a@x.test" });
        var channel = new Channel { OwnerId = Owner, Title = "Chan", TelegramChatId = ChannelChatId };
        db.Channels.Add(channel);
        var post = new ChannelPost { OwnerId = Owner, ChannelId = channel.Id, TelegramMessageId = PostMessageId };
        db.ChannelPosts.Add(post);
        db.SaveChanges();
        return (connection, post.Id);
    }

    // A message from the discussion supergroup, replying to the automatic forward of the post.
    private static Message RealComment(ChatType chatType = ChatType.Supergroup, bool repliedIsAutoForward = true) => new()
    {
        Chat = new Chat { Id = DiscussionGroupId, Type = chatType },
        Id = 5000,
        Text = "nice",
        ReplyToMessage = new Message
        {
            Chat = new Chat { Id = DiscussionGroupId, Type = ChatType.Supergroup },
            Id = 4000,
            IsAutomaticForward = repliedIsAutoForward,
            ForwardOrigin = new MessageOriginChannel
            {
                Chat = new Chat { Id = ChannelChatId, Type = ChatType.Channel },
                MessageId = PostMessageId,
                Date = DateTime.UtcNow,
            },
        },
    };

    [Fact]
    public async Task A_real_comment_in_the_discussion_group_counts()
    {
        var (connection, postId) = Seeded();
        using var db = Open(connection);

        Assert.True(await TelegramEngagement.ApplyCommentAsync(db, RealComment()));
        await db.SaveChangesAsync();

        Assert.Equal(1, db.ChannelPosts.Single(p => p.Id == postId).CommentCount);
    }

    [Fact]
    public async Task A_forward_into_a_private_chat_does_not_count()
    {
        var (connection, postId) = Seeded();
        using var db = Open(connection);

        // The exploit: same forwarded-post reply, but arriving in a private chat with the bot.
        Assert.False(await TelegramEngagement.ApplyCommentAsync(db, RealComment(chatType: ChatType.Private)));
        await db.SaveChangesAsync();

        Assert.Equal(0, db.ChannelPosts.Single(p => p.Id == postId).CommentCount);
    }

    [Fact]
    public async Task A_reply_to_a_hand_pasted_forward_does_not_count()
    {
        var (connection, postId) = Seeded();
        using var db = Open(connection);

        // In the right group, but the replied-to message is a user's own paste, not Telegram's
        // automatic forward that roots a real comment thread.
        Assert.False(await TelegramEngagement.ApplyCommentAsync(db, RealComment(repliedIsAutoForward: false)));
        await db.SaveChangesAsync();

        Assert.Equal(0, db.ChannelPosts.Single(p => p.Id == postId).CommentCount);
    }
}
