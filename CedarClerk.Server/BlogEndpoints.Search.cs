using System.Text;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Search;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Server;

// Wave 1 item 2, the public half — a plain GET form over the FTS index. The query never reaches
// SQL as anything but a sanitized parameter (DraftSearchIndex owns that), and the index is only
// ever asked for this owner's published public posts, so this page can show nothing the blog
// index does not already show.
public static partial class BlogEndpoints
{
    private const int BlogSearchLimit = 20;
    private const int BlogSearchQueryMaxLength = 200;

    // Inline rather than a BlogIcons member: the glyph set lives in Core, and one magnifier does
    // not justify reaching across the module line — the header draws its own SVGs the same way.
    private const string SearchIcon =
        "<svg class=\"gl\" width=\"15\" height=\"15\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" "
        + "stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">"
        + "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-3.8-3.8\"/></svg>";

    private static string SearchFormHtml(string query, bool en, string? lang = null) =>
        $"""
        <form class="search-form" method="get" action="/search" role="search">
        {(lang is null ? "" : $"<input type=\"hidden\" name=\"lang\" value=\"{System.Net.WebUtility.HtmlEncode(lang)}\">")}
        <input class="search-input" type="search" name="q" value="{System.Net.WebUtility.HtmlEncode(query)}"
               maxlength="{BlogSearchQueryMaxLength}" placeholder="{(en ? "Search posts" : "Поиск по постам")}"
               aria-label="{(en ? "Search posts" : "Поиск по постам")}">
        <button class="search-btn" type="submit">{SearchIcon}<span>{(en ? "Search" : "Найти")}</span></button>
        </form>
        """;

    private static async Task RenderSearchAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        var channel = await GetBlogChannelInfoAsync(db, site);

        // The same convention the index follows: English chrome unless the reader asked for a
        // language the page has words for.
        var lang = ctx.Request.Query["lang"].FirstOrDefault() is { } q && Languages.IsContentLanguage(q)
            ? q
            : Languages.English;
        var en = lang != Languages.Russian;

        var query = (ctx.Request.Query["q"].FirstOrDefault() ?? "").Trim();
        if (query.Length > BlogSearchQueryMaxLength)
            query = query[..BlogSearchQueryMaxLength];

        var sb = new StringBuilder();
        sb.Append("<a class=\"back-link\" href=\"/?lang=").Append(Uri.EscapeDataString(lang)).Append("\">").Append(BlogIcons.ArrowLeft).Append(' ')
          .Append(en ? "All posts" : "Все посты").Append("</a>");
        sb.Append("<div class=\"search-head\"><h1>").Append(en ? "Search" : "Поиск").Append("</h1>");
        sb.Append(SearchFormHtml(query, en, ctx.Request.Query.ContainsKey("lang") ? lang : null));
        sb.Append("</div>");

        if (query.Length > 0)
        {
            // Resolved only when there is something to ask — the empty form needs no index.
            var index = ctx.RequestServices.GetRequiredService<IDraftSearchIndex>();
            var langFilter = lang == Languages.English && ctx.Request.Query["lang"].Count == 0 ? null : lang;
            var hits = await index.SearchPublishedAsync(site.OwnerId, query, langFilter, BlogSearchLimit);

            if (hits.Count == 0)
            {
                sb.Append("<p class=\"empty\">").Append(en ? "Nothing found." : "Ничего не найдено.").Append("</p>");
            }
            else
            {
                sb.Append("<div class=\"post-list\">");
                foreach (var hit in hits)
                {
                    sb.Append("<a class=\"post-card\" href=\"/").Append(hit.Slug);
                    if (hit.Language.Length > 0)
                        sb.Append("?lang=").Append(Uri.EscapeDataString(hit.Language));
                    sb.Append("\">");
                    sb.Append("<div class=\"post-card-meta\">");
                    if (hit.PublishedAt is { } published)
                        sb.Append("<span class=\"post-card-date\">")
                          .Append(BlogDateFormatter.DateLocal(published, lang, site.TimeZoneId)).Append("</span>");
                    if (hit.Language.Length > 0)
                        sb.Append("<span class=\"post-card-langs\">").Append(hit.Language.ToUpperInvariant()).Append("</span>");
                    sb.Append("</div>");
                    sb.Append("<div class=\"post-card-title\">").Append(System.Net.WebUtility.HtmlEncode(hit.Title)).Append("</div>");
                    if (hit.Snippet.Length > 0)
                        sb.Append("<div class=\"post-card-excerpt\">").Append(System.Net.WebUtility.HtmlEncode(hit.Snippet)).Append("</div>");
                    sb.Append("</a>");
                }
                sb.Append("</div>");
            }
        }

        // Result pages are per-query permutations of content the index already exposes — nothing
        // for a crawler to keep.
        const string meta = "<meta name=\"robots\" content=\"noindex\">";
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(en ? "Search" : "Поиск", sb.ToString(), lang,
            RenderHeader(channel, lang), meta));
    }
}
