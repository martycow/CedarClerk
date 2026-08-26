using System.Globalization;
using System.Text;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// The public game page (T-159, ADR-134), grown into the game's own site by ADR-216: gallery and
// trailer, counted views and store-link clicks, an address to follow it by, downloadable builds and
// a feed of its own. A second file rather than 400 more lines in a 3400-line one — the routing
// still lives next to the blog's other branches, in HandleRequest.
public static partial class BlogEndpoints
{
    private static async Task RenderShowcaseAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
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

        // T-296 — the same 30-minute cookie a post view uses, so the two numbers mean the same
        // thing: a refresh is not a second reader on either page.
        var viewedCookie = Consts.Showcase.ViewedCookiePrefix + project.Id;
        if (!ctx.Request.Cookies.ContainsKey(viewedCookie))
        {
            await RecordShowcaseStatAsync(db, project, ShowcaseStatKinds.View, "");
            ctx.Response.Cookies.Append(viewedCookie, "1", new CookieOptions
            {
                MaxAge = TimeSpan.FromMinutes(30),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
            });
        }

        var posts = await ShowcasePostsAsync(db, site, project.Id);

        var roadmap = (await db.GameTasks
            .Where(t => t.OwnerId == site.OwnerId && t.ProjectId == project.Id && t.IsPublicRoadmap && t.ArchivedAt == null)
            .Select(t => new { t.Title, t.Status })
            .ToListAsync())
            // A reader's order, not the board's: what is moving now, then what is planned, then done.
            .OrderBy(t => t.Status switch
            {
                TaskStatuses.InProgress => 0,
                TaskStatuses.Planned => 1,
                TaskStatuses.Backlog => 2,
                _ => 3,
            })
            .ThenBy(t => t.Title)
            .ToList();

        // T-299 — a build is offered only when its owner said so and gave somewhere to get it.
        var downloads = await db.Builds
            .Where(b => b.OwnerId == site.OwnerId && b.ProjectId == project.Id
                        && b.IsPublic && b.DownloadUrl != null && b.DownloadUrl != "")
            .OrderByDescending(b => b.ReleasedAt ?? b.CreatedAt)
            .Select(b => new { b.Version, b.Notes, b.ReleasedAt, b.DownloadUrl })
            .ToListAsync();

        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var mainBase = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
        var sb = new StringBuilder();

        sb.Append("<div class=\"showcase-head\">");
        if (project.CoverUrl is { Length: > 0 } cover)
            sb.Append("<img class=\"showcase-cover\" src=\"").Append(MediaSrc(cover, mainBase)).Append("\" alt=\"\">");
        sb.Append("<div class=\"showcase-head-text\"><h1>").Append(Html(project.Name)).Append("</h1>");
        if (project.Description is { Length: > 0 } desc)
            sb.Append("<p class=\"showcase-desc\">").Append(Html(desc)).Append("</p>");

