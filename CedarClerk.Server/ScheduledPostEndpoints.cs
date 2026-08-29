using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Publishing;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class ScheduledPostEndpoints
{
    // Language nullable rather than a literal default — it resolves against the draft's own
    // primary language (ADR-064), which is not Russian for every draft any more.
    //
    // TargetId is how a non-Telegram network is scheduled (ADR-099); ChatId is what a Telegram
    // caller still sends. Exactly one of the two identifies the destination, and either way the
    // stored row ends up carrying a TargetId.
    public record ScheduleRequest(Guid DraftId, DateTime ScheduledAtUtc, string? ChatId = null,
        Guid? TargetId = null, string Format = Consts.ContentTypes.Markdown, string? Language = null,
        bool Silent = false, bool Pin = false);

    // Wave 2 item 9 — drag-reschedule on the calendar. Body carries the one thing that moves.
    public record RescheduleRequest(DateTime ScheduledAtUtc);

    public static void MapScheduledPostEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/posts/scheduled").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var posts = await db.ScheduledPosts.Where(p => p.OwnerId == uid)
                .OrderBy(p => p.ScheduledAtUtc)
                .Join(db.Drafts, p => p.DraftId, d => d.Id, (p, d) => new
                {
                    p.Id, p.DraftId, DraftTitle = d.Title, p.ChatId, p.TargetId, p.Network, p.ScheduledAtUtc,
                    p.Status, p.Error, p.MessageId, p.Format, p.Language, p.SlotId, p.Silent, p.PinAfterSend,
                })
                .ToListAsync();

            var channels = await db.Channels.Where(c => c.OwnerId == uid).ToListAsync();
            // The name of a non-Telegram destination, which has no Channel row to read it from.
            var targetNames = await db.PublishTargets.Where(t => t.OwnerId == uid)
                .ToDictionaryAsync(t => t.Id, t => t.DisplayName);
            return Results.Ok(posts.Select(p =>
            {
                var trimmed = p.ChatId.Trim();
                var channel = trimmed.StartsWith('@')
                    ? channels.FirstOrDefault(c => string.Equals(c.Username, trimmed[1..], StringComparison.OrdinalIgnoreCase))
                    : long.TryParse(trimmed, out var numId) ? channels.FirstOrDefault(c => c.TelegramChatId == numId) : null;
                return new
                {
                    p.Id, p.DraftId, p.DraftTitle, p.ChatId, p.TargetId, p.Network, p.ScheduledAtUtc,
                    p.Status, p.Error, p.MessageId, p.Format, p.Language, p.SlotId, p.Silent, p.PinAfterSend,
                    ChannelTitle = channel?.Title,
                    TargetName = channel?.Title
                        ?? (p.TargetId is { } tid && targetNames.TryGetValue(tid, out var name) ? name : null),
                };
            }));
        });

        app.MapPost("/api/posts/schedule", async (ScheduleRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (draft is null)
                return Results.NotFound(new { error = ErrorMessages.DraftNotFoundPlain });
            
            // ADR-099 — one destination, named either way it can be named. A target id comes from
            // the export window for any network; a chat id is the Telegram-shaped call that predates
            // it, and it is resolved to a target here so every stored row carries one.
            string network;
            Guid targetId;
            var chatId = "";
            if (req.TargetId is { } requested)
            {
                var target = await db.PublishTargets.FirstOrDefaultAsync(
                    t => t.Id == requested && t.OwnerId == uid && t.IsActive);
                if (target is null)
                    return Results.Json(new { error = ErrorMessages.DestinationNotConnected }, statusCode: StatusCodes.Status403Forbidden);
                network = target.Network;
                targetId = target.Id;
                if (network == PublishNetworks.Telegram) chatId = target.RemoteId;
            }
            else if (!string.IsNullOrWhiteSpace(req.ChatId))
            {
                var channel = await SubscriptionPlan.ResolveOwnedChannelAsync(db, uid, req.ChatId);
                if (channel is null)
                    return Results.Json(new { error = ErrorMessages.ScheduleOnlyToOwnChannels }, statusCode: StatusCodes.Status403Forbidden);
                network = PublishNetworks.Telegram;
                targetId = (await TelegramTargetProjection.EnsureAsync(db, channel)).Id;
                chatId = req.ChatId;
            }
            else
            {
                return Results.BadRequest(new { error = ErrorMessages.PickADestination });
            }

            var language = req.Language ?? draft.PrimaryLanguage;
            if (language != draft.PrimaryLanguage)
            {
                var hasTranslation = await db.DraftTranslations.AnyAsync(t => t.DraftId == req.DraftId && t.Language == language);
                if (!hasTranslation)
                    return Results.BadRequest(new { error = ErrorMessages.NoVersionInLanguage(language) });
            }

            var post = new ScheduledPost
            {
                DraftId = req.DraftId,
                ChatId = chatId,
                TargetId = targetId,
                Network = network,
                ScheduledAtUtc = req.ScheduledAtUtc,
                OwnerId = uid,
                Format = req.Format,
                Language = language,
                Silent = req.Silent,
                PinAfterSend = req.Pin,
            };
            db.ScheduledPosts.Add(post);
            await db.SaveChangesAsync();
            return Results.Ok(new { post.Id });
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.ScheduledPosts
                .Where(p => p.Id == id && p.OwnerId == uid)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        // Wave 2 item 9 — moving a ticket on the calendar. Pending only: a Sent post already
        // happened and a Failed one wants a retry decision, not a new date; both answer 409.
        group.MapPatch("/{id:guid}", async (Guid id, RescheduleRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var (status, post) = await RescheduleAsync(db, uid, id, req.ScheduledAtUtc);
            return status switch
            {
                StatusCodes.Status200OK => Results.Ok(new { post!.Id, post.ScheduledAtUtc }),
                StatusCodes.Status404NotFound => Results.NotFound(),
                _ => Results.Json(new { error = ErrorMessages.ScheduledPostNotPending }, statusCode: StatusCodes.Status409Conflict),
            };
        });
    }

    /// <summary>The reschedule decision on its own, so a test drives it without the endpoint
    /// plumbing. 200 with the updated row, 404 for a missing/foreign id, 409 for a settled one.</summary>
    public static async Task<(int Status, ScheduledPost? Post)> RescheduleAsync(
        CedarDbContext db, string uid, Guid id, DateTime scheduledAtUtc)
    {
        var post = await db.ScheduledPosts.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
        if (post is null) return (StatusCodes.Status404NotFound, null);
        if (post.Status != "Pending") return (StatusCodes.Status409Conflict, null);

        post.ScheduledAtUtc = scheduledAtUtc;
        await db.SaveChangesAsync();
        return (StatusCodes.Status200OK, post);
    }
}
