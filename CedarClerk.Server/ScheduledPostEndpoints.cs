using System.Security.Claims;
using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class ScheduledPostEndpoints
{
    public record ScheduleRequest(Guid DraftId, string ChatId, DateTime ScheduledAtUtc, string Format = "Html");

    public static void MapScheduledPostEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/posts/scheduled").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await db.ScheduledPosts.Where(p => p.OwnerId == uid)
                .OrderBy(p => p.ScheduledAtUtc)
                .Join(db.Drafts, p => p.DraftId, d => d.Id, (p, d) => new
                {
                    p.Id, p.DraftId, DraftTitle = d.Title, p.ChatId, p.ScheduledAtUtc,
                    p.Status, p.Error, p.MessageId, p.Format,
                })
                .ToListAsync();
        });

        app.MapPost("/api/posts/schedule", async (ScheduleRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draftExists = await db.Drafts.AnyAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (!draftExists)
                return Results.NotFound(new { error = "Draft not found" });

            var post = new ScheduledPost
            {
                DraftId = req.DraftId,
                ChatId = req.ChatId,
                ScheduledAtUtc = req.ScheduledAtUtc,
                OwnerId = uid,
                Format = req.Format,
            };
            db.ScheduledPosts.Add(post);
            await db.SaveChangesAsync();
            return Results.Ok(new { post.Id });
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            // Deletes regardless of status: for "Pending" this cancels the job, for "Sent"/"Failed"
            // it just clears the completed entry from the list — nothing left to cancel there.
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.ScheduledPosts
                .Where(p => p.Id == id && p.OwnerId == uid)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}
