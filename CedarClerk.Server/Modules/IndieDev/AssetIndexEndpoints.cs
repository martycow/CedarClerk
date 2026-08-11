using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// T-122 (ADR-107) — the asset index: paths and metadata, never bytes.
//
// ## Why there is a second flag on top of the module's
//
// These endpoints make the SERVER walk the SERVER's disk on a tenant's say-so. On a laptop that is
// the whole point; on the Pi, which serves every account from one process, it is a stranger being
// able to enumerate `/etc` and read back the filenames. So indexing is off unless
// `Cedar:AssetIndex:Enabled` says otherwise, and the only thing that says otherwise is the desktop
// shell, which sets it for its own single-user process.
//
// Listing what is already indexed stays available either way — those rows are owner-scoped like
// everything else, and refusing to show them would break a project opened from the web after
// being indexed on the desktop.
// NOTE the name: the server root already has an `AssetEndpoints.MapAssetEndpoints` for uploaded
// post media, and two extension methods with one name on WebApplication is an ambiguity waiting to
// resolve the wrong way. These are two different things (ADR-107) and now they read as two.
public static class AssetIndexEndpoints
{
    public record IndexRequest(string? Path);

    public const string EnabledKey = "Cedar:AssetIndex:Enabled";

    /// <summary>
    /// The extensions a thumbnail exists for, as a set the list query can translate to SQL.
    /// <c>AssetKinds.CanPreview</c> takes a path and so cannot be called inside a LINQ-to-SQLite
    /// projection; this is the same answer in a form EF can send to the database.
    /// </summary>
    private static readonly string[] PreviewableExtensions =
        ["png", "jpg", "jpeg", "gif", "bmp", "webp", "tga", "tif", "tiff", "pbm", "qoi", "blend", "blend1", "blend2"];

    /// <summary>Whether this installation may walk its own filesystem. False on the Pi, by omission.</summary>
    public static bool IndexingEnabled(IConfiguration config) => config.IsOn(EnabledKey);

    public static void MapAssetIndexEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/assets").RequireAuthorization();

        group.MapGet("/", async (
            Guid projectId, ClaimsPrincipal user, CedarDbContext db,
            string? kind = null, string? search = null, bool missing = false, int skip = 0, int take = 60) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            var query = db.AssetEntries.Where(a => a.ProjectId == projectId && a.OwnerId == uid);
            if (missing) query = query.Where(a => a.MissingSince != null);
            if (kind is not null && AssetKinds.IsKnown(kind)) query = query.Where(a => a.Kind == kind);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim();
                query = query.Where(a => EF.Functions.Like(a.RelativePath, $"%{needle}%"));
            }

