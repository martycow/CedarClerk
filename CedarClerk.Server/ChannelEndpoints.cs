using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server;

public static class ChannelEndpoints
{
    public record ConnectChannelRequest(string ChatId);

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

            // Rights checking
            switch (chat.Type)
            {
                case ChatType.Channel:
                {
                    var canPost = member is ChatMemberAdministrator { CanPostMessages: true } || 
                                  member.Status == ChatMemberStatus.Creator;
                    
                    if (!canPost)
                        return Results.BadRequest(new { error = "Bot must have an Admin with the right to send messages OR Creator." });
                    break;
                }
                case ChatType.Group:
                case ChatType.Supergroup:
                {
                    var canPost = member is ChatMemberAdministrator || member.Status == ChatMemberStatus.Creator;
                    if (!canPost)
                        return Results.BadRequest(new { error = "Bot must be an Admin or Creator of the Group/Supergroup." });
                    break;
                }
                case ChatType.Private:
                    return Results.BadRequest(new { error = "Private chats are not supported" });
                case ChatType.Sender:
                    return Results.BadRequest(new { error = "Sender chats are not supported" });
                default:
                    return Results.BadRequest(new { error = "Unsupported chat type" });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
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
    }
}
