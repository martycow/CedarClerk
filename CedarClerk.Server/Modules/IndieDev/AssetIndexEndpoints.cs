using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// T-122 (ADR-107) — the asset index: paths and metadata, never bytes.
//
// ## The server no longer walks anything (ADR-117)
//
// Until 12.08.2026 these endpoints made the SERVER walk the SERVER's disk on a tenant's say-so, which
// is the feature on a laptop and a filename disclosure on a shared host — so it sat behind
// `Cedar:AssetIndex:Enabled`, which only the desktop shell set.
//
// That flag is gone, and so is the walk. The desktop agent reads the disk and the page pushes what it
// found: `PUT source` declares the folder and the machine, `POST batch` delivers pages of files,
// `POST sweep` closes the scan. The capability is therefore **absent** from a hosted install rather
// than switched off in one, which is a stronger statement than the flag ever made — there is no
// configuration of production that can enumerate its own filesystem, because that code is not here.
//
// What arrives instead is a list of names from the account that owns them, which is ordinary data
// entry. Ordinary, but unbounded if left alone, hence the caps below — every one a number rather than
// a vague intention.
//
// NOTE the name: the server root already has an `AssetEndpoints.MapAssetEndpoints` for uploaded post
// media, and two extension methods with one name on WebApplication is an ambiguity waiting to resolve
// the wrong way. These are two different things (ADR-107) and now they read as two.
public static class AssetIndexEndpoints
{
    /// <summary>Declares where a project's assets live, and on which machine.</summary>
    public record SourceRequest(string? MachineId, string? MachineName, string? RootPath);

    /// <summary>One file as the agent described it. Mirrors <c>ScannedFile</c> across the wire.</summary>
    public record FileRecord(
        string? RelativePath, string? FileName, string? Extension, string? Kind,
        long SizeBytes, DateTime ModifiedAt,
        int? Width, int? Height, int? DurationMs, int? SampleRate);

    public record BatchRequest(List<FileRecord>? Files);

    public record SweepRequest(DateTime? ScanStartedAt, int Unreadable);

    // Caps. Each one exists because the endpoint is a write channel into the cloud that a client
    // fills, and "the client is our own page" is not a bound — a defect in that page is.
    private const int MaxBatchRows = 500;
    private const int MaxRowsPerProject = 200_000;
    private const int MaxRelativePathLength = 1024;
    private const int MaxThumbnailBytes = 256 * 1024;
    private const int MaxThumbnailsPerRequest = 20;

    /// <summary>
    /// The extensions a thumbnail can exist for, as a set the list query can translate to SQL.
    /// <c>AssetKinds.CanPreview</c> takes a path and so cannot be called inside a LINQ-to-SQLite
    /// projection; this is the same answer in a form EF can send to the database.
    /// </summary>
    private static readonly string[] PreviewableExtensions =
        ["png", "jpg", "jpeg", "gif", "bmp", "webp", "tga", "tif", "tiff", "pbm", "qoi", "blend", "blend1", "blend2"];

    public static void MapAssetIndexEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/assets").RequireAuthorization();

        group.MapGet("/", async (
            Guid projectId, ClaimsPrincipal user, CedarDbContext db,
            string? kind = null, string? search = null, bool missing = false,
            string sort = "path", string direction = "asc", int skip = 0, int take = 60) =>
        {
            var validationError = ValidateCollectionQuery(kind, sort, direction);
            if (validationError is not null)
                return Results.BadRequest(new { error = validationError });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            var query = db.AssetEntries.Where(a => a.ProjectId == projectId && a.OwnerId == uid);
            if (missing) query = query.Where(a => a.MissingSince != null);
            if (kind is not null) query = query.Where(a => a.Kind == kind);
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
            // take is clamped, not trusted: the grid pages, and an unbounded page over a hundred
            // thousand rows is a request that never returns.
            //
            // Materialised before projecting, because Project() is a method and EF cannot translate
            // one into SQL. At most 200 rows of small columns, so the cost of loading whole entities
            // is real but tiny — and it buys one definition of `hasThumbnail` instead of two.
            var ordered = OrderForIndex(query, sort, direction);
            var rows = await ordered
                .Skip(Math.Max(0, skip))
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync();
            var page = rows.Select(Project).ToList();

            return Results.Ok(new
            {
                rootPath = project.AssetRootPath,
                // ADR-117 — the client compares this against its own machine to decide whether it is
                // looking at files or at fingerprints of files. Sent on the page rather than per row
                // because there is one root per project, so the answer cannot differ row to row.
                sourceMachine = project.AssetRootMachineId is null
                    ? null
                    : new { id = project.AssetRootMachineId, name = project.AssetRootMachineName },
                indexedAt = project.AssetsIndexedAt,
                total,
                totalIndexed = await all.CountAsync(),
                missingCount = await all.CountAsync(a => a.MissingSince != null),
                // What the preview pass still owes, so the screen can say "previews for 2000 of 8431"
                // instead of leaving a grid of placeholders unexplained.
                thumbnailsPending = await all.CountAsync(a =>
                    a.MissingSince == null
                    && PreviewableExtensions.Contains(a.Extension)
                    && a.ThumbnailForModifiedAt != a.ModifiedAt),
                byKind,
                items = page,
            });
        });

