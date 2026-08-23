using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
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
        ["application/pdf"] = (".pdf", Consts.FileSizes.MediaMaxBytes),
        ["application/zip"] = (".zip", Consts.FileSizes.MediaMaxBytes),
        ["application/x-zip-compressed"] = (".zip", Consts.FileSizes.MediaMaxBytes),
        ["text/plain"] = (".txt", Consts.FileSizes.MediaMaxBytes),
        ["text/markdown"] = (".md", Consts.FileSizes.MediaMaxBytes),
        ["text/csv"] = (".csv", Consts.FileSizes.MediaMaxBytes),
        ["application/json"] = (".json", Consts.FileSizes.MediaMaxBytes),
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = (".docx", Consts.FileSizes.MediaMaxBytes),
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = (".xlsx", Consts.FileSizes.MediaMaxBytes),
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = (".pptx", Consts.FileSizes.MediaMaxBytes),
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

                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                var bytes = ImageMetadataStripper.Strip(buffer.ToArray(), file.ContentType);

                var asset = new Asset
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    SizeBytes = bytes.Length,
                    OwnerId = uid,
                };
                asset.LocalPath = $"asset_{asset.Id}{ext}";
                await File.WriteAllBytesAsync(Path.Combine(media.Dir, asset.LocalPath), bytes);

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

        // ADR-127 — the owner-wide library behind /media. One response carries the page, the
        // unfiltered type counts (chips must not shrink when a filter is on) and the quota line.
        app.MapGet("/api/assets", async (ClaimsPrincipal user, CedarDbContext db, string? q, string? type, int skip = 0, int take = 60) =>
            {
                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                skip = Math.Max(0, skip);
                take = Math.Clamp(take, 1, 200);

                var owned = db.Assets.AsNoTracking().Where(a => a.OwnerId == uid);

                var typeCounts = await owned.GroupBy(a => a.ContentType)
                    .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
                var counts = new
                {
                    image = typeCounts.Where(c => c.Key.StartsWith("image/")).Sum(c => c.Count),
                    video = typeCounts.Where(c => c.Key.StartsWith("video/")).Sum(c => c.Count),
                    audio = typeCounts.Where(c => c.Key.StartsWith("audio/")).Sum(c => c.Count),
                };

                var filtered = owned;
                if (!string.IsNullOrWhiteSpace(q))
                    filtered = filtered.Where(a => a.FileName.Contains(q.Trim()));
                if (type is "image" or "video" or "audio")
                    filtered = filtered.Where(a => a.ContentType.StartsWith(type + "/"));

                var total = await filtered.CountAsync();
                var items = await filtered.OrderByDescending(a => a.CreatedAt)
                    .Skip(skip).Take(take)
                    .Select(a => new { a.Id, a.FileName, a.LocalPath, a.ContentType, a.SizeBytes, a.CreatedAt })
                    .ToListAsync();

                var usedBytes = await owned.SumAsync(a => (long?)a.SizeBytes) ?? 0;
                var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);

                return Results.Ok(new { items, total, counts, usedBytes, limitBytes = PlanLimitations.StorageLimitBytes(tier) });
            })
            .RequireAuthorization();

        // ADR-127 — delete by on-demand scan over every document and translation the owner has.
        // Busy answers 409 with the referencing drafts so the reader knows where to go; free
        // removes the file, the Telegram derivative and the row.
        app.MapDelete("/api/assets/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
            {
                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == uid);
                if (asset is null) return Results.NotFound();

                var name = asset.LocalPath;
                var drafts = await db.Drafts.AsNoTracking().Where(d => d.OwnerId == uid)
                    .Select(d => new { d.Id, d.Title, d.CedarJson }).ToListAsync();
                var busyIds = drafts
                    .Where(d => CedarPackage.FindReferencedMediaPathsSafe(d.CedarJson).Contains(name))
                    .Select(d => d.Id).ToHashSet();

                var draftIds = drafts.Select(d => d.Id).ToList();
                var translations = await db.DraftTranslations.AsNoTracking()
                    .Where(t => draftIds.Contains(t.DraftId))
                    .Select(t => new { t.DraftId, t.CedarJson }).ToListAsync();
                foreach (var t in translations)
                {
                    if (busyIds.Contains(t.DraftId)) continue;
                    if (CedarPackage.FindReferencedMediaPathsSafe(t.CedarJson).Contains(name))
                        busyIds.Add(t.DraftId);
                }

                if (busyIds.Count > 0)
                {
                    var usedBy = drafts.Where(d => busyIds.Contains(d.Id))
                        .Select(d => new { draftId = d.Id, title = d.Title }).ToList();
                    return Results.Json(new { error = ErrorMessages.AssetInUse, usedBy },
                        statusCode: StatusCodes.Status409Conflict);
                }

                await Modules.IndieDev.ProjectLinks.RemoveAllForAsync(
                    db, uid, LinkTargets.Attachment, asset.Id);
                var publicUrl = $"/media/{asset.LocalPath}";
                await db.Projects
                    .Where(p => p.OwnerId == uid && p.CoverUrl == publicUrl)
                    .ExecuteUpdateAsync(update => update.SetProperty(p => p.CoverUrl, (string?)null));

                DeleteIfExists(Path.Combine(media.Dir, asset.LocalPath));
                if (asset.TelegramLocalPath is not null)
                    DeleteIfExists(Path.Combine(media.Dir, asset.TelegramLocalPath));

                db.Assets.Remove(asset);
                await db.SaveChangesAsync();
                return Results.Ok();
            })
            .RequireAuthorization();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
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
