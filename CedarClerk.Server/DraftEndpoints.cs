using System.Security.Claims;
using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftEndpoints
{
    public record SaveDraftRequest(string Title, string CedarJson);

    public static void MapDraftEndpoints(this WebApplication app)
    {
        var groupBuilder = app.MapGroup("/api/drafts").RequireAuthorization();
        
        // List of drafts
        groupBuilder.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await db.Drafts.Where(d => d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new { d.Id, d.Title, d.CreatedAt, d.UpdatedAt })
                .ToListAsync();
        });

        // Specific draft
        groupBuilder.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            return draft is null ? Results.NotFound() : Results.Ok(new { draft.Id, draft.Title, draft.CedarJson, draft.CreatedAt, draft.UpdatedAt });
        });

        // Create new draft
        groupBuilder.MapPost("/", async (SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = new Draft { Title = req.Title, CedarJson = req.CedarJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        });

        // Update specific draft
        groupBuilder.MapPut("/{id:guid}", async (Guid id, SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.Title = req.Title;
            draft.CedarJson = req.CedarJson;
            draft.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.UpdatedAt });
        });

        // Delete specific draft
        groupBuilder.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.Drafts
                .Where(x => x.Id == id && x.OwnerId == uid)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}