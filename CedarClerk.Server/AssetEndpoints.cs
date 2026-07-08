using System.Security.Claims;
using CedarClerk.Server.Data;

namespace CedarClerk.Server;

public record MediaPaths(string Dir);

public static class AssetEndpoints
{
    internal const long ImageMaxBytes = 5 * 1024 * 1024;
    private const long MediaMaxBytes = 20 * 1024 * 1024;

    private static readonly Dictionary<string, (string Ext, long MaxBytes)> Allowed = new()
    {
        ["image/jpeg"] = (".jpg", ImageMaxBytes),
        ["image/png"]  = (".png", ImageMaxBytes),
        ["image/gif"]  = (".gif", ImageMaxBytes),
        ["image/webp"] = (".webp", ImageMaxBytes),
        ["video/mp4"]  = (".mp4", MediaMaxBytes),
        ["audio/mpeg"] = (".mp3", MediaMaxBytes),
        ["audio/ogg"]  = (".ogg", MediaMaxBytes),
    };

    public static void MapAssetEndpoints(this WebApplication app)
    {
        app.MapPost("/api/assets", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
            {
                if (!Allowed.TryGetValue(file.ContentType, out var allowed))
                    return Results.BadRequest(new { error = $"Unsupported type: {file.ContentType}" });

                var (ext, maxBytes) = allowed;
                if (file.Length == 0 || file.Length > maxBytes)
                    return Results.BadRequest(new { error = $"File is too large ({maxBytes / (1024 * 1024)}MB Maximum)" });

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