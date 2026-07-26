using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Ai;
using CedarClerk.Server.Translation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftEndpoints
{
    public record SaveDraftRequest(string Title, string CedarJson);
    public record SaveTranslationRequest(string Title, string CedarJson);
    public record UpdateTagsRequest(string Tags);
    public record UpdateFolderRequest(Guid? FolderId);

    private const long CedarZipMaxBytes = 50 * 1024 * 1024;
    private const int CedarMaxAssetCount = 50;

    // A bulk Notion-shaped export routinely carries far more images than a personal .cedar
    // draft — a single large page can easily have 100+ (verified against a real 216-image
    // export, 17.07.2026) — so markdown import gets its own, more generous caps rather than
    // sharing the .cedar ones. See ADR-026, docs/DECISIONS.md.
    private const long MarkdownZipMaxBytes = 200 * 1024 * 1024;
    private const int MarkdownMaxImageCount = 300;

    private static readonly Dictionary<string, string> ImportImageExtensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
    };

    public static void MapDraftEndpoints(this WebApplication app)
    {
        var groupBuilder = app.MapGroup("/api/drafts").RequireAuthorization();
        
        groupBuilder.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var drafts = await db.Drafts.Where(d => d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new
                {
                    d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                    d.IsArchived, d.LastTelegramMessageId, d.LastTelegramUsername, d.FolderId,
                    Translations = db.DraftTranslations.Where(t => t.DraftId == d.Id)
                        .Select(t => new { t.Language, t.UpdatedAt }).ToList(),
                })
                .ToListAsync();

            // Most recent Pending-or-Failed schedule per draft — a /drafts screen "Scheduled"/
            // "Failed" badge only means something for a real, persisted ScheduledPost row (see
            // ADR-035: an immediate export failure isn't persisted anywhere, unlike this one).
            var draftIds = drafts.Select(d => d.Id).ToList();
            var scheduledRows = await db.ScheduledPosts
                .Where(s => draftIds.Contains(s.DraftId) && (s.Status == "Pending" || s.Status == "Failed"))
                .ToListAsync();
            var scheduled = scheduledRows
                .GroupBy(s => s.DraftId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ScheduledAtUtc).First());

            return drafts.Select(d => new
            {
                d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                d.IsArchived, d.LastTelegramMessageId, d.LastTelegramUsername, d.FolderId,
                Languages = d.Translations.Select(t => t.Language).ToList(),
                StaleLanguages = d.Translations.Where(t => t.UpdatedAt < d.UpdatedAt).Select(t => t.Language).ToList(),
                Scheduled = scheduled.TryGetValue(d.Id, out var s)
                    ? new { s.ScheduledAtUtc, s.ChatId, s.Status, s.Error }
                    : null,
            });
        });

        groupBuilder.MapPost("/{id:guid}/archive", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.IsArchived = true;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsArchived });
        });

        groupBuilder.MapPost("/{id:guid}/unarchive", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.IsArchived = false;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsArchived });
        });
        
        groupBuilder.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();


            var translations = await db.DraftTranslations.Where(t => t.DraftId == id)
                .Select(t => new { t.Language, t.Title, t.UpdatedAt })
                .ToListAsync();
            return Results.Ok(new { draft.Id, draft.Title, draft.CedarJson, draft.CreatedAt, draft.UpdatedAt, draft.BlogSlug, draft.IsBlogPublished, draft.BlogPublishedAt, draft.Tags, draft.FolderId, Translations = translations });
        });

        // Backs the export modal's "Files" list — every media asset referenced by this draft
        // (RU and, if it exists, its EN translation), with size and Telegram-compression status,
        // so Marty can see what's actually embedded before publishing large photos.
        groupBuilder.MapGet("/{id:guid}/assets", async (Guid id, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var names = CedarPackage.FindReferencedMediaPaths(draft.CedarJson).ToList();
            var translationJson = await db.DraftTranslations.Where(t => t.DraftId == id).Select(t => t.CedarJson).FirstOrDefaultAsync();
            if (translationJson is not null)
                names = names.Union(CedarPackage.FindReferencedMediaPaths(translationJson)).ToList();

            if (names.Count == 0)
                return Results.Ok(Array.Empty<object>());

            var assets = await db.Assets.Where(a => a.OwnerId == uid && names.Contains(a.LocalPath)).ToListAsync();
            return Results.Ok(assets.Select(a => new
            {
                a.Id,
                a.FileName,
                a.LocalPath,
                a.ContentType,
                a.SizeBytes,
                HasTelegramDerivative = a.TelegramLocalPath is not null,
                TelegramSizeBytes = a.TelegramLocalPath is not null && File.Exists(Path.Combine(media.Dir, a.TelegramLocalPath))
                    ? new FileInfo(Path.Combine(media.Dir, a.TelegramLocalPath)).Length
                    : (long?)null,
            }));
        });

        groupBuilder.MapPut("/{id:guid}/tags", async (Guid id, UpdateTagsRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.Tags = string.Join(",", req.Tags.Split(',')
                .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct());
            
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Tags });
        });
        
        // Backs the tag "cloud" picker (ADR-035) — usage counts across every one of the owner's
        // drafts. No separate Tag table exists; Draft.Tags is a flat comma-separated column, so
        // this aggregates in-memory rather than adding relational tag storage for a picker list.
        groupBuilder.MapGet("/tags", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var allTags = await db.Drafts.Where(d => d.OwnerId == uid && d.Tags != "")
                .Select(d => d.Tags)
                .ToListAsync();

            var counts = allTags
                .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(t => t)
                .Select(g => new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            return Results.Ok(counts);
        });

        // See the ADR following ADR-038, docs/DECISIONS.md — one folder per draft, folderId null
        // unassigns. FolderId itself is a plain scalar (no FK constraint), so ownership of the
        // target folder must be checked here explicitly.
        groupBuilder.MapPut("/{id:guid}/folder", async (Guid id, UpdateFolderRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            if (req.FolderId is { } folderId)
            {
                var ownsFolder = await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == uid);
                if (!ownsFolder) return Results.NotFound();
            }

            draft.FolderId = req.FolderId;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.FolderId });
        });

        groupBuilder.MapGet("/{id:guid}/translations/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            return translation is null
                ? Results.NotFound()
                : Results.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt, translation.SourceSnapshotJson });
        });
        
        groupBuilder.MapPut("/{id:guid}/translations/{lang}", async (Guid id, string lang, SaveTranslationRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (!Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            if (translation is null)
            {
                translation = new DraftTranslation { DraftId = id, Language = lang };
                db.DraftTranslations.Add(translation);
            }
            translation.Title = req.Title;
            translation.CedarJson = req.CedarJson;
            translation.UpdatedAt = DateTime.UtcNow;
            translation.SourceSnapshotJson = draft.CedarJson;
            await db.SaveChangesAsync();
            return Results.Ok(new { translation.Language, translation.UpdatedAt, translation.SourceSnapshotJson });
        });
        
        groupBuilder.MapPost("/{id:guid}/translations/{lang}/auto", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (!Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
            if (draft is null) return Results.NotFound();

            // AI features are Pro Plus; each call counts against the per-day AI quota
            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = "Auto-translate is a Pro Plus feature. Upgrade to use it." }, statusCode: StatusCodes.Status403Forbidden);
            
            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = $"Daily AI limit ({PlanLimitations.AiDailyLimit} calls) reached — resets at midnight UTC." }, statusCode: StatusCodes.Status429TooManyRequests);

            ITranslationProvider? provider;
            try
            {
                provider = TranslationProviderFactory.Create(cfg, httpFactory);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is null)
                return Results.Json(new { error = "Auto-translate is not configured" }, statusCode: StatusCodes.Status501NotImplemented);

            TranslationResult result;
            try
            {
                result = await provider.TranslateAsync(draft.Title, draft.CedarJson, lang, ct);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            
            try
            {
                using var docCheck = JsonDocument.Parse(result.CedarJson);
                var root = docCheck.RootElement;
                if (root.ValueKind != JsonValueKind.Object || 
                    !root.TryGetProperty("type", out var typeProp) || 
                    typeProp.GetString() != "doc")
                {
                    return Results.Json(new { error = "Translator returned an invalid document — try again" },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "Translator returned invalid JSON — try again" }, statusCode: StatusCodes.Status502BadGateway);
            }

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
            if (translation is null)
            {
                translation = new DraftTranslation { DraftId = id, Language = lang };
                db.DraftTranslations.Add(translation);
            }
            translation.Title = result.Title;
            translation.CedarJson = result.CedarJson;
            translation.UpdatedAt = DateTime.UtcNow;
            translation.SourceSnapshotJson = draft.CedarJson;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt, translation.SourceSnapshotJson });
        });

        groupBuilder.MapPost("/{id:guid}/ai-edit/{lang}/{kind}", async (Guid id, string lang, string kind, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (lang != Languages.Primary && !Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported language: {lang}" });

            AiEditKind editKind;
            switch (kind)
            {
                case "fix-errors": editKind = AiEditKind.FixErrors; break;
                case "schizo": editKind = AiEditKind.Schizo; break;
                default: return Results.BadRequest(new { error = $"Unknown AI edit kind: {kind}" });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
            if (draft is null) return Results.NotFound();

            // AI features are Pro Plus; each call counts against the per-day AI quota
            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = "AI editing is a Pro Plus feature. Upgrade to use it." }, statusCode: StatusCodes.Status403Forbidden);

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = $"Daily AI limit ({PlanLimitations.AiDailyLimit} calls) reached — resets at midnight UTC." }, statusCode: StatusCodes.Status429TooManyRequests);

            DraftTranslation? translation = null;
            string sourceTitle, sourceCedarJson;
            if (lang == Languages.Primary)
            {
                sourceTitle = draft.Title;
                sourceCedarJson = draft.CedarJson;
            }
            else
            {
                translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
                if (translation is null) return Results.NotFound(new { error = $"No {lang} version to edit yet" });
                sourceTitle = translation.Title;
                sourceCedarJson = translation.CedarJson;
            }

            IAiEditProvider? provider;
            try
            {
                provider = AiEditProviderFactory.Create(cfg, httpFactory);
            }
            catch (AiEditException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is null)
                return Results.Json(new { error = "AI editing is not configured" }, statusCode: StatusCodes.Status501NotImplemented);

            AiEditResult result;
            try
            {
                result = await provider.EditAsync(sourceTitle, sourceCedarJson, editKind, ct);
            }
            catch (AiEditException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }

            try
            {
                using var docCheck = JsonDocument.Parse(result.CedarJson);
                var root = docCheck.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "doc")
                {
                    return Results.Json(new { error = "AI returned an invalid document — try again" },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "AI returned invalid JSON — try again" }, statusCode: StatusCodes.Status502BadGateway);
            }

            if (translation is null)
            {
                draft.Title = result.Title;
                draft.CedarJson = result.CedarJson;
                draft.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                translation.Title = result.Title;
                translation.CedarJson = result.CedarJson;
                translation.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { title = result.Title, cedarJson = result.CedarJson, updatedAt = DateTime.UtcNow });
        });

        groupBuilder.MapDelete("/{id:guid}/translations/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var deleted = await db.DraftTranslations
                .Where(t => t.DraftId == id && t.Language == lang)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
        
        groupBuilder.MapPost("/", async (SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = new Draft { Title = req.Title, CedarJson = req.CedarJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        });
        
        groupBuilder.MapPut("/{id:guid}", async (Guid id, SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.Title = req.Title;
            draft.CedarJson = req.CedarJson;
            draft.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.UpdatedAt });
        });
        
        groupBuilder.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.Drafts
                .Where(x => x.Id == id && x.OwnerId == uid)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
        
        groupBuilder.MapGet("/{id:guid}/cedar", async (Guid id, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var mediaNames = CedarPackage.FindReferencedMediaPaths(draft.CedarJson);
            var assets = new List<CedarAsset>();
            foreach (var name in mediaNames)
            {
                var path = Path.Combine(media.Dir, name);
                if (!File.Exists(path)) continue; // asset was removed since; export what we still have
                assets.Add(new CedarAsset(name, await File.ReadAllBytesAsync(path)));
            }

            using var ms = new MemoryStream();
            CedarPackage.Write(ms, draft.CedarJson, new CedarPackageMeta(draft.Title, draft.CreatedAt), assets);

            var fileName = SanitizeFileName(draft.Title) + ".cedar";
            return Results.File(ms.ToArray(), "application/zip", fileName);
        });

        // A standalone, self-contained HTML page — the article body via the same
        // CedarToBlogHtmlRenderer the live blog uses, plus a minimal title/date header and the
        // author's signature. Deliberately doesn't reuse BlogEndpoints.PageShell/ShellTemplate:
        // those wire up comment/reaction fetch() calls against a live slug and a theme-toggle
        // button, none of which make sense for a file opened locally with no server behind it.
        groupBuilder.MapGet("/{id:guid}/export-html", async (Guid id, string? lang, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var language = lang is not null && Languages.IsTranslationLanguage(lang) ? lang : Languages.Primary;
            var title = draft.Title;
            var cedarJson = draft.CedarJson;
            if (language != Languages.Primary)
            {
                var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == language);
                if (translation is null)
                    return Results.BadRequest(new { error = $"No {language.ToUpperInvariant()} version of this draft" });
                title = translation.Title;
                cedarJson = translation.CedarJson;
            }

            var blogHost = cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;
            var body = CedarToBlogHtmlRenderer.Render(cedarJson, $"https://{blogHost}", language);
            var owner = await db.Users.Where(u => u.Id == uid)
                .Select(u => new { u.PostSignature, u.PostSignatureUrl, u.PlanTier, u.PlanExpiresAt })
                .FirstAsync();
            var ownerPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
            var signature = PlanLimitations.ResolveSignature(ownerPlan, owner.PostSignature, owner.PostSignatureUrl);
            var publishedAt = draft.BlogPublishedAt ?? draft.CreatedAt;

            var html = StaticExportHtml(title, body, language, signature, publishedAt, cedarJson);
            var fileName = SanitizeFileName(title) + ".html";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "text/html", fileName);
        });

        groupBuilder.MapPost("/import", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            if (file.Length == 0 || file.Length > CedarZipMaxBytes)
                return Results.BadRequest(new { error = $"File is too large ({CedarZipMaxBytes / (1024 * 1024)}MB maximum)" });

            CedarPackageContents pkg;
            await using (var stream = file.OpenReadStream())
            {
                try
                {
                    pkg = CedarPackage.Read(stream);
                }
                catch (CedarPackageException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }

            if (pkg.Assets.Count > CedarMaxAssetCount)
                return Results.BadRequest(new { error = $"Too many assets in package ({CedarMaxAssetCount} maximum)" });

            using (var docCheck = JsonDocument.Parse(pkg.DocumentJson))
            {
                var root = docCheck.RootElement;
                var looksLikeTiptapDoc = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "doc"
                    && root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array;
                if (!looksLikeTiptapDoc)
                    return Results.BadRequest(new { error = "Invalid document structure." });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);
            var incomingBytes = pkg.Assets.Sum(kv => (long)kv.Value.Length);
            if (!PlanLimitations.HasStorageRoom(tier, usedBytes, incomingBytes))
                return Results.Json(new { error = $"Storage limit of your plan ({PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)}MB) exceeded. Upgrade for more." }, statusCode: StatusCodes.Status403Forbidden);

            var pathRewrites = new Dictionary<string, string>();

            foreach (var (originalName, bytes) in pkg.Assets)
            {
                var contentType = ImageContentSniffer.DetectContentType(bytes);
                if (contentType is null || !ImportImageExtensions.TryGetValue(contentType, out var ext))
                    return Results.BadRequest(new { error = $"Unsupported or invalid asset: {originalName}" });
                if (bytes.Length > Consts.FileSizes.ImageMaxBytes)
                    return Results.BadRequest(new { error = $"Asset too large: {originalName}" });

                var newName = $"asset_{Guid.NewGuid()}{ext}";
                await File.WriteAllBytesAsync(Path.Combine(media.Dir, newName), bytes);

                db.Assets.Add(new Asset
                {
                    FileName = originalName,
                    ContentType = contentType,
                    SizeBytes = bytes.Length,
                    LocalPath = newName,
                    OwnerId = uid,
                });

                pathRewrites[originalName] = newName;
            }

            var rewrittenJson = CedarPackage.RewriteMediaPaths(pkg.DocumentJson, pathRewrites);
            var draft = new Draft { Title = pkg.Title, CedarJson = rewrittenJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();

            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        }).DisableAntiforgery();

        groupBuilder.MapPost("/import-markdown", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            if (file.Length == 0 || file.Length > MarkdownZipMaxBytes)
                return Results.BadRequest(new { error = $"File is too large ({MarkdownZipMaxBytes / (1024 * 1024)}MB maximum)" });

            // ZipArchive needs a seekable stream; IFormFile's underlying stream may not be.
            using var uploadCopy = new MemoryStream();
            await using (var uploadStream = file.OpenReadStream())
                await uploadStream.CopyToAsync(uploadCopy);
            uploadCopy.Position = 0;

            ZipArchive archive;
            try
            {
                archive = new ZipArchive(uploadCopy, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "The file is not a valid .zip archive." });
            }

            using (archive)
            {
                var mdEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                if (mdEntry is null)
                    return Results.BadRequest(new { error = "No .md file found inside the zip." });

                string markdownText;
                using (var mdStream = mdEntry.Open())
                using (var reader = new StreamReader(mdStream))
                    markdownText = await reader.ReadToEndAsync();

                var imageEntries = archive.Entries
                    .Where(e => e != mdEntry
                        && !e.FullName.EndsWith('/')
                        && ImageFileExtensions.Contains(Path.GetExtension(e.FullName))
                        && !e.FullName.Contains("..")
                        && !Path.IsPathRooted(e.FullName))
                    .ToList();

                if (imageEntries.Count > MarkdownMaxImageCount)
                    return Results.BadRequest(new { error = $"Too many images in the zip ({MarkdownMaxImageCount} maximum)" });

                var docJson = MarkdownToCedarConverter.Convert(markdownText, out var titleFromHeading);
                var referencedNames = CedarPackage.FindReferencedMediaPaths(docJson);

                // Matched by basename only — Notion's exact subfolder layout isn't preserved. If the
                // same filename appears under more than one subfolder (rare, but possible in a large
                // multi-page export), the first match wins rather than throwing on a duplicate key.
                var byBasename = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                var byBasenameCi = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in imageEntries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    byBasename.TryAdd(name, entry);
                    byBasenameCi.TryAdd(name, entry);
                }

                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
                var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);

                var unmatched = new List<string>();
                var pending = new List<(string OriginalName, byte[] Bytes, string ContentType, string Ext)>();
                long incomingBytes = 0;

                foreach (var refName in referencedNames)
                {
                    if (!byBasename.TryGetValue(refName, out var entry) && !byBasenameCi.TryGetValue(refName, out entry))
                    {
                        unmatched.Add(refName);
                        continue;
                    }

                    byte[] bytes;
                    using (var entryStream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        await entryStream.CopyToAsync(ms);
                        bytes = ms.ToArray();
                    }

                    var contentType = ImageContentSniffer.DetectContentType(bytes);
                    if (contentType is null || !ImportImageExtensions.TryGetValue(contentType, out var ext) || bytes.Length > Consts.FileSizes.ImageMaxBytes)
                    {
                        unmatched.Add(refName);
                        continue;
                    }

                    incomingBytes += bytes.Length;
                    pending.Add((refName, bytes, contentType, ext));
                }

                if (!PlanLimitations.HasStorageRoom(tier, usedBytes, incomingBytes))
                    return Results.Json(new { error = $"Storage limit of your plan ({PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)}MB) exceeded. Upgrade for more." }, statusCode: StatusCodes.Status403Forbidden);

                var pathRewrites = new Dictionary<string, string>();
                foreach (var (originalName, bytes, contentType, ext) in pending)
                {
                    var newName = $"asset_{Guid.NewGuid()}{ext}";
                    await File.WriteAllBytesAsync(Path.Combine(media.Dir, newName), bytes);

                    db.Assets.Add(new Asset
                    {
                        FileName = originalName,
                        ContentType = contentType,
                        SizeBytes = bytes.Length,
                        LocalPath = newName,
                        OwnerId = uid,
                    });

                    pathRewrites[originalName] = newName;
                }

                var rewrittenJson = CedarPackage.RewriteMediaPaths(docJson, pathRewrites);
                var title = titleFromHeading ?? Path.GetFileNameWithoutExtension(mdEntry.Name);
                var draft = new Draft { Title = title, CedarJson = rewrittenJson, OwnerId = uid };
                db.Drafts.Add(draft);
                await db.SaveChangesAsync();

                return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id, unmatchedImages = unmatched });
            }
        }).DisableAntiforgery()
          .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MarkdownZipMaxBytes });
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "draft" : sanitized;
    }

    // Trimmed CSS subset of BlogEndpoints.ShellTemplate — just enough to render every block type
    // CedarToBlogHtmlRenderer can emit (headings, lists, tables, blockquote, code, collage,
    // carousel, spoiler, math, footnotes, TOC) plus a minimal title/date/signature header.
    // Duplicated rather than shared (docs/DESIGN.md already notes CSS is duplicated per-component
    // in this codebase, not centralized) because this needs to be fully self-contained in one
    // file with no external <link>/fetch of any kind.
    private static string StaticExportHtml(string title, string bodyHtml, string lang, ResolvedSignature? signature, DateTime publishedAt, string cedarJson)
    {
        var mathAssets = bodyHtml.Contains("math-tex")
            ? """<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css"><script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js" onload="document.querySelectorAll('.math-tex').forEach(function (el) { try { katex.render(el.textContent, el, { displayMode: el.dataset.display === 'true', throwOnError: false }); } catch (e) {} });"></script>"""
            : "";
        var signatureBlock = BlogEndpoints.SignatureHtml(signature, "div");
        // Same rule as BlogEndpoints.RenderPostAsync: skip the separate <h1> if the document's
        // own first block is already a heading, to avoid showing the title twice.
        var titleHeading = HeadingOutline.StartsWithHeading(cedarJson)
            ? ""
            : $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>";

        return $$"""
            <!doctype html>
            <html lang="{{lang}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
            <style>
            :root {
                color-scheme: light dark;
                --bg: #ECE9E2; --sheet: #FCFBF8; --alt: #EFECE4; --border: #DBD5C8;
                --text: #26231D; --t2: #6B655A; --t3: #9F988A; --accent: #5B6E46;
                --font-sans: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                --font-mono: ui-monospace, Menlo, Consolas, monospace;
                --asoft: color-mix(in srgb, var(--accent) 13%, var(--sheet));
                --abord: color-mix(in srgb, var(--accent) 38%, var(--border));
            }
            @media (prefers-color-scheme: dark) {
                :root {
                    --bg: #171511; --sheet: #211E18; --alt: #2F2C23; --border: #3C382D;
                    --text: #EAE6DB; --t2: #A69F8F; --t3: #776F5F;
                    --accent: color-mix(in srgb, #5B6E46 55%, #E8F0E8 45%);
                }
            }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text); font-family: var(--font-sans); line-height: 1.6; }
            a { color: var(--accent); }
            img, video { max-width: 100%; height: auto; }
            .page { max-width: 720px; margin: 0 auto; padding: 40px 20px 60px; }
            .post-sheet { background: var(--sheet); border-radius: 12px; box-shadow: 0 1px 3px rgba(40,35,25,.10); padding: 32px 40px 28px; }
            .post-sheet h1 { font-size: 27px; font-weight: 700; letter-spacing: -.015em; line-height: 1.22; margin: 0 0 6px; text-align: center; }
            .post-meta { font-size: 12px; color: var(--t3); text-align: center; margin: 0 0 22px; }
            .post-sheet h2 { font-size: 20px; font-weight: 600; letter-spacing: -.01em; margin: 24px 0 8px; }
            .post-sheet p { font-size: 16px; line-height: 1.65; margin: 0 0 14px; }
            .toc { background: var(--asoft); border: 1px solid var(--abord); border-radius: 10px; padding: 14px 18px; margin: 0 0 18px; }
            .toc-title { font-size: 11px; font-weight: 700; letter-spacing: .05em; text-transform: uppercase; color: var(--accent); margin: 0 0 8px; }
            .toc ul { list-style: none; margin: 0; padding: 0; font-size: 14px; line-height: 1.8; }
            .toc li a { color: var(--text); }
            .toc .toc-lvl-2 { padding-left: 14px; } .toc .toc-lvl-3 { padding-left: 28px; }
            .toc .toc-lvl-4 { padding-left: 42px; } .toc .toc-lvl-5 { padding-left: 56px; } .toc .toc-lvl-6 { padding-left: 70px; }
            .spoiler { background: var(--t3); color: transparent; border-radius: 4px; padding: 0 5px; cursor: pointer; }
            .spoiler:hover, .spoiler:focus { background: var(--alt); color: inherit; }
            .post-sheet code { font-family: var(--font-mono); font-size: .85em; background: var(--alt); border-radius: 4px; padding: 1px 6px; }
            .post-sheet pre { background: #22201A; color: #C9C08C; border-radius: 8px; padding: 12px 14px; overflow-x: auto; }
            .post-sheet pre code { background: none; padding: 0; font-size: 13.5px; line-height: 1.55; }
            .post-sheet blockquote { border-left: 3px solid var(--abord); padding: 2px 0 2px 14px; color: var(--t2); margin: 0 0 16px; }
            .post-sheet hr { border: none; border-top: 1px solid var(--border); margin: 24px 0; }
            .post-sheet ul, .post-sheet ol { font-size: 16px; line-height: 1.7; padding-left: 20px; margin: 0 0 16px; }
            .post-sheet figure { margin: 0 0 16px; }
            .post-sheet figcaption { text-align: center; font-size: 13px; color: var(--t2); margin-top: 6px; }
            .post-sheet table { width: 100%; border-collapse: collapse; font-size: 14.5px; margin: 0 0 16px; overflow-x: auto; display: block; }
            .post-sheet th, .post-sheet td { border: 1px solid var(--border); padding: 7px 11px; text-align: left; vertical-align: top; }
            .post-sheet th { background: var(--alt); font-weight: 600; }
            .math-tex { margin: 16px 0; overflow-x: auto; }
            div.math-tex { text-align: center; }
            .collage { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 6px; }
            .collage img { width: 100%; height: 160px; object-fit: cover; border-radius: 6px; }
            .carousel { position: relative; margin: 16px 0; }
            .carousel-viewport img { width: 100%; display: block; border-radius: 6px; }
            .carousel-prev, .carousel-next { position: absolute; top: 50%; transform: translateY(-50%); background: rgba(0,0,0,0.5); color: #fff; border: none; width: 32px; height: 32px; border-radius: 50%; cursor: pointer; font-size: 18px; }
            .carousel-prev { left: 8px; } .carousel-next { right: 8px; }
            .carousel-dots { display: flex; justify-content: center; gap: 6px; margin-top: 8px; }
            .carousel-dot { width: 8px; height: 8px; border-radius: 50%; border: none; background: rgba(128,128,128,0.4); cursor: pointer; padding: 0; }
            .carousel-dot.active { background: var(--accent); }
            .footnotes { font-size: 12.5px; color: var(--t2); border-top: 1px solid var(--border); padding: 10px 0 0; margin: 0 0 4px; }
            .footnotes sup, .post-sheet sup { color: var(--accent); font-weight: 600; }
            .post-signature { font-size: 13.5px; font-style: italic; color: var(--t2); white-space: pre-line; border-top: 1px solid var(--border); padding-top: 14px; margin-top: 18px; }
            .made-with { text-align: center; font-size: 11.5px; color: var(--t3); margin-top: 18px; }
            {{mathAssets}}
            </style>
            </head>
            <body>
            <div class="page">
            <div class="post-sheet">
            {{titleHeading}}
            <div class="post-meta">{{publishedAt.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}}</div>
            {{bodyHtml}}
            {{signatureBlock}}
            </div>
            <div class="made-with">Made with Cedar Clerk</div>
            </div>
            <script>
            document.querySelectorAll('.carousel').forEach(function (car) {
                var imgs = car.querySelectorAll('.carousel-viewport img');
                var dots = car.querySelectorAll('.carousel-dot');
                var i = 0;
                function show(n) {
                    i = (n + imgs.length) % imgs.length;
                    imgs.forEach(function (img, idx) { img.style.display = idx === i ? '' : 'none'; });
                    dots.forEach(function (d, idx) { d.classList.toggle('active', idx === i); });
                }
                var prev = car.querySelector('.carousel-prev');
                var next = car.querySelector('.carousel-next');
                if (prev) prev.addEventListener('click', function () { show(i - 1); });
                if (next) next.addEventListener('click', function () { show(i + 1); });
                dots.forEach(function (d, idx) { d.addEventListener('click', function () { show(idx); }); });
                if (imgs.length) show(0);
            });
            </script>
            </body>
            </html>
            """;
    }
}