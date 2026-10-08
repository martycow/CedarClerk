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
        app.MapPost("/api/assets", async (IFormFile file, Guid? projectId, ClaimsPrincipal user, CedarDbContext db, MediaPaths media, ILogger<Asset> logger) =>
            {
                if (!Allowed.TryGetValue(file.ContentType, out var allowed))
                    return Results.BadRequest(new { error = ErrorMessages.UnsupportedFileType(file.ContentType) });

                var (ext, maxBytes) = allowed;
                if (file.Length == 0 || file.Length > maxBytes)
                    return Results.BadRequest(new { error = ErrorMessages.FileTooLarge(maxBytes / (1024 * 1024)) });

                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid && p.ArchivedAt == null))
                    return Results.NotFound();
                var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
                var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);

                if (!PlanLimitations.HasStorageRoom(tier, usedBytes, file.Length))
                {
                    var planLimitMb = PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024);

                    return Results.Json(
                        new { error = ErrorMessages.StorageLimitExceeded(planLimitMb) },
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
                    ProjectId = projectId,
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

        // ADR-238 — the three facts a media node cannot carry: resolution, byte size and which file
        // it is. Owner-scoped, because an asset guid on a blog-published document is public
        // knowledge and this must not become a way to read another account's library.
        app.MapGet("/api/assets/meta", async (Guid? id, string? path, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
            {
                if (id is null && string.IsNullOrWhiteSpace(path))
                    return Results.BadRequest(new { error = ErrorMessages.AssetIdOrPathRequired });

                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var meta = await LookupMetaAsync(db, uid, id, path, media);
                return meta is null ? Results.NotFound() : Results.Ok(meta);
            })
            .RequireAuthorization();

        // ADR-127 — the owner-wide library behind /media. One response carries the page, the
        // unfiltered type counts (chips must not shrink when a filter is on) and the quota line.
        // ADR-204 — `project` is a guid, or the literal "none" for the files that belong to no
        // project. Absent means every bucket, which is what the library page opens on.
        app.MapGet("/api/assets", async (
            ClaimsPrincipal user, CedarDbContext db, string? q, string? type, string? project,
            string sort = "added", string direction = "desc", int skip = 0, int take = 60) =>
            {
                var validationError = ValidateLibraryCollectionQuery(type, project, sort, direction);
                if (validationError is not null)
                    return Results.BadRequest(new { error = validationError });

                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                skip = Math.Max(0, skip);
                take = Math.Clamp(take, 1, 200);

                // `all` is every file the owner has and is what the quota is measured over; `owned`
                // is the bucket being looked at, and the type chips and the list read off it.
                var all = db.Assets.AsNoTracking().Where(a => a.OwnerId == uid);

                // Buckets are counted over everything, like the type counts below: a strip whose
                // numbers move when a filter is on cannot be read as a total.
                var bucketRows = await all.GroupBy(a => a.ProjectId)
                    .Select(g => new { ProjectId = g.Key, Count = g.Count() }).ToListAsync();
                var buckets = bucketRows.Select(b => new { projectId = b.ProjectId, count = b.Count }).ToList();

                var owned = all;
                if (project is "none")
                    owned = owned.Where(a => a.ProjectId == null);
                else if (Guid.TryParse(project, out var projectId))
                    owned = owned.Where(a => a.ProjectId == projectId);

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
                var ordered = OrderForLibrary(filtered, sort, direction);
                var items = await ordered
                    .Skip(skip).Take(take)
                    .Select(a => new { a.Id, a.FileName, a.LocalPath, a.ContentType, a.SizeBytes, a.CreatedAt, a.ProjectId })
                    .ToListAsync();

                var usedBytes = await all.SumAsync(a => (long?)a.SizeBytes) ?? 0;
                var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);

                return Results.Ok(new { items, total, counts, buckets, usedBytes, limitBytes = PlanLimitations.StorageLimitBytes(tier) });
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
                await db.Projects
                    .Where(p => p.OwnerId == uid && p.BannerUrl == publicUrl)
                    .ExecuteUpdateAsync(update => update.SetProperty(p => p.BannerUrl, (string?)null));

                DeleteIfExists(Path.Combine(media.Dir, asset.LocalPath));
                if (asset.TelegramLocalPath is not null)
                    DeleteIfExists(Path.Combine(media.Dir, asset.TelegramLocalPath));

                db.Assets.Remove(asset);
                await db.SaveChangesAsync();
                return Results.Ok();
            })
            .RequireAuthorization();
    }

    public static string? ValidateLibraryCollectionQuery(
        string? type, string? project, string sort, string direction)
    {
        if (type is not null && type is not ("image" or "video" or "audio"))
            return ErrorMessages.UnknownMediaTypeFilter;
        if (!string.IsNullOrWhiteSpace(project) && project != "none" && !Guid.TryParse(project, out _))
            return ErrorMessages.UnknownMediaProjectFilter;
        if (sort is not ("name" or "type" or "size" or "added"))
            return ErrorMessages.UnknownMediaSortKey;
        if (direction is not ("asc" or "desc"))
            return ErrorMessages.UnknownMediaSortDirection;
        return null;
    }

    public static IOrderedQueryable<Asset> OrderForLibrary(
        IQueryable<Asset> query, string sort, string direction) => (sort, direction) switch
    {
        ("name", "asc") => query.OrderBy(a => a.FileName).ThenBy(a => a.Id),
        ("name", "desc") => query.OrderByDescending(a => a.FileName).ThenBy(a => a.Id),
        ("type", "asc") => query.OrderBy(a => a.ContentType).ThenBy(a => a.FileName).ThenBy(a => a.Id),
        ("type", "desc") => query.OrderByDescending(a => a.ContentType).ThenBy(a => a.FileName).ThenBy(a => a.Id),
        ("size", "asc") => query.OrderBy(a => a.SizeBytes).ThenBy(a => a.Id),
        ("size", "desc") => query.OrderByDescending(a => a.SizeBytes).ThenBy(a => a.Id),
        ("added", "asc") => query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id),
        _ => query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id),
    };

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public record AssetMeta(Guid Id, string FileName, string ContentType, long SizeBytes,
        int? Width, int? Height, Guid? ProjectId);

    /// <summary>
    /// One asset by id, or by the <c>LocalPath</c> an older document carries as its <c>src</c>
    /// (ADR-238 clause 4) — an id-only lookup would answer for nothing written before today.
    /// Null when nothing this owner has matches.
    /// </summary>
    public static async Task<AssetMeta?> LookupMetaAsync(
        CedarDbContext db, string ownerId, Guid? id, string? path, MediaPaths media)
    {
        var localPath = path?.Trim();
        if (localPath is not null && localPath.StartsWith("/media/", StringComparison.Ordinal))
            localPath = localPath["/media/".Length..];

        var asset = id is { } assetId
            ? await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId && a.OwnerId == ownerId)
            : localPath is { Length: > 0 }
                ? await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.LocalPath == localPath && a.OwnerId == ownerId)
                : null;
        if (asset is null) return null;

        var (width, height) = Dimensions(Path.Combine(media.Dir, asset.LocalPath), asset.ContentType);
        return new AssetMeta(asset.Id, asset.FileName, asset.ContentType, asset.SizeBytes, width, height, asset.ProjectId);
    }

    // Identify reads the header, not the pixels — which is what lets this run per selection instead
    // of becoming two columns and a backfill over the whole media directory (ADR-238 clause 5). It
    // throws for formats it does not know, and a file can be missing entirely; either is "unknown
    // resolution", never a failed request.
    private static (int? Width, int? Height) Dimensions(string fullPath, string contentType)
    {
        if (!contentType.StartsWith("image/", StringComparison.Ordinal)) return (null, null);
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(fullPath);
            return (info.Width, info.Height);
        }
        catch (Exception)
        {
            return (null, null);
        }
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
