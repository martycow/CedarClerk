using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>Where the landing's uploaded screenshots live. Injected, like <see cref="MediaPaths"/>.</summary>
public record LandingPaths(string Dir);

// The landing tab of the admin panel (ADR-215). Same file split as Entities.IndieDev.cs: a second
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

    public record LandingSaveRequest(
        string? KickerEn, string? KickerRu,
        string? HeroTitleEn, string? HeroTitleRu,
        string? HeroSubEn, string? HeroSubRu,
        string? ProofEn, string? ProofRu,
        string? NoteEn, string? NoteRu,
        string? ShowcaseBlog,
        bool ShowShots, bool ShowFeatures, bool ShowPricing, bool ShowRoadmap, bool ShowStory,
        // Nullable so an editor build that predates the field cannot reset it by omission.
        bool? ShowDownload,
        List<LandingShot>? Shots,
        List<LandingRoadmapColumn>? Roadmap,
        List<LandingStoryStep>? Story);

    private static void MapLandingAdmin(RouteGroupBuilder group)
    {
        // One read for the whole tab: the stored row, whatever is actually on disk, and how many
        // people are waiting. Three requests to draw one screen is three chances to draw half of it.
        group.MapGet("/landing", async (CedarDbContext db, LandingPaths paths, IConfiguration cfg) =>
        {
            var row = await db.LandingSettings.AsNoTracking().FirstOrDefaultAsync();
            var content = LandingContent.From(row, cfg[Consts.General.ShowcaseBlogCfg]);
            return Results.Ok(new
            {
                row?.KickerEn, row?.KickerRu,
                row?.HeroTitleEn, row?.HeroTitleRu,
                row?.HeroSubEn, row?.HeroSubRu,
                row?.ProofEn, row?.ProofRu,
                row?.NoteEn, row?.NoteRu,
                row?.ShowcaseBlog,
                content.ShowShots,
                content.ShowFeatures,
                content.ShowPricing,
                content.ShowRoadmap,
                content.ShowStory,
                content.ShowDownload,
                content.Shots,
                content.Roadmap,
                content.Story,
                // What the placeholders would say if the fields above stay empty, so the editor can
                // show them as placeholders rather than making the admin guess what "empty" means.
                Defaults = new
                {
                    Kicker = LandingTexts.Kicker,
                    HeroTitle = LandingTexts.HeroTitle,
                    HeroSub = LandingTexts.HeroSub,
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
        {
            var actor = (await users.GetUserAsync(principal))!;
            var row = await db.LandingSettings.FirstOrDefaultAsync();
            if (row is null)
            {
                row = new LandingSettings();
                db.LandingSettings.Add(row);
            }

            row.KickerEn = Trim(req.KickerEn);
            row.KickerRu = Trim(req.KickerRu);
            row.HeroTitleEn = Trim(req.HeroTitleEn);
            row.HeroTitleRu = Trim(req.HeroTitleRu);
            row.HeroSubEn = Trim(req.HeroSubEn);
            row.HeroSubRu = Trim(req.HeroSubRu);
            row.ProofEn = Trim(req.ProofEn);
            row.ProofRu = Trim(req.ProofRu);
            row.NoteEn = Trim(req.NoteEn);
            row.NoteRu = Trim(req.NoteRu);
            row.ShowcaseBlog = Trim(req.ShowcaseBlog);
            row.ShowShots = req.ShowShots;
            row.ShowFeatures = req.ShowFeatures;
            row.ShowPricing = req.ShowPricing;
            row.ShowRoadmap = req.ShowRoadmap;
            row.ShowStory = req.ShowStory;
            row.ShowDownload = req.ShowDownload ?? row.ShowDownload;
            row.ShotsJson = LandingContent.Serialize(req.Shots ?? []);
            row.RoadmapJson = LandingContent.Serialize(req.Roadmap ?? []);
            row.StoryJson = LandingContent.Serialize(req.Story ?? []);
            row.UpdatedAt = DateTime.UtcNow;

            // The landing is the one page a stranger sees, and it is now editable from a form.
            // "Who changed the front door, and when" belongs in the same journal as everything else
            // that can be changed from this panel.
            Audit(db, actor, "landing", details: $"{req.Shots?.Count ?? 0} shots");
            await db.SaveChangesAsync();
            return Results.Ok();
        });

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