            // Counts for the filter chips come from the unfiltered set: a chip that only knows its
            // own total cannot say how many of the others there are, which is what the chips are for.
            var all = db.AssetEntries.Where(a => a.ProjectId == projectId && a.OwnerId == uid);
            var byKind = await all.GroupBy(a => a.Kind)
                .Select(g => new { Kind = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Kind, g => g.Count);

            var total = await query.CountAsync();
            // take is clamped, not trusted: the grid virtualises, and an unbounded page over a
            // hundred thousand rows is a request that never returns.
            var page = await query
                .OrderBy(a => a.RelativePath)
                .Skip(Math.Max(0, skip))
                .Take(Math.Clamp(take, 1, 200))
                .Select(a => new
                {
                    a.Id, a.RelativePath, a.FileName, a.Extension, a.Kind,
                    a.SizeBytes, a.ModifiedAt, a.IndexedAt, a.MissingSince,
                    a.Width, a.Height, a.DurationMs, a.SampleRate,
                    // The grid asks for a thumbnail only where one can exist — an <img> pointed at a
                    // 404 renders as a broken-image icon, which is exactly what this screen must
                    // never show. Decided per extension, not per kind: a PSD is an image nothing
                    // here can decode, and a .blend is a model that carries its own picture.
                    hasThumbnail = a.MissingSince == null && PreviewableExtensions.Contains(a.Extension),
                })
                .ToListAsync();

            return Results.Ok(new
            {
                rootPath = project.AssetRootPath,
                indexedAt = project.AssetsIndexedAt,
                total,
                totalIndexed = await all.CountAsync(),
                missingCount = await all.CountAsync(a => a.MissingSince != null),
                byKind,
                items = page,
            });
        });

        group.MapGet("/{assetId:guid}", async (Guid projectId, Guid assetId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            var asset = await db.AssetEntries
                .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid);
            if (asset is null) return Results.NotFound();

            return Results.Ok(new
            {
                asset.Id, asset.RelativePath, asset.FileName, asset.Extension, asset.Kind,
                asset.SizeBytes, asset.ModifiedAt, asset.IndexedAt, asset.MissingSince,
                asset.Width, asset.Height, asset.DurationMs, asset.SampleRate,
                hasThumbnail = asset.MissingSince == null && PreviewableExtensions.Contains(asset.Extension),
                // The absolute path is what "Reveal in file manager" needs, and it is only
                // meaningful on the machine that did the indexing.
                fullPath = project.AssetRootPath is null
                    ? null
                    : Path.Combine(project.AssetRootPath, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            });
        });

        // Re-stat one file, for the "Re-index file" button on the asset view. Cheap, and it is how
        // a file that has come back stops reading as missing without re-walking the whole tree.
        group.MapPost("/{assetId:guid}/reindex", async (
            Guid projectId, Guid assetId, ClaimsPrincipal user, CedarDbContext db, IConfiguration config) =>
        {
            if (!IndexingEnabled(config))
                return Results.Json(new { error = ErrorMessages.AssetIndexingUnavailable }, statusCode: StatusCodes.Status403Forbidden);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project?.AssetRootPath is null) return Results.NotFound();

            var asset = await db.AssetEntries
                .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid);
            if (asset is null) return Results.NotFound();

            var fullPath = Path.Combine(project.AssetRootPath, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(fullPath);
            if (info.Exists)
            {
                asset.SizeBytes = info.Length;
                asset.ModifiedAt = info.LastWriteTimeUtc;
                asset.MissingSince = null;
            }
            else
            {
                asset.MissingSince ??= DateTime.UtcNow;
            }
            asset.IndexedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { asset.Id, asset.SizeBytes, asset.ModifiedAt, asset.IndexedAt, asset.MissingSince });
        });

        // T-140 — the thumbnail. Behind the same authorisation as everything else and NOT under
        // /media/*: an indexed file is somebody's unreleased game art on their own disk, while the
        // public media path is for what an author chose to publish.
        group.MapGet("/{assetId:guid}/thumb", async (
            Guid projectId, Guid assetId, ClaimsPrincipal user, CedarDbContext db,
            ThumbnailPaths thumbs, ILogger<AssetIndexService> logger) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project?.AssetRootPath is null) return Results.NotFound();

            var asset = await db.AssetEntries
                .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid);
            if (asset is null || asset.MissingSince is not null || !AssetMetadata.CanHaveThumbnail(asset.RelativePath))
                return Results.NotFound();

            var cached = thumbs.For(asset.Id);
            // Regenerated when the file has moved on since the cached copy, or a replaced sprite
            // would keep showing its predecessor until somebody noticed.
            if (asset.ThumbnailForModifiedAt != asset.ModifiedAt || !File.Exists(cached))
            {
                var source = Path.Combine(project.AssetRootPath,
                    asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!AssetMetadata.TryWriteThumbnail(source, cached, logger)) return Results.NotFound();
                asset.ThumbnailForModifiedAt = asset.ModifiedAt;
                await db.SaveChangesAsync();
            }

            return Results.File(cached, "image/jpeg", enableRangeProcessing: false);
        });

        // T-141 — documents this asset is linked to. Stated by the author, never discovered: an
        // indexed file lives outside Cedar Clerk, so no document can reference it on its own.
        group.MapGet("/{assetId:guid}/links", async (Guid projectId, Guid assetId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var documentIds = await ProjectLinks.LinkedIdsAsync(db, uid, LinkTargets.Asset, assetId, LinkTargets.Document);
            var documents = await db.Drafts
                .Where(d => documentIds.Contains(d.Id) && d.OwnerId == uid)
                .OrderBy(d => d.Title)
                .Select(d => new { d.Id, d.Title, d.DocumentType })
                .ToListAsync();

            return Results.Ok(documents);
        });

        group.MapPost("/{assetId:guid}/links/{draftId:guid}", async (
            Guid projectId, Guid assetId, Guid draftId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();
            if (!await db.AssetEntries.AnyAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid))
                return Results.NotFound();
            if (!await db.Drafts.AnyAsync(d => d.Id == draftId && d.OwnerId == uid)) return Results.NotFound();

            await ProjectLinks.AddAsync(db, uid, projectId, LinkTargets.Asset, assetId, LinkTargets.Document, draftId);
            return Results.NoContent();
        });

        group.MapDelete("/{assetId:guid}/links/{draftId:guid}", async (
            Guid projectId, Guid assetId, Guid draftId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var removed = await ProjectLinks.RemoveAsync(db, uid, LinkTargets.Asset, assetId, LinkTargets.Document, draftId);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/index", (Guid projectId, ClaimsPrincipal user, AssetIndexService scans, IConfiguration config) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var scan = scans.Current(projectId);
            // Owner check on the scan itself, not just the project: the dictionary is process-wide.
            if (scan is null || scan.OwnerId != uid)
                return Results.Ok(new { running = false, available = IndexingEnabled(config) });

            return Results.Ok(Describe(scan, IndexingEnabled(config)));
        });

        group.MapPost("/index", async (
            Guid projectId, IndexRequest? req, ClaimsPrincipal user, CedarDbContext db,
            AssetIndexService scans, IConfiguration config) =>
        {
            if (!IndexingEnabled(config))
                return Results.Json(new { error = ErrorMessages.AssetIndexingUnavailable }, statusCode: StatusCodes.Status403Forbidden);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            // A new path replaces the root and re-indexes; no path re-scans the existing one.
            var root = string.IsNullOrWhiteSpace(req?.Path) ? project.AssetRootPath : req.Path.Trim();
            if (string.IsNullOrWhiteSpace(root))
                return Results.Json(new { error = ErrorMessages.AssetFolderRequired }, statusCode: StatusCodes.Status400BadRequest);

            try
            {
                root = Path.GetFullPath(root);
            }
            catch (Exception)
            {
                return Results.Json(new { error = ErrorMessages.AssetFolderNotFound(root) }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!Directory.Exists(root))
                return Results.Json(new { error = ErrorMessages.AssetFolderNotFound(root) }, statusCode: StatusCodes.Status400BadRequest);

            if (!string.Equals(project.AssetRootPath, root, StringComparison.OrdinalIgnoreCase))
            {
                // Changing the root invalidates every relative path under the old one. Delete rather
                // than mark missing: these rows describe a folder the project is no longer about,
                // and leaving them would fill the index with permanent "not found" entries.
                await db.AssetEntries.Where(a => a.ProjectId == projectId).ExecuteDeleteAsync();
                project.AssetRootPath = root;
                await db.SaveChangesAsync();
            }

            var scan = scans.Start(projectId, uid, root);
            return Results.Ok(Describe(scan, true));
        });

        group.MapDelete("/index", (Guid projectId, ClaimsPrincipal user, AssetIndexService scans) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var scan = scans.Current(projectId);
            if (scan is null || scan.OwnerId != uid) return Results.NotFound();
            return scans.Cancel(projectId) ? Results.NoContent() : Results.NotFound();
        });
    }

    private static object Describe(AssetScan scan, bool available) => new
    {
        available,
        running = scan.Status is AssetScanStatus.Counting or AssetScanStatus.Indexing,
        status = scan.Status.ToString().ToLowerInvariant(),
        scan.RootPath,
        scan.Total,
        // The two passes can disagree by a file or two when the folder changes mid-scan, so the
        // client would otherwise be able to render 101%.
        processed = Math.Min(scan.Processed, scan.Total == 0 ? scan.Processed : scan.Total),
        scan.Indexed,
        scan.MarkedMissing,
        scan.Unreadable,
        scan.Error,
        scan.StartedAt,
        scan.FinishedAt,
    };
}
