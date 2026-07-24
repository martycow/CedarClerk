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
        app.MapPost("/api/assets", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media, ILogger<Asset> logger) =>
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

                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                await File.WriteAllBytesAsync(Path.Combine(media.Dir, asset.LocalPath), buffer.ToArray());

                db.Assets.Add(asset);
                await db.SaveChangesAsync();

                // Pre-generate the Telegram-safe derivative now (not just lazily at publish time)
                // so a normal publish right after upload doesn't pay the compression cost inline.
                // Always the "standard" target — a per-publish compression level (export modal)
                // only affects what PostEndpoints.PublishAsync asks for at send time.
                await EnsureTelegramSafeAsync(asset, media, db, logger, Consts.FileSizes.TelegramSafeImageBytes);

                return Results.Ok(new { id = asset.Id, url = $"/media/{asset.LocalPath}" });
            })
            .RequireAuthorization()
            .DisableAntiforgery();
    }

    // Telegram rejects a photo fetched by URL above ~TelegramSafeImageBytes with a misleading
    // "wrong type of the web page content" error (confirmed empirically 19.07.2026 — see ADR in
    // docs/DECISIONS.md). Generates a resized/recompressed JPEG derivative for Telegram sends only
    // — blog/.cedar export always keep the original untouched. JPEG only: camera photos are the
    // actual reported case, and PNG/GIF/WebP are rare for this while re-encoding them as JPEG
    // would lose transparency/animation. Called both right after upload (AssetEndpoints, above)
    // and lazily from PostEndpoints.PublishAsync, so assets uploaded before this feature existed
    // (or where compression didn't run for any reason) still get a derivative on next publish
    // attempt instead of failing forever.
    //
    // targetMaxBytes lets the caller ask for a different compression degree (the export modal's
    // compression-level control) than whatever produced a previously-cached derivative — a cached
    // file already under the requested budget is reused as-is (no point recompressing something
    // that already fits); anything else is regenerated, since a looser target might have produced
    // a derivative bigger than what's now being asked for.
    internal static async Task EnsureTelegramSafeAsync(Asset asset, MediaPaths media, CedarDbContext db, ILogger? logger, long targetMaxBytes)
    {
        if (asset.ContentType != "image/jpeg" || asset.SizeBytes <= targetMaxBytes)
            return;

        if (asset.TelegramLocalPath is not null)
        {
            var cachedPath = Path.Combine(media.Dir, asset.TelegramLocalPath);
            if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length <= targetMaxBytes)
                return;
        }

        var originalPath = Path.Combine(media.Dir, asset.LocalPath);
        if (!File.Exists(originalPath))
            return;

        var original = await File.ReadAllBytesAsync(originalPath);
        var compressed = ImageCompressor.TryCompressJpeg(original, targetMaxBytes, logger);
        if (compressed is null)
            return;

        asset.TelegramLocalPath = $"asset_{asset.Id}_tg.jpg";
        await File.WriteAllBytesAsync(Path.Combine(media.Dir, asset.TelegramLocalPath), compressed);
        await db.SaveChangesAsync();
    }
}