        var links = ParseShowcaseLinks(project.ShowcaseLinks);
        if (links.Count > 0)
        {
            sb.Append("<div class=\"showcase-links\">");
            for (var i = 0; i < links.Count; i++)
            {
                // T-296 — the click is counted by going through us. Server-side because a page with
                // no JavaScript cannot report one; the label still says where it leads.
                sb.Append("<a class=\"showcase-link\" rel=\"noopener\" target=\"_blank\" href=\"")
                  .Append(ShowcasePath(project, $"/go/{i}")).Append("\">")
                  .Append(Html(links[i].Label)).Append("</a>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></div>");

        if (YouTubeLink.EmbedUrl(project.ShowcaseTrailerUrl) is { } embed)
            sb.Append("<div class=\"showcase-trailer\"><iframe src=\"").Append(Html(embed))
              .Append("\" loading=\"lazy\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" allowfullscreen></iframe></div>");

        var gallery = ShowcaseGallery.Parse(project.ShowcaseGallery);
        if (gallery.Count > 0)
        {
            sb.Append("<div class=\"showcase-gallery\">");
            foreach (var image in gallery)
            {
                var src = MediaSrc(image, mainBase);
                sb.Append("<a class=\"showcase-shot\" href=\"").Append(src)
                  .Append("\" target=\"_blank\" rel=\"noopener\"><img src=\"").Append(src)
                  .Append("\" loading=\"lazy\" alt=\"\"></a>");
            }
            sb.Append("</div>");
        }

        if (downloads.Count > 0)
        {
            sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Downloads" : "Скачать").Append("</h2>");
            sb.Append("<div class=\"download-list\">");
            foreach (var b in downloads)
            {
                sb.Append("<a class=\"download-row\" rel=\"noopener\" target=\"_blank\" href=\"")
                  .Append(Html(b.DownloadUrl!)).Append("\">");
                sb.Append("<span class=\"download-version\">").Append(Html(b.Version)).Append("</span>");
                if (b.ReleasedAt is { } released)
                    sb.Append("<span class=\"download-date\">").Append(BlogDateFormatter.DateLocal(released, pageLang)).Append("</span>");
                if (b.Notes is { Length: > 0 } notes)
                    sb.Append("<span class=\"download-notes\">").Append(Html(Shorten(notes))).Append("</span>");
                sb.Append("</a>");
            }
            sb.Append("</div>");
        }

        sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Devlog" : "Девлог").Append("</h2>");
        if (posts.Count == 0)
        {
            sb.Append(en ? "<p class=\"empty\">Nothing published yet.</p>"
                         : "<p class=\"empty\">Пока ничего не опубликовано.</p>");
        }
        else
        {
            sb.Append("<div class=\"post-list\">");
            foreach (var p in posts)
            {
                var excerpt = p.IsPrivate ? "" : Excerpt(p.CedarJson);
                sb.Append("<a class=\"post-card\" href=\"/").Append(p.BlogSlug).Append("\">");
                sb.Append("<div class=\"post-card-meta\"><span class=\"post-card-date\">")
                  .Append(p.BlogPublishedAt is { } cardDate ? BlogDateFormatter.DateLocal(cardDate, pageLang) : "")
                  .Append("</span>");
                if (p.IsPrivate)
                    sb.Append("<span class=\"post-card-locked\">").Append(BlogIcons.Lock).Append("</span>");
                sb.Append("</div>");
                sb.Append("<div class=\"post-card-title\">").Append(Html(p.ArticleTitle ?? p.Title)).Append("</div>");
                if (excerpt.Length > 0)
                    sb.Append("<div class=\"post-card-excerpt\">").Append(Html(excerpt)).Append("</div>");
                sb.Append("</a>");
            }
            sb.Append("</div>");
        }

        sb.Append(RenderFollowForm(ctx, project, en));

        if (roadmap.Count > 0)
        {
            sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Roadmap" : "Роадмап").Append("</h2>");
            sb.Append("<div class=\"roadmap-list\">");
            foreach (var task in roadmap)
            {
                var (label, tone) = task.Status switch
                {
                    TaskStatuses.InProgress => (en ? "In progress" : "В работе", "now"),
                    TaskStatuses.Planned => (en ? "Planned" : "Запланировано", "next"),
                    TaskStatuses.Backlog => (en ? "Someday" : "Когда-нибудь", "later"),
                    _ => (en ? "Done" : "Готово", "done"),
                };
                sb.Append("<div class=\"roadmap-row\"><span class=\"roadmap-status ").Append(tone).Append("\">")
                  .Append(label).Append("</span><span class=\"roadmap-title\">")
                  .Append(Html(task.Title)).Append("</span></div>");
            }
            sb.Append("</div>");
        }

        // A showcase on its own domain has no blog around it to go back to.
        var backLink = project.CustomDomain is { Length: > 0 }
            ? ""
            : $"<a class=\"back-link\" href=\"/\">&larr; {(en ? "All posts" : "Все посты")}</a>";
        var body = $"{backLink}{sb}";

        var blogBase = site.BaseUrl;
        var ogImage = project.CoverUrl is { Length: > 0 } c ? MediaSrc(c, mainBase) : $"{blogBase}/og-default.png";
        var meta = OgMetaBuilder.Build(new OgMetaInput(
            project.Name, project.Description, $"{blogBase}/games/{project.ShowcaseSlug}",
            ogImage, 1200, 630,
            channel?.Title ?? "Cedar Clerk", pageLang,
            [], null, null, null, IsArticle: false), OgMetaPolicy.Full);
        // T-298 — a reader following one game should not have to take the whole blog with it.
        meta += $"<link rel=\"alternate\" type=\"application/rss+xml\" title=\"{Html(project.Name)}\" href=\"{ShowcasePath(project, "/rss.xml")}\">";

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(project.Name, body, pageLang, RenderHeader(channel, pageLang), meta));
    }

    /// <summary>
    /// The project a showcase URL names. Every showcase path resolves it this way, so a page the
    /// owner has switched off is absent from all of them at once.
    /// </summary>
    private static Task<Project?> FindShowcaseAsync(CedarDbContext db, BlogSite site, string slug) =>
        db.Projects.FirstOrDefaultAsync(p =>
            p.ShowcaseSlug == slug && p.OwnerId == site.OwnerId && p.ArchivedAt == null);

    /// <summary>
    /// The devlog feed, with the blog index's visibility rule verbatim (ADR-134): a listed-private
    /// post is a locked card here too, and anything else private is absent.
    /// </summary>
    private static async Task<List<ShowcasePost>> ShowcasePostsAsync(CedarDbContext db, BlogSite site, Guid projectId) =>
        await db.Drafts
            .Where(d => d.OwnerId == site.OwnerId && d.ProjectId == projectId && d.IsBlogPublished
                        && (!d.IsPrivate || d.IsListedWhilePrivate))
            .OrderByDescending(d => d.BlogPublishedAt)
            .Select(d => new ShowcasePost(d.Title, d.ArticleTitle, d.BlogSlug!, d.BlogPublishedAt, d.CedarJson, d.IsPrivate))
            .ToListAsync();

    private sealed record ShowcasePost(string Title, string? ArticleTitle, string BlogSlug,
        DateTime? BlogPublishedAt, string CedarJson, bool IsPrivate);

    /// <summary>
    /// A showcase's own path: the site root when the project answers on a domain of its own (T-300),
    /// <c>/games/{slug}</c> otherwise. Every link the page draws goes through here, so a custom
    /// domain never bounces its reader back onto the subdomain.
    /// </summary>
    private static string ShowcasePath(Project project, string suffix) =>
        project.CustomDomain is { Length: > 0 } ? suffix : $"/games/{project.ShowcaseSlug}{suffix}";

    private static string Html(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string MediaSrc(string path, string mainBase) => Html(path.StartsWith('/') ? mainBase + path : path);

    private static string Shorten(string text) => text.Length <= 140 ? text : text[..139].TrimEnd() + "…";

    /// <summary>
    /// One day's counter for one thing, upserted the way <c>RecordViewGeoAsync</c> is: bump first,
    /// insert only when no row existed, and read the unique index rejecting a racing insert as
    /// "somebody else made the row" rather than as a failure.
    /// </summary>
    private static async Task RecordShowcaseStatAsync(CedarDbContext db, Project project, string kind, string label)
    {
        var day = DateTime.UtcNow.Date;

        Task<int> BumpAsync() => db.ShowcaseStatDailies
            .Where(s => s.ProjectId == project.Id && s.Day == day && s.Kind == kind && s.Label == label)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Count, r => r.Count + 1));

        if (await BumpAsync() > 0) return;

        var row = new ShowcaseStatDaily
        {
            OwnerId = project.OwnerId,
            ProjectId = project.Id,
            Day = day,
            Kind = kind,
            Label = label,
            Count = 1,
        };
        db.ShowcaseStatDailies.Add(row);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            await BumpAsync();
        }
    }

    /// <summary>T-296 — count the click, then send the reader where the pill said.</summary>
    private static async Task RedirectShowcaseLinkAsync(HttpContext ctx, CedarDbContext db, BlogSite site,
        string slug, string rawIndex)
    {
        var project = await FindShowcaseAsync(db, site, slug);
        var links = project is null ? [] : ParseShowcaseLinks(project.ShowcaseLinks);
        if (project is null || !int.TryParse(rawIndex, out var index) || index < 0 || index >= links.Count)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var (label, url) = links[index];
        await RecordShowcaseStatAsync(db, project, ShowcaseStatKinds.LinkClick, label);
        ctx.Response.Redirect(url, permanent: false);
    }

    /// <summary>T-298 — the same items the page lists, for a reader who wants one game's devlog.</summary>
    private static async Task RenderShowcaseRssAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var project = await FindShowcaseAsync(db, site, slug);
        if (project is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // A feed reader never meets the private gate, so a listed-private post is absent here — the
        // same narrowing the blog-wide feed makes against its own index.
        var posts = (await ShowcasePostsAsync(db, site, project.Id))
            .Where(p => !p.IsPrivate)
            .Take(RssItemLimit)
            .ToList();

        var siteUrl = $"{site.BaseUrl}/";
        var feedUrl = $"{site.BaseUrl}/games/{project.ShowcaseSlug}";

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>").Append('\n');
        sb.Append("<rss version=\"2.0\"><channel>");
        sb.Append("<title>").Append(Html(project.Name)).Append("</title>");
        sb.Append("<link>").Append(feedUrl).Append("</link>");
        sb.Append("<description>").Append(Html(project.Description is { Length: > 0 } d ? d : project.Name)).Append("</description>");
        sb.Append("<atom:link xmlns:atom=\"http://www.w3.org/2005/Atom\" href=\"").Append(feedUrl)
          .Append("/rss.xml\" rel=\"self\" type=\"application/rss+xml\" />");

        foreach (var p in posts)
        {
            var url = $"{siteUrl}{p.BlogSlug}";
            var excerpt = Excerpt(p.CedarJson);
            sb.Append("<item>");
            sb.Append("<title>").Append(Html(p.ArticleTitle ?? p.Title)).Append("</title>");
            sb.Append("<link>").Append(url).Append("</link>");
            sb.Append("<guid isPermaLink=\"true\">").Append(url).Append("</guid>");
            if (p.BlogPublishedAt is { } published)
                sb.Append("<pubDate>").Append(published.ToString("R", CultureInfo.InvariantCulture)).Append("</pubDate>");
            if (excerpt.Length > 0)
                sb.Append("<description>").Append(Html(excerpt)).Append("</description>");
            sb.Append("</item>");
        }

        sb.Append("</channel></rss>");
        ctx.Response.ContentType = "application/rss+xml; charset=utf-8";
        await ctx.Response.WriteAsync(sb.ToString());
    }

    /// <summary>
    /// T-294 — the blog index's way into the showcases. Public, unarchived projects only, which is
    /// the same rule the page itself answers by: a strip that listed a switched-off project would
    /// be a row of links to 404s.
    /// </summary>
    private static async Task<string> RenderGamesStripAsync(CedarDbContext db, BlogSite site, string lang)
    {
        var games = await db.Projects
            .Where(p => p.OwnerId == site.OwnerId && p.ShowcaseSlug != null && p.ArchivedAt == null)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Name, p.ShowcaseSlug, p.CoverUrl })
            .ToListAsync();
        if (games.Count == 0) return "";

        var en = lang != Languages.Russian;
        var sb = new StringBuilder();
        sb.Append("<h2 class=\"showcase-section\">").Append(en ? "Games" : "Игры").Append("</h2>");
        sb.Append("<div class=\"games-strip\">");
        foreach (var g in games)
        {
            sb.Append("<a class=\"games-card\" href=\"/games/").Append(g.ShowcaseSlug).Append("\">");
            if (g.CoverUrl is { Length: > 0 } cover)
                sb.Append("<img class=\"games-cover\" src=\"").Append(Html(cover)).Append("\" loading=\"lazy\" alt=\"\">");
            sb.Append("<span class=\"games-name\">").Append(Html(g.Name)).Append("</span></a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// T-294 — the way back from a devlog to the game it is about. Only when that game has a page:
    /// a project without a showcase is working material, and naming it here would publish it.
    /// </summary>
    private static async Task<string> RenderPostGameLinkAsync(CedarDbContext db, BlogSite site, Draft draft, string lang)
    {
        if (draft.ProjectId is not { } projectId) return "";

        var game = await db.Projects
            .Where(p => p.Id == projectId && p.OwnerId == site.OwnerId && p.ShowcaseSlug != null && p.ArchivedAt == null)
            .Select(p => new { p.Name, p.ShowcaseSlug })
            .FirstOrDefaultAsync();
        if (game is null) return "";

        var label = lang == Languages.Russian ? "Девлог игры" : "Devlog of";
        return $"<p class=\"post-game\">{label} <a href=\"/games/{game.ShowcaseSlug}\">{Html(game.Name)}</a></p>";
    }
}
