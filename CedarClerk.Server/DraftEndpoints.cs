using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftEndpoints
{
    public record SaveDraftRequest(string Title, string CedarJson);
    public record SaveTranslationRequest(string Title, string CedarJson);
    public record UpdateTagsRequest(string Tags);

    private const long CedarZipMaxBytes = 50 * 1024 * 1024;
    private const int CedarMaxAssetCount = 50;

    private static readonly Dictionary<string, string> ImportImageExtensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    public static void MapDraftEndpoints(this WebApplication app)
    {
        var groupBuilder = app.MapGroup("/api/drafts").RequireAuthorization();
        
        groupBuilder.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await db.Drafts.Where(d => d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new
                {
                    d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                    Languages = db.DraftTranslations.Where(t => t.DraftId == d.Id).Select(t => t.Language).ToList(),
                })
                .ToListAsync();
        });
        
        groupBuilder.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();


            var translations = await db.DraftTranslations.Where(t => t.DraftId == id)
                .Select(t => new { t.Language, t.Title, t.UpdatedAt })
                .ToListAsync();
            return Results.Ok(new { draft.Id, draft.Title, draft.CedarJson, draft.CreatedAt, draft.UpdatedAt, draft.BlogSlug, draft.IsBlogPublished, draft.BlogPublishedAt, draft.Tags, Translations = translations });
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
        
        groupBuilder.MapGet("/{id:guid}/translations/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            return translation is null
                ? Results.NotFound()
                : Results.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt });
        });
        
        groupBuilder.MapPut("/{id:guid}/translations/{lang}", async (Guid id, string lang, SaveTranslationRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (!Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            if (translation is null)
            {
                translation = new DraftTranslation { DraftId = id, Language = lang };
                db.DraftTranslations.Add(translation);
            }
            translation.Title = req.Title;
            translation.CedarJson = req.CedarJson;
            translation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { translation.Language, translation.UpdatedAt });
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
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt });
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
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "draft" : sanitized;
    }
}