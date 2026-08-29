using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CedarClerk.Server;

/// <summary>
/// GET /og/{slug}.png on the blog host (Wave 1 item 1) — the branded 1200x630 card a post without
/// a cover offers to link-preview crawlers. Rendered from embedded fonts and the design tokens,
/// cached on disk per (title, site name, card version); the meta URL carries ?v={hash} so a title
/// edit busts CDN and browser caches without any purge machinery.
/// </summary>
public static class OgImageEndpoint
{
    public const int Width = 1200;
    public const int Height = 630;

    /// <summary>Bump when the card's look changes — it is part of the cache key everywhere.</summary>
    public const string OgCardVersion = "1";

    private const int AccentBarWidth = 8;
    private const int MarginX = 96;
    private const float TitleFontSize = 62f;
    private const float SiteFontSize = 28f;
    private const int TitleMaxLines = 4;
    private const float TitleLineHeight = 1.25f;

    private static readonly Lazy<CardFonts> Fonts = new(LoadFonts);

    private sealed record CardFonts(Font Title, FontFamily[] TitleFallback, Font Site, FontFamily[] SiteFallback);

    public static async Task HandleAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string fileName)
    {
        if (!fileName.EndsWith(".png", StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var slug = fileName[..^4];

        // A listed-while-private post already shows its title to crawlers (OgMetaPolicy
        // .TitleImageOnly), so a card repeating that title reveals nothing new; an unlisted one
        // stays indistinguishable from a missing slug.
        var draft = await db.Drafts.FirstOrDefaultAsync(d =>
            d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished
            && (!d.IsPrivate || d.IsListedWhilePrivate));
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var title = draft.ArticleTitle ?? draft.Title;
        var siteName = await BlogSubscriberNotifier.SiteNameAsync(db, site);
        var hash = Hash8(title, siteName);

        ctx.Response.Headers.CacheControl = "public, max-age=86400";
        ctx.Response.Headers.ETag = $"\"{hash}\"";
        if (ctx.Request.Headers.IfNoneMatch.ToString().Contains(hash, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        var bytes = await CachedCardAsync(site.OwnerId, slug, hash, title, siteName);
        ctx.Response.ContentType = "image/png";
        await ctx.Response.Body.WriteAsync(bytes);
    }

    /// <summary>Absolute, on the blog host, ?v so a title edit is a new URL to every cache.</summary>
    public static string ImageUrl(BlogSite site, string slug, string title, string siteName) =>
        $"{site.BaseUrl}/og/{slug}.png?v={Hash8(title, siteName)}";

    public static string Hash8(string title, string siteName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{title}|{siteName}|{OgCardVersion}")))[..8]
            .ToLowerInvariant();

    private static async Task<byte[]> CachedCardAsync(string ownerId, string slug, string hash, string title, string siteName)
    {
        var dataDir = Environment.GetEnvironmentVariable(Consts.DataDirectoryKey);
        if (dataDir is null) return RenderCard(title, siteName);

        var cacheDir = Path.Combine(dataDir, "og-cache", ownerId);
        var cachePath = Path.Combine(cacheDir, $"{slug}-{hash}.png");
        if (File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath);

        var bytes = RenderCard(title, siteName);
        try
        {
            Directory.CreateDirectory(cacheDir);
            foreach (var stale in Directory.EnumerateFiles(cacheDir, $"{slug}-*.png"))
                File.Delete(stale);
            var tmp = cachePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (IOException)
        {
            // A concurrent request won the write — the bytes in hand are the same card.
        }
        return bytes;
    }

    /// <summary>The card itself, deterministic from its two strings — what the unit test renders.</summary>
    public static byte[] RenderCard(string title, string siteName)
    {
        var fonts = Fonts.Value;
        var paper = Color.ParseHex(DesignTokens.MaterialsLight["paper-bright"]);
        var accent = Color.ParseHex(DesignTokens.Light["accent"]);
        var ink = Color.ParseHex(DesignTokens.Light["text"]);
        var muted = Color.ParseHex(DesignTokens.Light["t2"]);

        var maxTextWidth = Width - MarginX * 2;
        var lines = WrapTitle(title, fonts.Title, fonts.TitleFallback, maxTextWidth);

        using var image = new Image<Rgba32>(Width, Height);
        image.Mutate(x =>
        {
            x.Fill(paper);
            x.Fill(accent, new RectangleF(0, 0, AccentBarWidth, Height));

            var lineStep = TitleFontSize * TitleLineHeight;
            var blockHeight = lines.Count * lineStep;
            var y = (Height - blockHeight) / 2f - 24f;
            foreach (var line in lines)
            {
                x.DrawText(new RichTextOptions(fonts.Title)
                {
                    Origin = new PointF(MarginX, y),
                    FallbackFontFamilies = fonts.TitleFallback,
                }, line, ink);
                y += lineStep;
            }

            x.DrawText(new RichTextOptions(fonts.Site)
            {
                Origin = new PointF(MarginX, Height - 84f),
                FallbackFontFamilies = fonts.SiteFallback,
            }, siteName, muted);
        });

        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private static List<string> WrapTitle(string title, Font font, FontFamily[] fallback, float maxWidth)
    {
        float WidthOf(string s) => TextMeasurer.MeasureAdvance(s,
            new TextOptions(font) { FallbackFontFamilies = fallback }).Width;

        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [""];

        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && WidthOf(candidate) > maxWidth)
            {
                lines.Add(current);
                current = word;
                if (lines.Count == TitleMaxLines) break;
            }
            else
            {
                current = candidate;
            }
        }

        if (lines.Count < TitleMaxLines)
        {
            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        // Out of lines: the fourth line makes room for an ellipsis instead of overflowing.
        var last = lines[^1];
        while (last.Length > 0 && WidthOf(last + "…") > maxWidth)
            last = last.Contains(' ') ? last[..last.LastIndexOf(' ')] : last[..^1];
        lines[^1] = last + "…";
        return lines;
    }

    private static CardFonts LoadFonts()
    {
        var titleCollection = new FontCollection();
        var titleFamily = AddEmbedded(titleCollection, "literata-latin-600-normal.woff2");
        var titleCyrillic = AddEmbedded(new FontCollection(), "literata-cyrillic-600-normal.woff2");

        var siteCollection = new FontCollection();
        var siteFamily = AddEmbedded(siteCollection, "source-sans-3-latin-400-normal.woff2");
        var siteCyrillic = AddEmbedded(new FontCollection(), "source-sans-3-cyrillic-400-normal.woff2");

        return new CardFonts(
            titleFamily.CreateFont(TitleFontSize),
            [titleCyrillic],
            siteFamily.CreateFont(SiteFontSize),
            [siteCyrillic]);
    }

    // The latin and cyrillic subsets carry the same family name, so each goes into its own
    // collection and the cyrillic one rides along as a fallback family instead of colliding.
    private static FontFamily AddEmbedded(FontCollection collection, string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded font {fileName} is missing from the assembly.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return collection.Add(stream);
    }
}
