using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server.Bot;

/// <summary>
/// Wave 2 item 15 — turns a chat_member update into a bump on the right ChannelMemberDaily row.
/// Static and database-only on purpose (the telegram-bot.md testing shape): everything here runs
/// against fabricated <see cref="ChatMemberUpdated"/> objects with no bot client anywhere near.
/// Only the daily tally is ever written — no per-user identity from the update is persisted.
/// </summary>
public static class ChannelMemberIngest
{
    public enum MemberChange { None, Join, Leave }

    /// <summary>
    /// A join is an outsider becoming a member (or arriving straight as an admin); a leave is the
    /// reverse. Everything else — restriction changes, admin promotions of existing members —
    /// moves nobody across the membership line and is ignored. Restricted is not a side of the
    /// line by itself: a muted member is still in the chat, so the status defers to the object's
    /// own IsMember flag rather than counting every mute as churn.
    /// </summary>
    public static MemberChange Classify(ChatMember oldMember, ChatMember newMember)
    {
        var wasIn = IsMember(oldMember);
        var isIn = IsMember(newMember);
        return (wasIn, isIn) switch
        {
            (false, true) => MemberChange.Join,
            (true, false) => MemberChange.Leave,
            _ => MemberChange.None,
        };
    }

    private static bool IsMember(ChatMember member) => member switch
    {
        ChatMemberRestricted restricted => restricted.IsMember,
        _ => member.Status is ChatMemberStatus.Member or ChatMemberStatus.Administrator or ChatMemberStatus.Creator,
    };

    /// <summary>
    /// Applies one update against a platform-scoped context. Every Channel row with this chat id
    /// gets its own tally — the bot is shared, so two accounts may both have connected the same
    /// channel and each owner's analytics are their own. A join arriving through one of that
    /// owner's named invite links lands on the link's row; everything else (including every leave,
    /// which Telegram never attributes to a link) lands on the null-link organic row.
    /// Returns true when anything changed; the caller saves.
    /// </summary>
    public static async Task<bool> ApplyAsync(CedarDbContext db, ChatMemberUpdated cm)
    {
        var change = Classify(cm.OldChatMember, cm.NewChatMember);
        if (change == MemberChange.None) return false;

        var channels = await db.Channels.Where(c => c.TelegramChatId == cm.Chat.Id).ToListAsync();
        if (channels.Count == 0) return false;

        var day = cm.Date == default ? DateTime.UtcNow.Date : cm.Date.Date;
        var inviteUrl = change == MemberChange.Join ? cm.InviteLink?.InviteLink : null;

        foreach (var channel in channels)
        {
            Guid? linkId = null;
            if (inviteUrl is not null)
                linkId = await db.ChannelInviteLinks
                    .Where(l => l.OwnerId == channel.OwnerId && l.ChannelId == channel.Id && l.InviteLink == inviteUrl)
                    .Select(l => (Guid?)l.Id)
                    .FirstOrDefaultAsync();

            var row = await db.ChannelMemberDailies.FirstOrDefaultAsync(d =>
                d.OwnerId == channel.OwnerId && d.ChannelId == channel.Id && d.Day == day && d.InviteLinkId == linkId);
            if (row is null)
            {
                row = new ChannelMemberDaily
                {
                    OwnerId = channel.OwnerId,
                    ChannelId = channel.Id,
                    Day = day,
                    InviteLinkId = linkId,
                };
                db.ChannelMemberDailies.Add(row);
            }

            if (change == MemberChange.Join) row.Joins++;
            else row.Leaves++;
        }

        return true;
    }
}
