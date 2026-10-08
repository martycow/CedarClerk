using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>Where the landing's uploaded screenshots live. Injected, like <see cref="MediaPaths"/>.</summary>
public record LandingPaths(string Dir);

// The landing tab of the admin panel (ADR-215, ADR-323). Same file split as Entities.IndieDev.cs: a second
// file rather than a second class, so these routes are inside the group's admin gate by
// construction and cannot be added outside it by accident.
public static partial class AdminEndpoints
{
    /// <summary>
    /// PNG, JPEG and WebP only. A screenshot is a picture of the product; the other types
    /// AssetEndpoints accepts are things a post carries, and none of them belong on a sales page.
    /// </summary>
    private static readonly Dictionary<string, string> LandingImageTypes = new()
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
    };

    public record LandingSaveRequest(LandingDocument? Document);

    public record LandingPreviewRequest(LandingDocument? Document, string? Language);

    private static void MapLandingAdmin(RouteGroupBuilder group)
    {
        // One read for the whole tab: the document, what a block may be, whatever is actually on
        // disk, and how many people are waiting.
        group.MapGet("/landing", async (CedarDbContext db, LandingPaths paths, IConfiguration cfg) =>
        {
            var row = await db.LandingSettings.AsNoTracking().FirstOrDefaultAsync();
            return Results.Ok(new
            {
                Document = LandingDocument.FromRow(row, cfg[Consts.General.ShowcaseBlogCfg]),
                Stored = LandingDocument.IsStored(row),
                Schema = new
                {
                    Layouts = LandingLayouts.All,
                    Blocks = LandingBlocks.All,
                    LandingDocument.Marks,
                    LandingDocument.RequiredLanguages,
                    Languages = Languages.UiLanguages.Select(code => new { Code = code, Endonym = Languages.EndonymOf(code) }),
                    Tokens = new[] { LandingDocument.LanguagesToken, LandingDocument.NetworksToken },
                },
                // The configured fallback, shown when the override is blank — otherwise an empty
                // field reads as "no showcase blog" while the footer still carries a link.
                ConfiguredShowcaseBlog = cfg[Consts.General.ShowcaseBlogCfg],
                Files = UploadedFiles(paths),
                Waitlist = await db.WaitlistEntries.CountAsync(),
            });
        });

        group.MapPut("/landing", async (LandingSaveRequest req, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CedarDbContext db) =>
            await SaveLandingAsync(req.Document, (await users.GetUserAsync(principal))!, db));

        group.MapPost("/landing/preview", PreviewLandingAsync);

        // The uploaded file keeps a name of the server's making. A screenshot arrives called
        // whatever the maintainer's screenshot tool called it, and that name goes straight into a
        // public URL — a generated one cannot carry a path separator, a leading dot or somebody
        // else's file name.
        group.MapPost("/landing/upload", async (IFormFile file, LandingPaths paths) =>
        {
            if (!LandingImageTypes.TryGetValue(file.ContentType, out var ext))
                return Results.BadRequest(new { error = ErrorMessages.LandingImageUnsupported(file.ContentType) });
            if (file.Length == 0 || file.Length > Consts.FileSizes.ImageMaxBytes)
                return Results.BadRequest(new
                {
                    error = ErrorMessages.LandingImageTooLarge(Consts.FileSizes.ImageMaxBytes / (1024 * 1024)),
                });

            Directory.CreateDirectory(paths.Dir);
            var name = $"shot_{Guid.NewGuid():N}{ext}";

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            await File.WriteAllBytesAsync(
                Path.Combine(paths.Dir, name),
                ImageMetadataStripper.Strip(buffer.ToArray(), file.ContentType));

            return Results.Ok(new { File = name, Url = LandingContent.ShotUrl(name) });
        }).DisableAntiforgery();

        // Deleting the file, not the row: the row is whatever the last save wrote, and a screenshot
        // still referenced by it renders as a broken frame until the next save either way. Removing
        // it from the list is the editor's job; removing it from the disk is this.
        group.MapDelete("/landing/files/{file}", (string file, LandingPaths paths) =>
        {
            var path = SafeLandingPath(paths, file);
            if (path is null) return Results.BadRequest(new { error = ErrorMessages.LandingBadFileName });
            if (File.Exists(path)) File.Delete(path);
            return Results.Ok();
        });

        // Newest first: the useful question about a waitlist is who joined since you last looked.
        group.MapGet("/landing/waitlist", async (CedarDbContext db) =>
            Results.Ok(await db.WaitlistEntries
                .OrderByDescending(w => w.CreatedAt)
                .Select(w => new { w.Id, w.Email, w.Language, w.CreatedAt })
                .ToListAsync()));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Stores the document and nothing else: the fixed columns it replaced are left exactly as
    /// they were, so a rollback to a build that still reads them finds the page it last drew.
    /// </summary>
    public static async Task<IResult> SaveLandingAsync(LandingDocument? document, ApplicationUser actor, CedarDbContext db)
    {
        if (document is null) return Results.BadRequest(new { error = ErrorMessages.LandingDocumentInvalid });
        if (document.Normalize().Validate() is { } problem) return Results.BadRequest(new { error = problem });

        var row = await db.LandingSettings.FirstOrDefaultAsync();
        if (row is null)
        {
            row = new LandingSettings();
            db.LandingSettings.Add(row);
        }
        row.DocumentJson = document.Serialize();
        row.UpdatedAt = DateTime.UtcNow;

        // "Who changed the front door, and when" belongs in the same journal as everything else
        // that can be changed from this panel.
        Audit(db, actor, "landing",
            details: $"{document.Sections.Count} sections, {document.Sections.Sum(s => s.Blocks.Count)} blocks");
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    /// <summary>
    /// The page an unsaved document would be, from the renderer the public page uses. A document
    /// that would not save is still drawn — the renderer escapes and drops what it will not show —
    /// and the reason it would not save travels beside it.
    /// </summary>
    public static async Task<IResult> PreviewLandingAsync(LandingPreviewRequest req, HttpResponse response,
        CedarDbContext db, IConfiguration cfg)
    {
        response.Headers.CacheControl = "private, no-store";
        if (req.Document is null) return Results.BadRequest(new { error = ErrorMessages.LandingDocumentInvalid });
        var document = req.Document.Normalize();
        if (document.Serialize().Length > LandingDocument.MaxBytes)
            return Results.BadRequest(new { error = ErrorMessages.LandingDocumentTooLarge(LandingDocument.MaxBytes / 1024) });

        var language = LandingRenderer.ChooseLanguage(req.Language, null, document.Languages);
        var html = LandingRenderer.Render(language, document, await DiscoveryEndpoints.LoadAsync(db, cfg),
            cfg[Consts.General.ShowcaseBlogCfg], analyticsKey: null, Consts.Analytics.DefaultHost);
        // The preview is framed without scripts or navigation, so a link in it opens nothing.
        html = html.Replace("<head>", "<head>\n<base target=\"_blank\">");
        return Results.Ok(new { Html = html, Language = language, Problem = document.Validate() });
    }

    private static List<string> UploadedFiles(LandingPaths paths) =>
        !Directory.Exists(paths.Dir)
            ? []
            : new DirectoryInfo(paths.Dir)
                .GetFiles()
                .Where(f => LandingImageTypes.ContainsValue(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => f.Name)
                .ToList();

    /// <summary>
    /// A path inside the landing directory, or null. The name reaches here from a URL segment, and
    /// <c>GetFullPath</c> is what turns "..%2f..%2fcedar.db" into something this can refuse.
    /// </summary>
    private static string? SafeLandingPath(LandingPaths paths, string file)
    {
        if (string.IsNullOrWhiteSpace(file) || file.Contains('/') || file.Contains('\\')) return null;
        var root = Path.GetFullPath(paths.Dir);
        var full = Path.GetFullPath(Path.Combine(root, file));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }
}
