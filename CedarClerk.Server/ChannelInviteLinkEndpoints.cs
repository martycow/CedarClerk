using System.Security.Claims;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace CedarClerk.Server;

/// <summary>
/// Wave 2 item 15 — named invite links on a connected Telegram channel, and the daily member-flow
/// aggregates the chat_member ingestion writes against them. Creating and revoking are send-type
/// bot calls (no 409 concern); a bot that is not running answers 503 with a clear message, the
/// PostEndpoints pattern. Deliberately its own file: ChannelEndpoints.cs belongs to the stats lane
/// this wave.
/// </summary>
public static class ChannelInviteLinkEndpoints
{
    public record CreateInviteLinkRequest(string Name);

    private const int NameMaxLength = 32; // Telegram's own cap on invite-link names

    public static void MapChannelInviteLinkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/channels/{id:guid}").RequireAuthorization();

        group.MapPost("/invite-links", async (Guid id, CreateInviteLinkRequest req, ClaimsPrincipal user,
            CedarDbContext db, TelegramBotService bot) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == uid);
            if (channel is null) return Results.NotFound();

            if (!bot.IsRunning)
                return Results.Json(new { error = ErrorMessages.BotNotRunningNoToken }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var name = req.Name.Trim();
            if (name.Length is 0 or > NameMaxLength)
                return Results.BadRequest(new { error = ErrorMessages.InviteLinkNameLength(NameMaxLength) });

            Telegram.Bot.Types.ChatInviteLink created;
            try
            {
                created = await bot.Client.CreateChatInviteLink(channel.TelegramChatId, name: name);
            }
            catch (Exception ex)
            {
                // Missing can_invite_users, or the bot lost its admin seat — Telegram's wording is
                // the most specific answer available.
                return Results.BadRequest(new { error = ex.Message });
            }

            var link = new ChannelInviteLink
            {
                OwnerId = uid,
                ChannelId = channel.Id,
                Name = name,
                InviteLink = created.InviteLink,
            };
            db.ChannelInviteLinks.Add(link);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = link.Id, name = link.Name, inviteLink = link.InviteLink });
        });

        group.MapGet("/invite-links", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Channels.AnyAsync(c => c.Id == id && c.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var links = await db.ChannelInviteLinks
                .Where(l => l.OwnerId == uid && l.ChannelId == id)
                .OrderBy(l => l.CreatedAt)
                .ToListAsync();

            var totals = await db.ChannelMemberDailies
                .Where(d => d.OwnerId == uid && d.ChannelId == id)
                .GroupBy(d => d.InviteLinkId)
                .Select(g => new { InviteLinkId = g.Key, Joins = g.Sum(d => d.Joins), Leaves = g.Sum(d => d.Leaves) })
                .ToListAsync();
            var byLink = totals.Where(t => t.InviteLinkId != null).ToDictionary(t => t.InviteLinkId!.Value);
            var organic = totals.FirstOrDefault(t => t.InviteLinkId == null);

            return Results.Ok(new
            {
                links = links.Select(l => new
                {
                    id = l.Id,
                    name = l.Name,
                    inviteLink = l.InviteLink,
                    createdAt = l.CreatedAt,
                    revokedAt = l.RevokedAt,
                    joins = byLink.TryGetValue(l.Id, out var t) ? t.Joins : 0,
                    leaves = byLink.TryGetValue(l.Id, out var v) ? v.Leaves : 0,
                    net = (byLink.TryGetValue(l.Id, out var n) ? n.Joins - n.Leaves : 0),
                }),
                // Joins with no named link plus every leave — Telegram never attributes a leave.
                organic = new { joins = organic?.Joins ?? 0, leaves = organic?.Leaves ?? 0 },
            });
        });

        group.MapDelete("/invite-links/{linkId:guid}", async (Guid id, Guid linkId, ClaimsPrincipal user,
            CedarDbContext db, TelegramBotService bot) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == uid);
            if (channel is null) return Results.NotFound();

            var link = await db.ChannelInviteLinks
                .FirstOrDefaultAsync(l => l.Id == linkId && l.OwnerId == uid && l.ChannelId == id);
            if (link is null) return Results.NotFound();

            if (link.RevokedAt is null)
            {
                if (!bot.IsRunning)
                    return Results.Json(new { error = ErrorMessages.BotNotRunningNoToken }, statusCode: StatusCodes.Status503ServiceUnavailable);
                try
                {
                    await bot.Client.RevokeChatInviteLink(channel.TelegramChatId, link.InviteLink);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                // Revoked, never deleted: the daily tallies this link attributed still point here,
                // and the table keeps answering which source brought whom.
                link.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(new { id = link.Id, revokedAt = link.RevokedAt });
        });

        group.MapGet("/member-flow", async (Guid id, ClaimsPrincipal user, CedarDbContext db, int days = 30) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Channels.AnyAsync(c => c.Id == id && c.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var since = DateTime.UtcNow.Date.AddDays(-Math.Clamp(days, 1, 180));
            var rows = await db.ChannelMemberDailies
                .Where(d => d.OwnerId == uid && d.ChannelId == id && d.Day >= since)
                .OrderBy(d => d.Day)
                .Select(d => new { day = d.Day, inviteLinkId = d.InviteLinkId, joins = d.Joins, leaves = d.Leaves })
                .ToListAsync();
            return Results.Ok(rows);
        });
    }
}