        group.MapGet("/{assetId:guid}", async (Guid projectId, Guid assetId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            var row = await db.AssetEntries
                .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid);
            if (row is null) return Results.NotFound();
            var asset = Project(row);

            return Results.Ok(new
            {
                asset.Id, asset.RelativePath, asset.FileName, asset.Extension, asset.Kind,
                asset.SizeBytes, asset.ModifiedAt, asset.IndexedAt, asset.MissingSince,
                asset.Width, asset.Height, asset.DurationMs, asset.SampleRate,
                asset.HasThumbnail, asset.CanHaveThumbnail,
                sourceMachine = project.AssetRootMachineId is null
                    ? null
                    : new { id = project.AssetRootMachineId, name = project.AssetRootMachineName },
                // The absolute path, for "Reveal in file manager". Only meaningful on the machine that
                // did the indexing — which is why the client is told which machine that was, rather
                // than being left to assume it is this one.
                fullPath = project.AssetRootPath is null
                    ? null
                    : Path.Combine(project.AssetRootPath, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            });
        });

        #region Import (ADR-117)

        // Declares the folder and the machine, and hands back the instant the scan is considered to
        // have started. That instant is the whole mechanism behind sweeping: rows not touched by a
        // batch after it are the rows the walk did not find.
        group.MapPut("/source", async (
            Guid projectId, SourceRequest? req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(req?.RootPath))
                return Results.Json(new { error = ErrorMessages.AssetFolderRequired }, statusCode: StatusCodes.Status400BadRequest);
            if (string.IsNullOrWhiteSpace(req.MachineId))
                return Results.Json(new { error = ErrorMessages.AssetMachineRequired }, statusCode: StatusCodes.Status400BadRequest);

            var root = req.RootPath.Trim();
            // A different folder — or the same folder on a different machine — invalidates every
            // relative path under the old one. Deleted rather than marked missing: these rows describe
            // a folder this project is no longer about, and keeping them would fill the index with
            // permanent "not found" entries. The machine matters as much as the path here, because
            // `D:\Projects\Game` on a laptop and on a desktop are different folders wearing one name.
            var moved = !string.Equals(project.AssetRootPath, root, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.AssetRootMachineId, req.MachineId, StringComparison.Ordinal);

            if (moved)
            {
                await db.AssetEntries.Where(a => a.ProjectId == projectId).ExecuteDeleteAsync();
                project.AssetRootPath = root;
                project.AssetRootMachineId = req.MachineId;
                project.AssetRootMachineName = Truncate(req.MachineName, 200);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new { scanStartedAt = DateTime.UtcNow, replaced = moved });
        });

        group.MapPost("/batch", async (
            Guid projectId, BatchRequest? req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var files = req?.Files ?? [];
            if (files.Count == 0) return Results.Ok(new { accepted = 0, added = 0, ids = new Dictionary<string, Guid>() });
            if (files.Count > MaxBatchRows)
                return Results.Json(new { error = ErrorMessages.AssetBatchTooLarge(MaxBatchRows) },
                    statusCode: StatusCodes.Status400BadRequest);

            // Rejected loudly rather than truncated: a client that silently loses the tail of its
            // index would leave a project that looks fully scanned and is not.
            var existingCount = await db.AssetEntries.CountAsync(a => a.ProjectId == projectId);
            if (existingCount + files.Count > MaxRowsPerProject)
                return Results.Json(new { error = ErrorMessages.AssetIndexFull(MaxRowsPerProject) },
                    statusCode: StatusCodes.Status400BadRequest);

            var paths = new List<string>(files.Count);
            foreach (var file in files)
            {
                if (SanitiseRelativePath(file.RelativePath) is not { } relative)
                    return Results.Json(new { error = ErrorMessages.AssetPathRejected(file.RelativePath ?? "") },
                        statusCode: StatusCodes.Status400BadRequest);
                paths.Add(relative);
            }

            // One query for the whole batch rather than one per file: five hundred round trips to
            // SQLite per page is the difference between a scan of seconds and one of minutes.
            var existing = await db.AssetEntries
                .Where(a => a.ProjectId == projectId && paths.Contains(a.RelativePath))
                .ToDictionaryAsync(a => a.RelativePath);

            var now = DateTime.UtcNow;
            var ids = new Dictionary<string, Guid>(files.Count);
            var added = 0;

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var relative = paths[i];

                existing.TryGetValue(relative, out var row);
                var applied = ApplyRecord(file, relative, row, uid, projectId, now);
                if (row is null)
                {
                    db.AssetEntries.Add(applied);
                    added++;
                }
                ids[relative] = applied.Id;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { accepted = files.Count, added, ids });
        });

        // Closes a scan: whatever the walk did not touch is marked, and the project's timestamp moves.
        group.MapPost("/sweep", async (
            Guid projectId, SweepRequest? req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();
            if (req?.ScanStartedAt is not { } startedAt)
                return Results.Json(new { error = ErrorMessages.AssetScanStampRequired }, statusCode: StatusCodes.Status400BadRequest);

            // MARKED, never deleted (ADR-107): an unplugged external drive must read as "not found at
            // path", not as "these files never existed".
            var now = DateTime.UtcNow;
            var marked = await db.AssetEntries
                .Where(a => a.ProjectId == projectId && a.OwnerId == uid
                            && a.MissingSince == null && a.IndexedAt < startedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MissingSince, now));

            project.AssetsIndexedAt = now;
            await db.SaveChangesAsync();

            return Results.Ok(new { markedMissing = marked, indexedAt = now, unreadable = req.Unreadable });
        });

        // What the preview pass still owes. Driving the pass from the server's own answer is what
        // makes it resumable and idempotent: interrupt it, run it again next week, and it picks up the
        // remainder instead of redoing the lot.
        group.MapGet("/thumbs/pending", async (Guid projectId, ClaimsPrincipal user, CedarDbContext db, int take = 200, int skip = 0) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var pending = await db.AssetEntries
                .Where(a => a.ProjectId == projectId && a.OwnerId == uid
                            && a.MissingSince == null
                            && PreviewableExtensions.Contains(a.Extension)
                            && a.ThumbnailForModifiedAt != a.ModifiedAt)
                .OrderByDescending(a => a.ModifiedAt).ThenBy(a => a.Id)
                // A file no pass can render stays pending for good, so the caller steps past the ones
                // it has already tried: 200 unreadable .blend backups at the head must not end the pass.
                .Skip(Math.Max(skip, 0))
                .Take(Math.Clamp(take, 1, 500))
                .Select(a => new { a.Id, a.RelativePath })
                .ToListAsync();

            var remaining = await db.AssetEntries.CountAsync(a =>
                a.ProjectId == projectId && a.OwnerId == uid
                && a.MissingSince == null
                && PreviewableExtensions.Contains(a.Extension)
                && a.ThumbnailForModifiedAt != a.ModifiedAt);

            // Newest first, because that is what the author is working on — if the pass is interrupted
            // or the budget runs out, the previews that exist are the ones most likely to be wanted.
            return Results.Ok(new { items = pending, remaining });
        });

        group.MapPut("/thumbs", async (
            Guid projectId, HttpRequest request, ClaimsPrincipal user, CedarDbContext db,
            ThumbnailPaths thumbs, IConfiguration config) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();
            if (!request.HasFormContentType) return Results.BadRequest(new { error = ErrorMessages.AssetThumbFormRequired });

            var form = await request.ReadFormAsync();
            if (form.Files.Count == 0) return Results.Ok(new { stored = 0, rejected = 0 });
            if (form.Files.Count > MaxThumbnailsPerRequest)
                return Results.Json(new { error = ErrorMessages.AssetThumbBatchTooLarge(MaxThumbnailsPerRequest) },
                    statusCode: StatusCodes.Status400BadRequest);

            // The system's ceiling, not the author's (ADR-117, Decision 5). Checked before writing
            // rather than after, and named out loud in the refusal: silently filling a 48 GB disk
            // would be denial of service dressed as generosity.
            var budget = config.GetValue<long?>(Consts.General.ThumbBudgetCfg) ?? Consts.General.ThumbBudgetDefaultBytes;
            if (ThumbnailUsage(thumbs) >= budget)
                return Results.Json(new { error = ErrorMessages.AssetThumbBudgetExhausted(budget) },
                    statusCode: StatusCodes.Status507InsufficientStorage);

            var stored = 0;
            var rejected = 0;
            foreach (var part in form.Files)
            {
                if (!Guid.TryParse(part.Name, out var assetId)) { rejected++; continue; }
                if (part.Length is 0 or > MaxThumbnailBytes) { rejected++; continue; }

                var asset = await db.AssetEntries
                    .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid);
                if (asset is null) { rejected++; continue; }

                using var buffer = new MemoryStream();
                await part.CopyToAsync(buffer);
                var bytes = buffer.ToArray();
                // Really a JPEG, or this endpoint is a way to store arbitrary bytes under a name the
                // grid then serves back as an image.
                if (!AssetMetadata.LooksLikeJpeg(bytes)) { rejected++; continue; }

                AssetMetadata.SaveThumbnail(thumbs.For(asset.Id), bytes);
                asset.ThumbnailForModifiedAt = asset.ModifiedAt;
                stored++;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { stored, rejected });
        });

        #endregion

        // T-140 — the thumbnail. Behind the same authorisation as everything else and NOT under
        // /media/*: an indexed file is somebody's unreleased game art, while the public media path is
        // for what an author chose to publish.
        //
        // **Serves only what was uploaded (ADR-117).** The generate-on-demand branch is gone because
        // this process cannot see the file — it is on the author's machine, which is the entire point.
        group.MapGet("/{assetId:guid}/thumb", async (
            Guid projectId, Guid assetId, ClaimsPrincipal user, CedarDbContext db, ThumbnailPaths thumbs) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var asset = await db.AssetEntries
                .FirstOrDefaultAsync(a => a.Id == assetId && a.ProjectId == projectId && a.OwnerId == uid);
            if (asset?.ThumbnailForModifiedAt is null || asset.ThumbnailForModifiedAt != asset.ModifiedAt)
                return Results.NotFound();

            var cached = thumbs.For(asset.Id);
            if (!File.Exists(cached)) return Results.NotFound();
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
    }

    public static string? ValidateCollectionQuery(string? kind, string sort, string direction)
    {
        if (kind is not null && !AssetKinds.IsKnown(kind))
            return ErrorMessages.UnknownAssetKindFilter;
        if (sort is not ("path" or "type" or "size" or "modified" or "status"))
            return ErrorMessages.UnknownAssetSortKey;
        if (direction is not ("asc" or "desc"))
            return ErrorMessages.UnknownAssetSortDirection;
        return null;
    }

    public static IOrderedQueryable<AssetEntry> OrderForIndex(
        IQueryable<AssetEntry> query, string sort, string direction) => (sort, direction) switch
    {
        ("path", "asc") => query.OrderBy(a => a.RelativePath).ThenBy(a => a.Id),
        ("path", "desc") => query.OrderByDescending(a => a.RelativePath).ThenBy(a => a.Id),
        ("type", "asc") => query.OrderBy(a => a.Kind).ThenBy(a => a.RelativePath).ThenBy(a => a.Id),
        ("type", "desc") => query.OrderByDescending(a => a.Kind).ThenBy(a => a.RelativePath).ThenBy(a => a.Id),
        ("size", "asc") => query.OrderBy(a => a.SizeBytes).ThenBy(a => a.Id),
        ("size", "desc") => query.OrderByDescending(a => a.SizeBytes).ThenBy(a => a.Id),
        ("modified", "asc") => query.OrderBy(a => a.ModifiedAt).ThenBy(a => a.Id),
        ("modified", "desc") => query.OrderByDescending(a => a.ModifiedAt).ThenBy(a => a.Id),
        ("status", "asc") => query.OrderBy(a => a.MissingSince != null).ThenBy(a => a.Id),
        _ => query.OrderByDescending(a => a.MissingSince != null).ThenBy(a => a.Id),
    };

    /// <summary>
    /// The row as every screen wants it. One projection rather than two hand-kept copies: the list
    /// and the detail view disagreeing about what <c>hasThumbnail</c> means is exactly how a grid ends
    /// up showing broken-image icons.
    /// </summary>
    private static AssetView Project(AssetEntry a) => new()
    {
        Id = a.Id,
        RelativePath = a.RelativePath,
        FileName = a.FileName,
        Extension = a.Extension,
        Kind = a.Kind,
        SizeBytes = a.SizeBytes,
        ModifiedAt = a.ModifiedAt,
        IndexedAt = a.IndexedAt,
        MissingSince = a.MissingSince,
        Width = a.Width,
        Height = a.Height,
        DurationMs = a.DurationMs,
        SampleRate = a.SampleRate,
        // "A preview is here now" — the grid points an <img> at this and must never get a 404, since
        // that renders as a broken-image icon.
        HasThumbnail = a.MissingSince == null && a.ThumbnailForModifiedAt == a.ModifiedAt,
        // "A preview is possible, but has not arrived yet." A different fact, and the screen needs
        // both: a placeholder that says "not uploaded" is honest, while one on a PSD that can never
        // have a preview is a promise nothing will keep.
        CanHaveThumbnail = PreviewableExtensions.Contains(a.Extension),
    };

    public class AssetView
    {
        public Guid Id { get; init; }
        public string RelativePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public string Extension { get; init; } = "";
        public string Kind { get; init; } = "";
        public long SizeBytes { get; init; }
        public DateTime ModifiedAt { get; init; }
        public DateTime IndexedAt { get; init; }
        public DateTime? MissingSince { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public int? DurationMs { get; init; }
        public int? SampleRate { get; init; }
        public bool HasThumbnail { get; init; }
        public bool CanHaveThumbnail { get; init; }
    }

    /// <summary>
    /// Folds one described file into a row — either updating the row that is there or building a new
    /// one. Returns the row either way; the caller adds it to the context when there was none.
    ///
    /// Its own function because this is where every judgement about a re-scan lives, and each one is
    /// only obvious once stated:
    /// <list type="bullet">
    /// <item>a file found again stops being missing, whatever it was missing from;</item>
    /// <item>a preview survives a re-scan that did not change the file — otherwise every re-index
    /// would re-upload every preview, which is precisely the cost the resumable pass exists to avoid;</item>
    /// <item>metadata is stamped even when the header said nothing, or that file is reopened forever.</item>
    /// </list>
    /// </summary>
    public static AssetEntry ApplyRecord(
        FileRecord file, string relativePath, AssetEntry? existing, string ownerId, Guid projectId, DateTime now)
    {
        var row = existing ?? new AssetEntry
        {
            OwnerId = ownerId,
            ProjectId = projectId,
            RelativePath = relativePath,
        };

        // Compared before the new values overwrite them. "Changed" means the bytes are different, which
        // is the only thing that invalidates a preview — a re-scan that finds the same file must not.
        var changed = existing is not null
            && (existing.ModifiedAt != file.ModifiedAt || existing.SizeBytes != file.SizeBytes);

        row.FileName = Truncate(file.FileName, 400) ?? Path.GetFileName(relativePath);
        row.Extension = Truncate(file.Extension, 32)?.ToLowerInvariant() ?? "";
        row.Kind = AssetKinds.IsKnown(file.Kind ?? "") ? file.Kind! : AssetKinds.Other;
        row.SizeBytes = Math.Max(0, file.SizeBytes);
        row.ModifiedAt = file.ModifiedAt;
        row.IndexedAt = now;
        row.MissingSince = null;
        row.Width = file.Width;
        row.Height = file.Height;
        row.DurationMs = file.DurationMs;
        row.SampleRate = file.SampleRate;
        // Stamped whether or not anything was learned: a file whose header says nothing must not be
        // reopened on every single scan for the rest of its life.
        row.MetadataForModifiedAt = file.ModifiedAt;
        if (changed) row.ThumbnailForModifiedAt = null;

        return row;
    }

    /// <summary>
    /// A relative path we are willing to store, or null. The agent is trusted to describe a folder
    /// honestly; the endpoint is not entitled to assume the agent is what called it.
    ///
    /// Rejected: absolute paths, drive prefixes, backslashes and any `..` segment. None of these can
    /// escape anywhere on their own — the cloud never opens these files — but they all reach
    /// <c>Path.Combine</c> in the `fullPath` the detail view hands back to a desktop client, which
    /// does.
    /// </summary>
    public static string? SanitiseRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var value = relativePath.Trim();
        if (value.Length > MaxRelativePathLength) return null;
        if (value.Contains('\\') || value.Contains(':')) return null;
        if (value.StartsWith('/')) return null;
        if (value.Split('/').Any(segment => segment is ".." or "." || segment.Length == 0)) return null;
        return value;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    /// <summary>
    /// Bytes currently held in the thumbnail directory. Measured rather than tracked in a column:
    /// a counter would drift from the truth the first time a file was removed by hand or a write
    /// failed halfway, and this directory holds thousands of small files, not millions.
    /// </summary>
    private static long ThumbnailUsage(ThumbnailPaths thumbs)
    {
        try
        {
            var dir = new DirectoryInfo(thumbs.Dir);
            return dir.Exists ? dir.EnumerateFiles("*.jpg").Sum(f => f.Length) : 0;
        }
        catch (Exception)
        {
            // An unreadable thumbnail directory must not be the reason an upload fails; the per-file
            // and per-request caps still bound what this endpoint can write.
            return 0;
        }
    }
}
