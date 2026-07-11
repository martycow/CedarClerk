using System.Security.Claims;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

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

                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var tier = await db.Users.Where(u => u.Id == uid).Select(u => u.PlanTier).FirstAsync();
                var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);
                if (!PlanQuotas.HasStorageRoom(tier, usedBytes, file.Length))
                    return Results.Json(new { error = $"Free plan storage limit ({PlanQuotas.FreeStorageBytes / (1024 * 1024)}MB) exceeded. Upgrade to Pro for more." }, statusCode: StatusCodes.Status403Forbidden);

                var asset = new Asset
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    SizeBytes = file.Length,
                    OwnerId = uid,
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