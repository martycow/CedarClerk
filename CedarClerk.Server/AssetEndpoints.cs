using System.Security.Claims;
using CedarClerk.Server.Data;

namespace CedarClerk.Server;

public record MediaPaths(string Dir);

public static class AssetEndpoints
{
    // 5 MBytes at max for a file
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"]  = ".png",
        ["image/gif"]  = ".gif",
        ["image/webp"] = ".webp",
    };

    public static void MapAssetEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assets", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
            {
                if (!Allowed.TryGetValue(file.ContentType, out var ext))
                    return Results.BadRequest(new { error = $"Unsupported type: {file.ContentType}" });
                
                if (file.Length is 0 or > MaxBytes)
                    return Results.BadRequest(new { error = "File is too large (5MB Maximum)" });

                var asset = new Asset
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    SizeBytes = file.Length,
                    OwnerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!,
                };
                asset.LocalPath = $"asset_{asset.Id}{ext}";

                await using (var fs = File.Create(Path.Combine(media.Dir, asset.LocalPath)))
                    await file.CopyToAsync(fs);

                db.Assets.Add(asset);
                await db.SaveChangesAsync();

                return Results.Ok(new { id = asset.Id, url = $"/media/{asset.LocalPath}" });
            })
            .RequireAuthorization()
            .DisableAntiforgery();
    }
}