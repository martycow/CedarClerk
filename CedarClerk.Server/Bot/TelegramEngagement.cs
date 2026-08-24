using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;

namespace CedarClerk.Server.Bot;

// ADR-205 — what the bot has seen happen to a post in Telegram. Two updates, one rule: find the
// ChannelPost the update is about and write the number onto it. Neither number can be fetched, so
// this is the only place either one ever comes from.
public static class TelegramEngagement
{
    /// <summary>
    /// `message_reaction_count` — the anonymous aggregate for a channel post. The update carries the
    /// full current tally, so this SETS rather than increments: a reader taking a reaction back
    /// arrives as a smaller total, and a missed update is corrected by the next one.
    /// </summary>
    public static async Task<bool> ApplyReactionsAsync(CedarDbContext db, MessageReactionCountUpdated update)
    {
        var post = await FindPostAsync(db, update.Chat.Id, update.MessageId);
        if (post is null) return false;

        var total = update.Reactions?.Sum(r => r.TotalCount) ?? 0;
        if (post.ReactionCount == total) return false;

        post.ReactionCount = total;
        post.StatsSeenAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// A comment is a message in the channel's linked discussion group, replying to the automatic
    /// forward of the post. Unlike reactions this one only ever counts up: Telegram sends no update
    /// when a comment is deleted, so the number is a floor and the ADR says so.
    /// </summary>
    public static async Task<bool> ApplyCommentAsync(CedarDbContext db, Message message)
    {
        // The bot's own replies are not comments, and neither is the automatic forward itself.
        if (message.ReplyToMessage is not { } repliedTo || message.IsAutomaticForward) return false;

        // Bot API 7 replaced forward_from_message_id with forward_origin; a channel post forwarded
        // into the discussion group is the one origin kind that names a message id.
        if (repliedTo.ForwardOrigin is not MessageOriginChannel origin) return false;

        var post = await FindPostAsync(db, origin.Chat.Id, origin.MessageId);
        if (post is null) return false;

        post.CommentCount++;
        post.StatsSeenAt = DateTime.UtcNow;
        return true;
    }

    private static async Task<ChannelPost?> FindPostAsync(CedarDbContext db, long telegramChatId, int messageId)
    {
        var channelIds = await db.Channels
            .Where(c => c.TelegramChatId == telegramChatId)
            .Select(c => c.Id)
            .ToListAsync();

        if (channelIds.Count == 0) return null;

        return await db.ChannelPosts
            .FirstOrDefaultAsync(p => channelIds.Contains(p.ChannelId) && p.TelegramMessageId == messageId);
    }
}
