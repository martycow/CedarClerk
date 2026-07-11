using System.Security.Claims;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public record MediaPaths(string Dir);

public static class AssetEndpoints
{
    private static readonly Dictionary<string, (string Ext, long MaxBytes)> Allowed = new()
    {
        ["image/jpeg"] = (".jpg", Consts.FileSizes.ImageMaxBytes),
        ["image/png"]  = (".png", Consts.FileSizes.ImageMaxBytes),
        ["image/gif"]  = (".gif", Consts.FileSizes.ImageMaxBytes),
        ["image/webp"] = (".webp", Consts.FileSizes.ImageMaxBytes),
        ["video/mp4"]  = (".mp4", Consts.FileSizes.MediaMaxBytes),
        ["audio/mpeg"] = (".mp3", Consts.FileSizes.MediaMaxBytes),
        ["audio/ogg"]  = (".ogg", Consts.FileSizes.MediaMaxBytes),
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
                var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
                var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);
                
                if (!PlanLimitations.HasStorageRoom(tier, usedBytes, file.Length))
                {
                    var planLimitMb = PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024);
                    
                    return Results.Json(
                        new { error = $"Storage limit of your plan ({planLimitMb}MB) exceeded. Upgrade for more." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

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