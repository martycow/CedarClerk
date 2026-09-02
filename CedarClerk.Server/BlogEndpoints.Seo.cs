using System.Text;
using System.Text.Json;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Wave 1 item 4 — what a crawler is handed on a plate: one sitemap per tenant host, and a
// machine-readable Article block on the pages that already reveal everything anyway. Nothing here
// widens visibility: the sitemap lists exactly the pages a stranger can open, and JSON-LD is
// emitted only under the Full meta policy — a gated post keeps its silence.
public static partial class BlogEndpoints
{
    private static async Task RenderSitemapAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        // Public posts only — a listed-private post advertises itself on the index by the owner's
        // choice, but a sitemap is an invitation to crawl, and the gate is not a page to invite
        // crawlers to.
        var posts = await db.Drafts
            .Where(d => d.OwnerId == site.OwnerId && d.IsBlogPublished && !d.IsPrivate && d.BlogSlug != null)
            .OrderByDescending(d => d.BlogPublishedAt)
            .Select(d => new { d.BlogSlug, d.BlogPublishedAt, d.UpdatedAt })
            .ToListAsync();

        var series = await db.Series
            .Where(s => s.OwnerId == site.OwnerId)
            .Select(s => s.Slug)
            .ToListAsync();

        var showcases = await db.Projects
            .Where(p => p.OwnerId == site.OwnerId && p.ShowcaseSlug != null && p.ArchivedAt == null)
            .Select(p => p.ShowcaseSlug!)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""").Append('\n');
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        void Url(string path, DateTime? lastMod = null)
        {
            sb.Append("<url><loc>").Append(System.Net.WebUtility.HtmlEncode(site.BaseUrl + path)).Append("</loc>");
            if (lastMod is { } mod)
                sb.Append("<lastmod>").Append(mod.ToString("yyyy-MM-dd")).Append("</lastmod>");
            sb.Append("</url>");
        }

        Url("/");
        foreach (var p in posts)
            Url($"/{p.BlogSlug}", p.UpdatedAt > p.BlogPublishedAt ? p.UpdatedAt : p.BlogPublishedAt ?? p.UpdatedAt);
        foreach (var slug in series)
            Url($"/series/{slug}");
        foreach (var slug in showcases)
        {
            Url($"/showcase/{slug}");
            Url($"/showcase/{slug}/press");
        }

        sb.Append("</urlset>");

        ctx.Response.ContentType = "application/xml; charset=utf-8";
        await ctx.Response.WriteAsync(sb.ToString());
    }

    /// <summary>
    /// The Article block a Full-policy post page carries in its head. Serialized with the default
    /// encoder, which escapes angle brackets as unicode sequences — user text can never close the
    /// script tag it is standing in.
    /// </summary>
    private static string ArticleJsonLd(string headline, DateTime? publishedUtc, DateTime? modifiedUtc,
        string lang, string imageUrl, string siteName, string canonicalUrl)
    {
        var author = new Dictionary<string, object> { ["@type"] = "Person", ["name"] = siteName };
        var doc = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Article",
            ["headline"] = headline,
            ["inLanguage"] = lang,
            ["image"] = new[] { imageUrl },
            ["author"] = author,
            ["publisher"] = new Dictionary<string, object> { ["@type"] = "Organization", ["name"] = siteName },
            ["mainEntityOfPage"] = canonicalUrl,
        };
        if (publishedUtc is { } published)
            doc["datePublished"] = published.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        if (modifiedUtc is { } modified)
        {
            // UpdatedAt predates BlogPublishedAt on first publish (the draft was last saved before
            // the publish click) — a modification date earlier than the publication date is a claim
            // no crawler should be handed.
            if (publishedUtc is { } floor && modified < floor)
                modified = floor;
            doc["dateModified"] = modified.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        return "<script type=\"application/ld+json\">" + JsonSerializer.Serialize(doc) + "</script>";
    }
}
