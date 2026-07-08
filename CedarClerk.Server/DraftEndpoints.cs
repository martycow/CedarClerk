using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftEndpoints
{
    public record SaveDraftRequest(string Title, string CedarJson);

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
        
        // List of drafts
        groupBuilder.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await db.Drafts.Where(d => d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new { d.Id, d.Title, d.CreatedAt, d.UpdatedAt })
                .ToListAsync();
        });

        // Specific draft
        groupBuilder.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            return draft is null ? Results.NotFound() : Results.Ok(new { draft.Id, draft.Title, draft.CedarJson, draft.CreatedAt, draft.UpdatedAt });
        });

        // Create new draft
        groupBuilder.MapPost("/", async (SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = new Draft { Title = req.Title, CedarJson = req.CedarJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        });

        // Update specific draft
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

        // Delete specific draft
        groupBuilder.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.Drafts
                .Where(x => x.Id == id && x.OwnerId == uid)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        // Export as .cedar (zip: document.json + assets/)
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

        // Import from .cedar (creates a new draft owned by the current user)
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
            var pathRewrites = new Dictionary<string, string>();

            foreach (var (originalName, bytes) in pkg.Assets)
            {
                var contentType = ImageContentSniffer.DetectContentType(bytes);
                if (contentType is null || !ImportImageExtensions.TryGetValue(contentType, out var ext))
                    return Results.BadRequest(new { error = $"Unsupported or invalid asset: {originalName}" });
                if (bytes.Length > AssetEndpoints.ImageMaxBytes)
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