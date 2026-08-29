using System.IO.Compression;
using System.Text;
using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// GET /games/{slug}/press/pack.zip (Wave 1 item 6) — everything a journalist wants in one file:
/// the factsheet as text, the cover, the showcase screenshots. Streamed on the fly, no caching:
/// the pack is requested rarely and its inputs (gallery, press fields) change without ceremony.
/// </summary>
public static class PressPackEndpoint
{
    public static async Task HandleAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string gameSlug)
    {
        // Owner named explicitly beside the ambient filter — the house rule for every blog query.
        var project = await db.Projects.FirstOrDefaultAsync(p =>
            p.ShowcaseSlug == gameSlug && p.OwnerId == site.OwnerId && p.ArchivedAt == null);
        if (project is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var mediaDir = ctx.RequestServices.GetRequiredService<MediaPaths>().Dir;

        // Buffered, not streamed: ZipArchive writes central-directory records synchronously on
        // dispose, and Kestrel forbids sync IO on the response body — a live request would 500
        // with zero bytes sent. A press pack is a factsheet and a dozen screenshots; buffering
        // it is cheaper than the AllowSynchronousIO escape hatch.
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var factsheet = zip.CreateEntry("factsheet.txt", CompressionLevel.Optimal);
            await using (var entryStream = factsheet.Open())
                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(FactsheetText(project, site)));

            if (MediaFilePath(mediaDir, project.CoverUrl) is { } coverPath)
                await AddFileAsync(zip, "cover" + Path.GetExtension(coverPath), coverPath);

            var index = 1;
            foreach (var galleryPath in ShowcaseGallery.Parse(project.ShowcaseGallery))
            {
                if (MediaFilePath(mediaDir, galleryPath) is not { } filePath) continue;
                await AddFileAsync(zip, $"screenshots/{index:00}-{Path.GetFileName(filePath)}", filePath);
                index++;
            }
        }

        ctx.Response.ContentType = "application/zip";
        ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{gameSlug}-press-pack.zip\"";
        ctx.Response.ContentLength = buffer.Length;
        buffer.Position = 0;
        await buffer.CopyToAsync(ctx.Response.Body);
    }

    /// <summary>Plain text on purpose — a factsheet is pasted into other people's documents, and
    /// the least structured format survives that best.</summary>
    public static string FactsheetText(Project project, BlogSite site)
    {
        var sb = new StringBuilder();
        sb.AppendLine(project.Name);
        sb.AppendLine(new string('=', Math.Max(project.Name.Length, 4)));
        sb.AppendLine();
        if (project.Description.Length > 0)
        {
            sb.AppendLine(project.Description);
            sb.AppendLine();
        }

        AppendRow(sb, "Genre", project.PressGenre);
        AppendRow(sb, "Engine", project.PressEngine);
        AppendRow(sb, "Price", project.PressPrice);
        AppendRow(sb, "Press contact", project.PressContactEmail);
        AppendRow(sb, "Website", project.CustomDomain is { } domain
            ? $"https://{domain}"
            : $"{site.BaseUrl}/games/{project.ShowcaseSlug}");

        if (project.PressFactsheetRows is { Length: > 0 } rows)
            foreach (var line in rows.Split('\n'))
                if (line.Trim() is { Length: > 0 } trimmed)
                    sb.AppendLine(trimmed);

        var links = project.ShowcaseLinks.Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (links.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Links");
            sb.AppendLine("-----");
            foreach (var link in links)
            {
                var pipe = link.IndexOf('|');
                sb.AppendLine(pipe > 0 ? $"{link[..pipe].Trim()}: {link[(pipe + 1)..].Trim()}" : link);
            }
        }
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string label, string? value)
    {
        if (value is { Length: > 0 })
            sb.Append(label).Append(": ").AppendLine(value);
    }

    /// <summary>A /media/... path resolved to a file on disk, or null for anything that is not a
    /// plain file name under the media directory — the same shape ShowcaseGallery already enforces.</summary>
    private static string? MediaFilePath(string mediaDir, string? mediaPath)
    {
        if (mediaPath is null || !mediaPath.StartsWith(Consts.Showcase.MediaPrefix, StringComparison.Ordinal)) return null;
        var fileName = mediaPath[Consts.Showcase.MediaPrefix.Length..];
        if (fileName.Length == 0 || fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\')) return null;
        var fullPath = Path.Combine(mediaDir, fileName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    private static async Task AddFileAsync(ZipArchive zip, string entryName, string filePath)
    {
        // Screenshots are already compressed formats; recompressing buys nothing and costs CPU.
        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        await using var entryStream = entry.Open();
        await using var file = File.OpenRead(filePath);
        await file.CopyToAsync(entryStream);
    }
}
