using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server;

public static class ChannelEndpoints
{
    public record ConnectChannelRequest(string ChatId);
    public record KnownChatDto(long TelegramChatId, string Title, string? Username, string Type);

    public static void MapChannelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/channels").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await db.Channels.Where(c => c.OwnerId == uid)
                .Select(c => new { c.Id, c.Title, c.TelegramChatId, c.Username })
                .ToListAsync();
        });

        group.MapPost("/", async (ConnectChannelRequest req, ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot, ILogger<Channel> logger) =>
        {
            if (!bot.IsRunning)
                return Results.Json(new { error = "Telegram bot is not running (no token configured)" }, statusCode: StatusCodes.Status503ServiceUnavailable);

            ChatFullInfo chat;
            try
            {
                chat = await bot.Client.GetChat(new ChatId(req.ChatId));
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "No TG-channel was found or no access to that channel" });
            }

            var member = await bot.Client.GetChatMember(chat.Id, bot.Me.Id);

            if (chat.Type is not (ChatType.Channel or ChatType.Group or ChatType.Supergroup))
                return Results.BadRequest(new { error = "Unsupported chat type" });

            if (!BotChatAccess.CanPost(chat.Type, member))
                return Results.BadRequest(new { error = chat.Type == ChatType.Channel
                    ? "Bot must have an Admin with the right to send messages OR Creator."
                    : "Bot must be an Admin or Creator of the Group/Supergroup." });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var tier = await db.Users.Where(u => u.Id == uid).Select(u => u.PlanTier).FirstAsync();
            var channelCount = await db.Channels.CountAsync(c => c.OwnerId == uid);
            if (!PlanQuotas.CanConnectAnotherChannel(tier, channelCount))
                return Results.Json(new { error = $"Free plan allows only {PlanQuotas.FreeMaxChannels} connected channel. Upgrade to Pro for more." }, statusCode: StatusCodes.Status403Forbidden);

            var channel = new Channel
            {
                Title = chat.Title ?? chat.Username ?? req.ChatId,
                TelegramChatId = chat.Id,
                Username = chat.Username,
                OwnerId = uid,
            };
            db.Channels.Add(channel);

            // Take the first snapshot right away so the stats UI isn't empty until the next 4 AM job run.
            try
            {
                var count = await bot.Client.GetChatMemberCount(new ChatId(channel.TelegramChatId));
                db.ChannelStatSnapshots.Add(new ChannelStatSnapshot { ChannelId = channel.Id, MemberCount = count });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to take initial member-count snapshot for channel {ChannelId}", channel.Id);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { channel.Id, channel.Title, channel.TelegramChatId, channel.Username });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.Channels.Where(c => c.Id == id && c.OwnerId == uid).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/{id:guid}/stats", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Channels.AnyAsync(c => c.Id == id && c.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var snapshots = await db.ChannelStatSnapshots
                .Where(s => s.ChannelId == id)
                .OrderByDescending(s => s.TakenAt)
                .Take(30)
                .OrderBy(s => s.TakenAt)
                .Select(s => new { s.TakenAt, s.MemberCount })
                .ToListAsync();

            var current = snapshots.Count > 0 ? snapshots[^1].MemberCount : (int?)null;
            var points = snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.MemberCount)).ToList();
            var deltaWeek = ChannelStatsCalculator.DeltaOverDays(points, 7, DateTime.UtcNow);

            return Results.Ok(new { current, deltaWeek, snapshots });
        });

        // Chats the bot is known to be in (tracked live from Telegram's my_chat_member updates —
        // see TelegramBotService) that aren't already connected by anyone. Scoped to chats where
        // the REQUESTING user's linked Telegram identity is actually an admin (via the
        // BotKnownChatAdmin cache) — the bot is shared across every Cedar Clerk account, so without
        // this filter everyone would see every chat the bot is in, including other users' channels.
        // Users who haven't linked a Telegram account (Settings > Account) get an empty list, since
        // there's no identity to scope against — they can still connect by typing @username/id.
        group.MapGet("/known", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var telegramUserId = await db.Users.Where(u => u.Id == uid).Select(u => u.TelegramUserId).FirstAsync();
            if (telegramUserId is null) return new List<KnownChatDto>();

            var connectedIds = db.Channels.Select(c => c.TelegramChatId);
            return await db.BotKnownChats
                .Where(k => k.BotCanPost && !connectedIds.Contains(k.TelegramChatId)
                    && db.BotKnownChatAdmins.Any(a => a.BotKnownChatId == k.Id && a.TelegramUserId == telegramUserId))
                .OrderByDescending(k => k.LastSeenAt)
                .Select(k => new KnownChatDto(k.TelegramChatId, k.Title, k.Username, k.Type))
                .ToListAsync();
        });

        // Re-checks the bot's current status and admin list in every known chat (title/username/
        // posting rights/admins may have changed since we last saw an update for it). Does NOT
        // discover chats the bot was already in before this feature started tracking
        // my_chat_member updates — for those, connecting still works the old way (type
        // @username/chat id manually).
        group.MapPost("/refresh-known-chats", async (CedarDbContext db, TelegramBotService bot, ILogger<Channel> logger) =>
        {
            if (!bot.IsRunning)
                return Results.Json(new { error = "Telegram bot is not running (no token configured)" }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var known = await db.BotKnownChats.ToListAsync();
            foreach (var chat in known)
            {
                try
                {
                    var fullChat = await bot.Client.GetChat(new ChatId(chat.TelegramChatId));
                    var member = await bot.Client.GetChatMember(chat.TelegramChatId, bot.Me.Id);

                    chat.Title = fullChat.Title ?? fullChat.Username ?? chat.Title;
                    chat.Username = fullChat.Username;
                    chat.Type = fullChat.Type.ToString();
                    chat.BotCanPost = BotChatAccess.CanPost(fullChat.Type, member);
                    chat.LastSeenAt = DateTime.UtcNow;

                    if (chat.BotCanPost)
                        await BotKnownChatSync.SyncAdminsAsync(db, bot.Client, chat);
                }
                catch (Exception ex)
                {
                    // Bot was removed from the chat, chat was deleted, etc. — leave the stale row
                    // as not-postable rather than deleting the discovery history.
                    chat.BotCanPost = false;
                    logger.LogWarning(ex, "Failed to refresh known chat {ChatId}", chat.TelegramChatId);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { refreshed = known.Count });
        });
    }
}
