using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class ScheduledPostEndpoints
{
    public record ScheduleRequest(Guid DraftId, string ChatId, DateTime ScheduledAtUtc, string Format = Consts.ContentTypes.Html, string Language = Languages.Primary);

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
                    p.Id, p.DraftId, DraftTitle = d.Title, p.ChatId, p.ScheduledAtUtc,
                    p.Status, p.Error, p.MessageId, p.Format, p.Language,
                })
                .ToListAsync();
            
            var channels = await db.Channels.Where(c => c.OwnerId == uid).ToListAsync();
            return Results.Ok(posts.Select(p =>
            {
                var trimmed = p.ChatId.Trim();
                var channel = trimmed.StartsWith('@')
                    ? channels.FirstOrDefault(c => string.Equals(c.Username, trimmed[1..], StringComparison.OrdinalIgnoreCase))
                    : long.TryParse(trimmed, out var numId) ? channels.FirstOrDefault(c => c.TelegramChatId == numId) : null;
                return new
                {
                    p.Id, p.DraftId, p.DraftTitle, p.ChatId, p.ScheduledAtUtc,
                    p.Status, p.Error, p.MessageId, p.Format, p.Language,
                    ChannelTitle = channel?.Title,
                };
            }));
        });

        app.MapPost("/api/posts/schedule", async (ScheduleRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draftExists = await db.Drafts.AnyAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (!draftExists)
                return Results.NotFound(new { error = "Draft not found" });
            
            if (await SubscriptionPlan.ResolveOwnedChannelAsync(db, uid, req.ChatId) is null)
                return Results.Json(new { error = "You can only schedule posts to your connected channels — connect this channel first (Channels popup)" }, statusCode: StatusCodes.Status403Forbidden);

            if (req.Language != Languages.Primary)
            {
                var hasTranslation = await db.DraftTranslations.AnyAsync(t => t.DraftId == req.DraftId && t.Language == req.Language);
                if (!hasTranslation)
                    return Results.BadRequest(new { error = $"No {req.Language.ToUpperInvariant()} version of this draft" });
            }

            var post = new ScheduledPost
            {
                DraftId = req.DraftId,
                ChatId = req.ChatId,
                ScheduledAtUtc = req.ScheduledAtUtc,
                OwnerId = uid,
                Format = req.Format,
                Language = req.Language,
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
    }
}
