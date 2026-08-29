using System.Text;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Wave 1 item 6 — the press kit page at /games/{slug}/press. Everything on it is either already
// public on the showcase or one of the five optional press fields the owner filled in; an empty
// section is omitted, never rendered blank.
public static partial class BlogEndpoints
{
    private static async Task RenderPressAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var channel = await GetBlogChannelInfoAsync(db, site);
        var project = await FindShowcaseAsync(db, site, slug);
        if (project is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(PageShell("Not found", "<p class=\"empty\">Page not found.</p>",
                Languages.Russian, RenderHeader(channel, Languages.Russian)));
            return;
        }

        var pageLang = ctx.Request.Query["lang"].ToString() is { Length: > 0 } requested
                       && Languages.IsContentLanguage(requested)
            ? requested
            : Languages.Russian;
        var en = pageLang != Languages.Russian;

        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var mainBase = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;

        var developer = await db.Users.Where(u => u.Id == site.OwnerId)
            .Select(u => u.AuthorDisplayName).FirstOrDefaultAsync();
        developer = string.IsNullOrWhiteSpace(developer) ? channel?.Title : developer;

        var posts = await ShowcasePostsAsync(db, site, project.Id);
        var publicPosts = posts.Where(p => !p.IsPrivate).ToList();
        var links = ParseShowcaseLinks(project.ShowcaseLinks);
        var gallery = ShowcaseGallery.Parse(project.ShowcaseGallery);
        var packUrl = ShowcasePath(ctx, project, "/press/pack.zip");

        var sb = new StringBuilder();

        sb.Append("<div class=\"press-bar\"><span class=\"press-bar-name\">").Append(Html(project.Name))
          .Append("</span><span class=\"press-bar-label\">press kit</span><span class=\"spacer\"></span>");
        sb.Append("<a class=\"press-pack-chip\" href=\"").Append(packUrl).Append("\">")
          .Append(en ? "Download press pack (.zip)" : "Скачать пресс-пак (.zip)").Append("</a>");
        sb.Append("</div>");

