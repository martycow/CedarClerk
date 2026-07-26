using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Real, named, user-managed drafts grouping — one folder per draft (see the ADR following
// ADR-038, docs/DECISIONS.md). Unlike Tags (a flat unmanaged string), folders are their own
// entity with create/rename/delete, so they get a dedicated endpoints file.
public static class FolderEndpoints
{
    public record UpsertFolderRequest(string Name);

    private const int FolderNameMaxLength = 60;

    public static void MapFolderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/folders").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var folders = await db.Folders.Where(f => f.OwnerId == uid)
                .OrderBy(f => f.Name)
                .ToListAsync();
            var counts = await db.Drafts.Where(d => d.OwnerId == uid && d.FolderId != null)
                .GroupBy(d => d.FolderId)
                .Select(g => new { FolderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.FolderId!.Value, g => g.Count);

            return Results.Ok(folders.Select(f => new { f.Id, f.Name, Count = counts.GetValueOrDefault(f.Id) }));
        });

        group.MapPost("/", async (UpsertFolderRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var name = req.Name.Trim();
            if (name.Length == 0 || name.Length > FolderNameMaxLength)
                return Results.Json(new { error = $"Folder name must be 1-{FolderNameMaxLength} characters" }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var folder = new Folder { OwnerId = uid, Name = name };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            return Results.Ok(new { folder.Id, folder.Name });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertFolderRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var name = req.Name.Trim();
            if (name.Length == 0 || name.Length > FolderNameMaxLength)
                return Results.Json(new { error = $"Folder name must be 1-{FolderNameMaxLength} characters" }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid);
            if (folder is null) return Results.NotFound();

            folder.Name = name;
            await db.SaveChangesAsync();
            return Results.Ok(new { folder.Id, folder.Name });
        });

        // Unassigns every draft in this folder (FolderId -> null) rather than deleting them —
        // a folder is purely an organizational label, deleting it must never touch draft content.
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == uid);
            if (folder is null) return Results.NotFound();

            await db.Drafts.Where(d => d.FolderId == id && d.OwnerId == uid)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.FolderId, d => null));

            db.Folders.Remove(folder);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
