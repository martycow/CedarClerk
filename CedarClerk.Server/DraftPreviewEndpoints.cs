using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// The owner's side of shareable draft preview links (Wave 1 item 8). One active link per draft:
/// POST creates or rotates the token, DELETE revokes it, and the token itself is the whole
/// credential — the public page (<c>/preview/{token}</c>, BlogEndpoints.HandleDraftPreviewAsync)
/// answers a wrong or revoked token with a plain 404.
/// </summary>
public static class DraftPreviewEndpoints
{
    public static void MapDraftPreviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/drafts").RequireAuthorization();

        group.MapPost("/{id:guid}/preview-link", async (Guid id, ClaimsPrincipal user, CedarDbContext db, HttpContext ctx) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            // Creating again rotates: the old link stops working the moment a new one exists,
            // which is what "one active link per draft" means.
            draft.PreviewToken = PrivateAccess.NewToken();
            await db.SaveChangesAsync();

            return Results.Ok(new { url = PreviewUrl(ctx, draft.PreviewToken) });
        });

        group.MapDelete("/{id:guid}/preview-link", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.PreviewToken = null;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>The app host's own address — a preview is working material and never lives on the
    /// public blog host.</summary>
    public static string PreviewUrl(HttpContext ctx, string token) =>
        $"{ctx.Request.Scheme}://{ctx.Request.Host}/preview/{token}";
}