        sb.Append("<h1 class=\"press-h1\">").Append(Html(project.Name)).Append("</h1>");
        if (project.Description is { Length: > 0 } tagline)
            sb.Append("<p class=\"press-tagline\">").Append(Html(Shorten(tagline))).Append("</p>");
        if (links.Count > 0)
        {
            sb.Append("<div class=\"press-chips\">");
            foreach (var (label, _) in links)
                sb.Append("<span class=\"press-chip\">").Append(Html(label)).Append("</span>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"press-grid\">");

        // ── Left: the factsheet card ─────────────────────────────────────────────────────────────
        sb.Append("<div class=\"press-card\">");
        sb.Append("<div class=\"press-card-title\">").Append(en ? "Factsheet" : "Факты").Append("</div>");

        void Row(string label, string valueHtml)
        {
            sb.Append("<div class=\"press-row\"><div class=\"press-row-label\">").Append(Html(label))
              .Append("</div><div class=\"press-row-value\">").Append(valueHtml).Append("</div></div>");
        }
        void TextRow(string label, string? value)
        {
            if (value is { Length: > 0 })
                Row(label, Html(value));
        }

        TextRow(en ? "Developer" : "Разработчик", developer);
        TextRow(en ? "Price" : "Цена", project.PressPrice);
        TextRow(en ? "Engine" : "Движок", project.PressEngine);
        TextRow(en ? "Genre" : "Жанр", project.PressGenre);
        if (links.Count > 0)
        {
            var linkHtml = new StringBuilder();
            foreach (var (label, url) in links)
            {
                if (linkHtml.Length > 0) linkHtml.Append("<br>");
                linkHtml.Append("<a href=\"").Append(Html(url)).Append("\" rel=\"noopener\" target=\"_blank\">")
                        .Append(Html(label)).Append("</a>");
            }
            Row(en ? "Links" : "Ссылки", linkHtml.ToString());
        }
        if (project.PressContactEmail is { Length: > 0 } contact)
            Row(en ? "Press contact" : "Контакт для прессы",
                $"<a href=\"mailto:{Html(contact)}\">{Html(contact)}</a>");

        // Free rows: "Label: value" per line; a line without a colon is skipped, not rendered.
        if (project.PressFactsheetRows is { Length: > 0 } freeRows)
        {
            foreach (var line in freeRows.Split('\n'))
            {
                var split = line.IndexOf(':');
                if (split <= 0) continue;
                var label = line[..split].Trim();
                var value = line[(split + 1)..].Trim();
                if (label.Length > 0 && value.Length > 0)
                    Row(label, Html(value));
            }
        }

        if (project.CoverUrl is { Length: > 0 } coverPath)
        {
            sb.Append("<div class=\"press-card-divider\"></div>");
            sb.Append("<div class=\"press-row\"><div class=\"press-row-label\">")
              .Append(en ? "Logo & key art" : "Лого и арты").Append("</div><div class=\"press-row-value\">")
              .Append("<a href=\"").Append(MediaSrc(coverPath, mainBase)).Append("\" target=\"_blank\" rel=\"noopener\">")
              .Append(en ? "Cover image" : "Обложка").Append("</a></div></div>");
        }
        sb.Append("</div>");

        // ── Right column ─────────────────────────────────────────────────────────────────────────
        sb.Append("<div class=\"press-main\">");

        if (project.Description is { Length: > 0 } about)
        {
            sb.Append("<h2 class=\"showcase-section\">").Append(en ? "About the game" : "Об игре").Append("</h2>");
            sb.Append("<div class=\"press-about\">");
            foreach (var paragraph in about.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sb.Append("<p>").Append(Html(paragraph)).Append("</p>");
            sb.Append("</div>");
        }

        if (gallery.Count > 0)
        {
            sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Screenshots" : "Скриншоты")
              .Append(" <a class=\"press-download-all\" href=\"").Append(packUrl).Append("\">")
              .Append(en ? "download all" : "скачать все").Append("</a></h2>");
            sb.Append("<div class=\"showcase-gallery press-shots\">");
            foreach (var image in gallery)
            {
                var src = MediaSrc(image, mainBase);
                sb.Append("<a class=\"showcase-shot\" href=\"").Append(src)
                  .Append("\" target=\"_blank\" rel=\"noopener\"><img src=\"").Append(src)
                  .Append("\" loading=\"lazy\" alt=\"\"></a>");
            }
            sb.Append("</div>");
        }

        if (YouTubeLink.EmbedUrl(project.ShowcaseTrailerUrl) is { } embed)
        {
            sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Trailer" : "Трейлер").Append("</h2>");
            sb.Append("<div class=\"showcase-trailer\"><iframe src=\"").Append(Html(embed))
              .Append("\" loading=\"lazy\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" allowfullscreen></iframe></div>");
        }

        if (publicPosts.Count > 0)
        {
            var latest = publicPosts[0];
            var countLine = en
                ? $"{publicPosts.Count} devlog {(publicPosts.Count == 1 ? "post" : "posts")}"
                : $"Постов в девлоге: {publicPosts.Count}";
            sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Devlog" : "Девлог").Append("</h2>");
            sb.Append("<p class=\"press-devlog-line\">").Append(countLine).Append(" — <a href=\"")
              .Append(ShowcasePath(ctx, project, "")).Append("\">")
              .Append(en ? "read the feed" : "читать ленту").Append("</a>. ")
              .Append(en ? "Latest: " : "Последний: ").Append("<a href=\"/").Append(latest.BlogSlug).Append("\">")
              .Append(Html(latest.ArticleTitle ?? latest.Title)).Append("</a></p>");
        }

        sb.Append("</div></div>");

        var blogBase = site.BaseUrl;
        var ogImage = project.CoverUrl is { Length: > 0 } cv ? MediaSrc(cv, mainBase) : $"{blogBase}/og-default.png";
        var meta = OgMetaBuilder.Build(new OgMetaInput(
            $"{project.Name} — press kit", project.Description, $"{blogBase}/games/{project.ShowcaseSlug}/press",
            ogImage, 1200, 630,
            channel?.Title ?? "Cedar Clerk", pageLang,
            [], null, null, null, IsArticle: false), OgMetaPolicy.Full);

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell($"{project.Name} — press kit", sb.ToString(), pageLang,
            RenderHeader(channel, pageLang), meta, mainClass: "site-main--press"));
    }
}
