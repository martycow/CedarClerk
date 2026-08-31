using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server;

public static class BotChatAccess
{
    /// <summary>
    /// For Groups/Supergroups the Bot MUST be an Administrator. It has an ability to post by default because it's a member of the CHAT.
    /// For Channels the Bot MUST be an Administrator and has the right to post messages.
    /// </summary>
    public static bool CanPost(ChatType chatType, ChatMember member)
    {
        return chatType switch
        {
            ChatType.Group or ChatType.Supergroup => member is ChatMemberAdministrator ||
                                                     member.Status == ChatMemberStatus.Creator,

            ChatType.Channel => member is ChatMemberAdministrator { CanPostMessages: true } ||
                                member.Status == ChatMemberStatus.Creator,

            _ => false,
        };
    }

    /// <summary>
    /// T-359 — the *caller* must be an admin or creator of the chat, not merely the bot. Without
    /// this a signed-up stranger could connect any channel the shared bot was already added to
    /// (audit finding 1). "Admin or creator" — the same bar the caller needs to have added the
    /// bot in the first place, and Telegram's own membership is the authority.
    /// </summary>
    public static bool IsAdminOrCreator(ChatMember member) =>
        member is ChatMemberAdministrator || member.Status == ChatMemberStatus.Creator;
}
