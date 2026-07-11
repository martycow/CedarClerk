using CedarClerk.Server;
using CedarClerk.Server.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Tests;

public class BotChatAccessTests
{
    private static readonly User BotUser = new() { Id = 1, IsBot = true, FirstName = "Bot" };

    [Fact]
    public void Channel_admin_with_post_rights_can_post()
    {
        var member = new ChatMemberAdministrator { User = BotUser, CanPostMessages = true };
        Assert.True(BotChatAccess.CanPost(ChatType.Channel, member));
    }

    [Fact]
    public void Channel_admin_without_post_rights_cannot_post()
    {
        var member = new ChatMemberAdministrator { User = BotUser, CanPostMessages = false };
        Assert.False(BotChatAccess.CanPost(ChatType.Channel, member));
    }

    [Fact]
    public void Channel_creator_can_post_even_without_explicit_flag()
    {
        var member = new ChatMemberOwner { User = BotUser };
        Assert.True(BotChatAccess.CanPost(ChatType.Channel, member));
    }

    [Fact]
    public void Channel_plain_member_cannot_post()
    {
        var member = new ChatMemberMember { User = BotUser };
        Assert.False(BotChatAccess.CanPost(ChatType.Channel, member));
    }

    [Theory]
    [InlineData(ChatType.Group)]
    [InlineData(ChatType.Supergroup)]
    public void Group_any_admin_role_can_post_regardless_of_post_messages_flag(ChatType chatType)
    {
        var member = new ChatMemberAdministrator { User = BotUser, CanPostMessages = false };
        Assert.True(BotChatAccess.CanPost(chatType, member));
    }

    [Theory]
    [InlineData(ChatType.Group)]
    [InlineData(ChatType.Supergroup)]
    public void Group_plain_member_cannot_post(ChatType chatType)
    {
        var member = new ChatMemberMember { User = BotUser };
        Assert.False(BotChatAccess.CanPost(chatType, member));
    }

    [Fact]
    public void Private_chat_is_never_postable()
    {
        var member = new ChatMemberOwner { User = BotUser };
        Assert.False(BotChatAccess.CanPost(ChatType.Private, member));
    }
}
