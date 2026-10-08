using CedarClerk.Localization;
using System.Net;
using CedarClerk.Core;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>The cross-account public commons decided in ADR-243.</summary>
public static class DiscoveryEndpoints
{
    public sealed record Settings(
        bool Enabled, bool ShowScreenshotSaturday, bool ShowProjects, bool ShowBlogs,
        LandingText Title, LandingText Intro);

    public sealed record Item(
        string Kind, string Title, string Summary, string Url, string ImageUrl,
        string Author, string? AvatarUrl, DateTime PublishedAt, IReadOnlyList<string> Tags,
        string Category, string? ProjectName = null, string? ProjectUrl = null,
        bool ScreenshotSaturday = false);

    public sealed record Snapshot(Settings Settings, IReadOnlyList<Item> Projects,
        IReadOnlyList<Item> Blogs, Item? Stage, int OptedInAuthors, int EligibleItems);

    private sealed record OwnerRow(string Id, string TenantUsername, string? AuthorDisplayName, string? AvatarUrl);

    public static void MapDiscoveryEndpoint(this WebApplication app)
    {
        app.MapGet("/discovery", async (HttpContext ctx, CedarDbContext db, IConfiguration cfg) =>
        {
            var ru = ChooseRussian(ctx);
            var snapshot = await LoadAsync(db, cfg);
            var query = (ctx.Request.Query["q"].FirstOrDefault() ?? "").Trim();
            var type = ctx.Request.Query["type"].FirstOrDefault() switch
            {
                "projects" => "projects",
                "devlogs" => "devlogs",
                "blogs" => "blogs",
                _ => "all",
            };
            var category = DiscoveryCategories.Normalize(ctx.Request.Query["category"].FirstOrDefault());
            var hasCategory = ctx.Request.Query.ContainsKey("category");

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = ctx.Request.Query.ContainsKey("shuffle")
                ? "public, max-age=10"
                : "public, max-age=60";
            ctx.Response.Headers.Vary = "Accept-Language";
            await ctx.Response.WriteAsync(Render(ru, snapshot, type, hasCategory ? category : null, query));
        }).AllowAnonymous();
    }

    public static Settings Resolve(DiscoverySettings? row) => new(
        row?.Enabled ?? true,
        row?.ShowScreenshotSaturday ?? true,
        row?.ShowProjects ?? true,
        row?.ShowBlogs ?? true,
        new LandingText(
            Blank(row?.TitleEn) ?? DiscoveryTexts.TitleEn,
            Blank(row?.TitleRu) ?? DiscoveryTexts.TitleRu),
        new LandingText(
            Blank(row?.IntroEn) ?? DiscoveryTexts.IntroEn,
            Blank(row?.IntroRu) ?? DiscoveryTexts.IntroRu));

    public static async Task<Snapshot> LoadAsync(CedarDbContext db, IConfiguration cfg,
        CancellationToken ct = default)
    {
        var settings = Resolve(await db.DiscoverySettings.AsNoTracking().FirstOrDefaultAsync(ct));
        if (!settings.Enabled)
            return new Snapshot(settings, [], [], null, 0, 0);

        var owners = await db.Users.AsNoTracking()
            .Where(u => u.DiscoveryOptIn && u.TenantUsername != null)
            .Select(u => new OwnerRow(u.Id, u.TenantUsername!, u.AuthorDisplayName, u.AvatarUrl))
            .ToListAsync(ct);
        if (owners.Count == 0)
            return new Snapshot(settings, [], [], null, 0, 0);

        var ownerIds = owners.Select(o => o.Id).ToList();
        var hosts = await BlogTenant.HostsForOwnersAsync(db, cfg, ownerIds, ct);
        var ownerById = owners.ToDictionary(o => o.Id, StringComparer.Ordinal);

        var projects = settings.ShowProjects && ProjectEndpoints.IsEnabled(cfg)
            ? await db.Projects.AsNoTracking()
                .Where(p => ownerIds.Contains(p.OwnerId) && p.ShowcaseSlug != null && p.ArchivedAt == null)
                .OrderByDescending(p => p.CreatedAt)
                .Take(120)
                .ToListAsync(ct)
            : [];
        var projectById = projects.ToDictionary(p => p.Id);

        var posts = settings.ShowBlogs || settings.ShowScreenshotSaturday
            ? await db.Drafts.AsNoTracking()
                .Where(d => ownerIds.Contains(d.OwnerId) && d.IsBlogPublished && d.BlogSlug != null && !d.IsPrivate)
                .OrderByDescending(d => d.BlogPublishedAt ?? d.UpdatedAt)
                .Take(180)
                .ToListAsync(ct)
            : [];

        var projectItems = new List<Item>();
        foreach (var project in projects)
        {
            if (!hosts.TryGetValue(project.OwnerId, out var host) || !ownerById.TryGetValue(project.OwnerId, out var owner))
                continue;

            var image = FirstMedia(project.ShowcaseGallery) ?? project.CoverUrl;
            var linked = posts.FirstOrDefault(d => d.ProjectId == project.Id);
            projectItems.Add(new Item(
                "project", project.Name, Limit(project.Description, 190),
                $"https://{host}/showcase/{Uri.EscapeDataString(project.ShowcaseSlug!)}",
                MediaUrl(host, image), DisplayName(owner), Avatar(host, owner.AvatarUrl),
                linked?.BlogPublishedAt ?? project.CreatedAt, [], DiscoveryCategories.Normalize(project.DiscoveryCategory),
                linked is null ? null : linked.ArticleTitle ?? linked.Title,
                linked is null ? null : $"https://{host}/{Uri.EscapeDataString(linked.BlogSlug!)}"));
        }

        var blogItems = new List<Item>();
        foreach (var post in posts)
        {
            if (!hosts.TryGetValue(post.OwnerId, out var host) || !ownerById.TryGetValue(post.OwnerId, out var owner))
                continue;

            var tags = BlogEndpoints.SplitTags(post.Tags);
            var project = post.ProjectId is { } projectId ? projectById.GetValueOrDefault(projectId) : null;
            blogItems.Add(new Item(
                project is null ? "blog" : "devlog",
                post.ArticleTitle ?? post.Title, Excerpt(post.CedarJson),
                $"https://{host}/{Uri.EscapeDataString(post.BlogSlug!)}", MediaUrl(host, post.CoverImagePath),
                DisplayName(owner), Avatar(host, owner.AvatarUrl), post.BlogPublishedAt ?? post.UpdatedAt,
                tags, project is null ? DiscoveryCategories.Other : DiscoveryCategories.Normalize(project.DiscoveryCategory),
                project?.Name,
                project is null ? null : $"https://{host}/showcase/{Uri.EscapeDataString(project.ShowcaseSlug!)}",
                tags.Any(IsScreenshotSaturday)));
        }

        Shuffle(projectItems);
        Shuffle(blogItems);
        var stage = settings.ShowScreenshotSaturday
            ? blogItems.FirstOrDefault(i => i.ScreenshotSaturday && !IsFallback(i.ImageUrl))
                ?? projectItems.FirstOrDefault(i => !IsFallback(i.ImageUrl))
            : projectItems.FirstOrDefault(i => !IsFallback(i.ImageUrl));

        return new Snapshot(settings, projectItems, blogItems, stage, owners.Count,
            projectItems.Count + blogItems.Count);
    }

    internal static string RenderLandingPreview(bool ru, Snapshot snapshot)
    {
        if (!snapshot.Settings.Enabled) return "";

        var projects = snapshot.Projects.Take(2).ToList();
        var blogs = snapshot.Blogs.Where(b => b.Kind == "blog").Take(2).ToList();
        if (projects.Count == 0 && blogs.Count == 0)
        {
            return $"""
                <section id="discover" class="discover-preview discover-empty">
                    <div><span class="stamp">Discovery</span><h2>{E(snapshot.Settings.Title.Pick(ru))}</h2>
                    <p>{E(snapshot.Settings.Intro.Pick(ru))}</p></div>
                    <a class="btn btn-pine" href="/discovery">{DiscoveryTexts.OpenDiscovery(!ru)}</a>
                </section>
                """;
        }

        var cards = projects.Concat(blogs).Select(i => $"""
            <a class="discover-mini" href="{E(i.Url)}">
                <img src="{E(i.ImageUrl)}" alt="" loading="lazy">
                <span><small>{E(KindLabel(i, ru))}</small><b>{E(i.Title)}</b><em>{E(i.Author)}</em></span>
            </a>
            """);
        return $"""
            <section id="discover" class="discover-preview">
                <div class="discover-copy"><span class="stamp">Discovery</span>
                    <h2>{E(snapshot.Settings.Title.Pick(ru))}</h2>
                    <p>{E(snapshot.Settings.Intro.Pick(ru))}</p>
                    <a class="btn btn-pine" href="/discovery">{DiscoveryTexts.ShuffleAndExplore(!ru)}</a>
                </div>
                <div class="discover-minis">{string.Join("", cards)}</div>
            </section>
            """;
    }

    public static string Render(bool ru, Snapshot snapshot, string type, string? category, string query)
    {
        var settings = snapshot.Settings;
        var projectPool = snapshot.Projects.Where(i => Matches(i, query)).ToList();
        var projects = projectPool.Where(i => category is null || i.Category == category).ToList();
        var blogs = snapshot.Blogs.Where(i => Matches(i, query)).ToList();
        if (type == "projects") blogs = [];
        if (type == "blogs")
        {
            projects = [];
            blogs = blogs.Where(i => i.Kind == "blog").ToList();
        }
        if (type == "devlogs")
        {
            projects = [];
            blogs = blogs.Where(i => i.Kind == "devlog").ToList();
        }

        var stage = snapshot.Stage is { } candidate
            && Matches(candidate, query)
            && (type != "projects" || candidate.Kind == "project")
            && (type != "blogs" || candidate.Kind == "blog")
            && (type != "devlogs" || candidate.Kind == "devlog")
            && (candidate.Kind != "project" || category is null || candidate.Category == category)
                ? candidate
                : settings.ShowScreenshotSaturday && type != "projects"
                    ? blogs.FirstOrDefault(i => i.ScreenshotSaturday) ?? projects.FirstOrDefault() ?? blogs.FirstOrDefault()
                    : projects.FirstOrDefault() ?? blogs.FirstOrDefault();
        var projectCards = string.Join("", projects.Take(4).Select((p, index) => ProjectCard(p, ru, index == 0)));
        var blogCards = string.Join("", blogs.Where(b => b.Kind == "blog").Take(3).Select(BlogCard));
        var devlogs = string.Join("", blogs.Where(b => b.Kind == "devlog").Take(4).Select(BlogCard));
        var categoryCards = string.Join("", DiscoveryCategories.All
            .Where(c => projectPool.Any(p => p.Category == c))
            .Select(c => CategoryCard(c, projectPool, ru, query)));
        var langSuffix = ru ? "&lang=ru" : "&lang=en";

        var section = 0;
        string NextSection() => (++section).ToString("00");
        var feedColumns = new List<string>();
        if (settings.ShowBlogs && type != "projects" && blogCards.Length > 0)
        {
            feedColumns.Add($"""
                <div class="feed-col">
                    <p class="column-note">{DiscoveryTexts.EveryItemHereWasSharedPubliclyBy(!ru)}</p>
                    <header class="section-head"><div><span>{NextSection()}</span><h2>{DiscoveryTexts.FromIndependentBlogs(!ru)}</h2></div><a href="/discovery?type=blogs{langSuffix}">{DiscoveryTexts.AllBlogs(!ru)}</a></header>
                    <div class="blog-list">{blogCards}</div>
                </div>
                """);
        }
        if (settings.ShowProjects && type != "blogs" && projectCards.Length > 0)
        {
            feedColumns.Add($"""
                <div class="feed-col">
                    <p class="column-note">{DiscoveryTexts.FreshProjectsFromIndependentMakersAcrossEvery(!ru)}</p>
                    <header class="section-head"><div><span>{NextSection()}</span><h2>Project Showcase</h2></div><a href="/discovery?type=projects{langSuffix}">{DiscoveryTexts.AllProjects(!ru)}</a></header>
                    <div class="project-list">{projectCards}</div>
                </div>
                """);
        }

        var feed = feedColumns.Count == 0 ? "" : $"""
            <section class="feed wrap{(feedColumns.Count == 1 ? " is-single" : "")}">
                {string.Join("", feedColumns)}
            </section>
            """;
        var devlogSection = settings.ShowBlogs && type != "projects" && devlogs.Length > 0
            ? $"""
                <section class="devlogs wrap"><header class="section-head"><div><span>{NextSection()}</span><h2>{DiscoveryTexts.ProjectDevlogs(!ru)}</h2></div></header><div class="devlog-grid">{devlogs}</div></section>
                """
            : "";
        var categorySection = settings.ShowProjects && type != "blogs" && type != "devlogs" && categoryCards.Length > 0
            ? $"""
                <section id="categories" class="categories wrap"><header class="section-head"><div><span>{NextSection()}</span><h2>{DiscoveryTexts.ProjectCategories(!ru)}</h2></div></header><div class="category-grid">{categoryCards}</div></section>
                """
            : "";
        var hasResults = stage is not null || feedColumns.Count > 0 || devlogs.Length > 0 || categorySection.Length > 0;
        var stageHasRail = stage is not null && projects.Any(p => p.Url != stage.Url);
        var globalEmpty = snapshot.EligibleItems == 0;
        var emptyStage = EmptyStage(ru, !globalEmpty);
        var stageContent = hasResults ? Stage(stage, projects, ru,
            settings.ShowScreenshotSaturday && (stage is null || stage.ScreenshotSaturday)) : emptyStage;
        var typeQuery = type == "all" ? "" : $"&type={Uri.EscapeDataString(type)}";
        var categoryQuery = category is null ? "" : $"&category={Uri.EscapeDataString(category)}";
        var searchQuery = string.IsNullOrWhiteSpace(query) ? "" : $"&q={Uri.EscapeDataString(query)}";
        var currentStateQuery = typeQuery + categoryQuery + searchQuery;
        var shuffle = hasResults && projects.Count + blogs.Count > 1
            ? $"""<a class="shuffle" href="/discovery?shuffle={Guid.NewGuid():N}&lang={(ru ? "ru" : "en")}{currentStateQuery}">{Icons.Svg("arrows-clockwise", 16)} {DiscoveryTexts.ShuffleDiscoveries(!ru)}</a>"""
            : "";
        var categoriesNav = settings.ShowProjects && type != "blogs" && type != "devlogs" && categoryCards.Length > 0
            ? $"""<a href="#categories">{DiscoveryTexts.Categories(!ru)}</a>"""
            : "";
        var searchType = type == "all" ? "" : $"""<input type="hidden" name="type" value="{E(type)}">""";
        var searchCategory = category is null ? "" : $"""<input type="hidden" name="category" value="{E(category)}">""";
        var startPublishing = settings.Enabled && globalEmpty
            ? ""
            : $"""<a class="start" href="/welcome#waitlist">{DiscoveryTexts.StartPublishing(!ru)}</a>""";

        var light = DesignTokens.Declarations(DesignTokens.Light, DesignTokens.MaterialsLight);
        var dark = DesignTokens.Declarations(DesignTokens.Dark, DesignTokens.MaterialsDark);
        var title = DiscoveryTexts.DiscoveryIndependentProjectsAndBlogsCedarClerk(!ru);
        var description = settings.Intro.Pick(ru);

        return $"""
            <!doctype html>
            <html lang="{(ru ? "ru" : "en")}">
            <head>
                <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>{E(title)}</title><meta name="description" content="{E(description)}">
                <meta property="og:title" content="{E(title)}"><meta property="og:description" content="{E(description)}">
                <meta property="og:type" content="website"><meta property="og:image" content="{Consts.URLs.MainHost}/og-default.png">
                <link rel="canonical" href="{Consts.URLs.MainHost}/discovery">
                <link rel="alternate" hreflang="en" href="{Consts.URLs.MainHost}/discovery?lang=en">
                <link rel="alternate" hreflang="ru" href="{Consts.URLs.MainHost}/discovery?lang=ru">
                {PublicControls.HeadScript}
                <style>{Css.Replace("{{LIGHT_TOKENS}}", light).Replace("{{DARK_TOKENS}}", dark).Replace("{{FONT_FACES}}", DesignTokens.FontFaces).Replace("{{PC_CSS}}", PublicControls.Css + PublicControls.PageCss)}</style>
            </head>
            <body>
                <header class="topbar">
                    <a class="brand" href="/welcome"><img src="/favicon.png" alt=""><span>Cedar Clerk</span></a>
                    <nav class="main-nav" aria-label="{DiscoveryTexts.PrimaryNavigation(!ru)}">
                        <a href="/welcome?lang={(ru ? "ru" : "en")}">{DiscoveryTexts.Home(!ru)}</a>
                        <a class="active" href="/discovery?lang={(ru ? "ru" : "en")}" aria-current="page">Discovery</a>
                        {categoriesNav}
                    </nav>
                    <div class="pc-controls">{PublicControls.LanguageSwitch(ru, "/discovery?lang=ru" + currentStateQuery, "/discovery?lang=en" + currentStateQuery)}{PublicControls.MenuHtml(ru ? "ru" : "en")}</div>
                    <a class="sign" href="/login">{DiscoveryTexts.SignIn(!ru)}</a>
                    {startPublishing}
                </header>

                {(settings.Enabled ? $"""
                <main data-layout="editorial">
                    <section class="stage{(stageHasRail ? " has-rail" : "")}{(!hasResults ? " is-empty" : "")}">
                        {stageContent}
                        <div class="stage-controls">
                            <form class="search" method="get" action="/discovery">
                                <label for="discovery-search">{DiscoveryTexts.SearchDiscovery(!ru)}</label>
                                <input id="discovery-search" type="search" name="q" value="{E(query)}" placeholder="{DiscoveryTexts.ProjectBlogOrAuthor(!ru)}">
                                <input type="hidden" name="lang" value="{(ru ? "ru" : "en")}">
                                {searchType}
                                {searchCategory}
                            </form>
                            <nav class="segments" aria-label="{DiscoveryTexts.ContentType(!ru)}">
                                {Segment("all", type, DiscoveryTexts.All(!ru), ru, query)}
                                {Segment("projects", type, DiscoveryTexts.ProjectsHeading(!ru), ru, query)}
                                {Segment("devlogs", type, DiscoveryTexts.Devlogs(!ru), ru, query)}
                                {Segment("blogs", type, DiscoveryTexts.Blogs(!ru), ru, query)}
                            </nav>
                            {shuffle}
                        </div>
                    </section>

                    <h1 class="sr-only">{E(settings.Title.Pick(ru))}</h1>
                    {feed}
                    {devlogSection}
                    {categorySection}
                </main>
                """ : Disabled(ru))}

                <footer><div class="wrap footer-inner"><a class="brand" href="/welcome"><img src="/favicon.png" alt=""><span>Cedar Clerk</span></a><span>{DiscoveryTexts.PublishingForIndependentMakers(!ru)}</span><span class="grow"></span><a href="/terms">{DiscoveryTexts.Terms(!ru)}</a><a href="/privacy">{DiscoveryTexts.Privacy(!ru)}</a></div></footer>
                {PublicControls.Script}
            </body></html>
            """;
    }

    private static string Stage(Item? stage, IReadOnlyList<Item> projects, bool ru, bool isSaturday)
    {
        var eyebrow = isSaturday ? $"{Icons.Svg("tree-evergreen", 14)} SATURDAY STAGE" : DiscoveryTexts.FEATURED(!ru);
        var heading = isSaturday ? "#ScreenshotSaturday" : DiscoveryTexts.IndependentWorkOfTheWeek(!ru);
        if (stage is null)
        {
            return $"""
                <div class="stage-empty"><span class="eyebrow">{eyebrow}</span><h2>{heading}</h2>
                <p>{DiscoveryTexts.FeedDescription(!ru)}</p></div>
                """;
        }

        var rail = projects.Where(p => p.Url != stage.Url).Take(4).ToList();
        var stageText = isSaturday
            ? DiscoveryTexts.SaturdayDescription(!ru)
            : stage.Summary;
        var avatar = stage.AvatarUrl is null ? "" : $"<img class=\"stage-avatar\" src=\"{E(stage.AvatarUrl)}\" alt=\"\">";
        return $"""
            <article class="stage-feature">
                <img src="{E(stage.ImageUrl)}" alt="" fetchpriority="high">
                <div class="stage-shade"></div><div class="stage-copy">
                    <span class="eyebrow">{eyebrow}</span><h2>{heading}</h2>
                    <p>{E(stageText)}</p>
                    <div class="stage-by">{avatar}<span>{E(stage.Author)} <i>·</i> {E(KindLabel(stage, ru))}</span></div>
                    <a href="{E(stage.Url)}">{DiscoveryTexts.SeeThisStage(!ru)} {Icons.Svg("arrow-right", 16)}</a>
                </div>
            </article>
            <aside class="stage-rail" aria-label="{DiscoveryTexts.MoreCategories(!ru)}">
                {string.Join("", rail.Select(p => $"""<a href="{E(p.Url)}"><img src="{E(p.ImageUrl)}" alt=""><span><small>{E(CategoryLabel(p.Category, ru))}</small><b>{E(p.Title)}</b></span></a>"""))}
            </aside>
            """;
    }

    private static string ProjectCard(Item item, bool ru, bool featured)
    {
        return $"""
            <article class="project-card{(featured ? " featured" : "")}">
                <a class="project-image" href="{E(item.Url)}"><img src="{E(item.ImageUrl)}" alt="" loading="lazy"><span>{E(CategoryLabel(item.Category, ru))}</span></a>
                <div class="project-body"><div class="project-meta">{E(item.Author)} · {item.PublishedAt:MMM yyyy}</div>
                    <h3><a href="{E(item.Url)}">{E(item.Title)}</a></h3><p>{E(item.Summary)}</p>
                    {(item.ProjectUrl is null ? "" : $"""<a class="linked" href="{E(item.ProjectUrl)}">{DiscoveryTexts.LatestDevlog(!ru)}: {E(item.ProjectName ?? "")}</a>""")}
                </div>
            </article>
            """;
    }

    private static string BlogCard(Item item) => $"""
        <article class="blog-card"><a class="blog-thumb" href="{E(item.Url)}"><img src="{E(item.ImageUrl)}" alt="" loading="lazy"></a>
            <div><div class="project-meta">{E(item.Author)} · {item.PublishedAt:dd MMM yyyy}</div><h3><a href="{E(item.Url)}">{E(item.Title)}</a></h3>
            <p>{E(item.Summary)}</p><div class="tag-row">{string.Join("", item.Tags.Take(3).Select(t => $"<span>#{E(t.TrimStart('#'))}</span>"))}</div></div>
        </article>
        """;

    private static string CategoryCard(string category, IReadOnlyList<Item> projects, bool ru, string query)
    {
        var example = projects.FirstOrDefault(p => p.Category == category);
        var count = projects.Count(p => p.Category == category);
        var queryPart = string.IsNullOrWhiteSpace(query) ? "" : "&q=" + Uri.EscapeDataString(query);
        return $"""
            <a class="category-card" href="/discovery?type=projects&category={category}&lang={(ru ? "ru" : "en")}{queryPart}">
                <img src="{E(example?.ImageUrl ?? "/og-default.png")}" alt="" loading="lazy">
                <span><b>{E(CategoryLabel(category, ru))}</b><small>{count}</small></span>
            </a>
            """;
    }

    private static string Segment(string value, string selected, string label, bool ru, string query)
    {
        var queryPart = string.IsNullOrWhiteSpace(query) ? "" : "&q=" + Uri.EscapeDataString(query);
        return $"<a href=\"/discovery?type={value}&lang={(ru ? "ru" : "en")}{queryPart}\"{(selected == value ? " aria-current=\"page\"" : "")}>{E(label)}</a>";
    }

    private static string EmptyStage(bool ru, bool filtered)
    {
        var heading = filtered
            ? (DiscoveryTexts.NothingMatchesThisView(!ru))
            : (DiscoveryTexts.EmptyDescription(!ru));
        var copy = filtered
            ? (DiscoveryTexts.ClearTheSearchAndFiltersTheWork(!ru))
            : (DiscoveryTexts.PrivacyDescription(!ru));
        var href = filtered ? $"/discovery?lang={(ru ? "ru" : "en")}" : "/welcome#waitlist";
        var action = filtered ? (DiscoveryTexts.ClearFilters(!ru))
            : (DiscoveryTexts.PublishTheFirstProject(!ru));
        return $"""
            <div class="stage-empty discovery-empty"><span class="eyebrow">DISCOVERY</span><h2>{heading}</h2>
            <p>{copy}</p><a class="stage-empty-action" href="{href}">{action} {Icons.Svg("arrow-right", 16)}</a></div>
            """;
    }

    private static string Disabled(bool ru) => $"""
        <main class="disabled wrap" data-layout="editorial"><img src="/og-default.png" alt=""><span class="eyebrow">DISCOVERY</span>
        <h1>{(DiscoveryTexts.DiscoveryIsTakingShortPause(!ru))}</h1>
        <p>{(DiscoveryTexts.BlogsAndShowcasesRemainAvailableAtTheir(!ru))}</p>
        <a class="start" href="/welcome">{(DiscoveryTexts.BackToCedarClerk(!ru))}</a></main>
        """;

    private static bool Matches(Item item, string query) => string.IsNullOrWhiteSpace(query)
        || new[] { item.Title, item.Summary, item.Author, item.ProjectName ?? "", string.Join(" ", item.Tags) }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string CategoryLabel(string value, bool ru) => value switch
    {
        DiscoveryCategories.Games => DiscoveryTexts.Games(!ru),
        DiscoveryCategories.AppsTools => DiscoveryTexts.AppsTools(!ru),
        DiscoveryCategories.ComicsArt => DiscoveryTexts.ComicsArt(!ru),
        DiscoveryCategories.FilmAnimation => DiscoveryTexts.FilmAnimation(!ru),
        DiscoveryCategories.MusicAudio => DiscoveryTexts.MusicAudio(!ru),
        DiscoveryCategories.Hardware => DiscoveryTexts.Hardware(!ru),
        _ => DiscoveryTexts.Other(!ru),
    };

    private static string KindLabel(Item item, bool ru) => item.Kind switch
    {
        "project" => CategoryLabel(item.Category, ru),
        "devlog" => DiscoveryTexts.Devlog(!ru),
        _ => DiscoveryTexts.IndependentBlog(!ru),
    };

    private static string DisplayName(OwnerRow owner) =>
        Blank(owner.AuthorDisplayName) ?? $"@{owner.TenantUsername}";

    private static string? Avatar(string host, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : MediaUrl(host, path);

    private static string MediaUrl(string host, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/og-default.png";
        if (MediaFileNames.TryParse(path, out _)) path = "/media/" + path;
        else if (path.StartsWith("media/", StringComparison.Ordinal)) path = "/" + path;
        return path.StartsWith("/", StringComparison.Ordinal) ? $"https://{host}{path}" : path;
    }

    private static string? FirstMedia(string value) => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    private static bool IsFallback(string path) => path == "/og-default.png";
    private static bool IsScreenshotSaturday(string tag) => tag.Trim().TrimStart('#').Equals("ScreenshotSaturday", StringComparison.OrdinalIgnoreCase);
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string Excerpt(string cedarJson)
    {
        try { return Limit(string.Join(" ", CedarPlainText.Paragraphs(cedarJson)), 220); }
        catch { return ""; }
    }

    private static string Limit(string? value, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length <= max) return text;
        var cut = text[..max];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut) + "…";
    }

    private static void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static bool ChooseRussian(HttpContext ctx) => ctx.Request.Query["lang"].ToString() switch
    {
        "ru" => true,
        "en" => false,
        _ => ctx.Request.Headers.AcceptLanguage.ToString().StartsWith("ru", StringComparison.OrdinalIgnoreCase),
    };

    private const string Css = """
        :root { color-scheme: light dark; {{LIGHT_TOKENS}} }
        @media (prefers-color-scheme: dark) { :root { {{DARK_TOKENS}} } }
        :root[data-theme="light"] { {{LIGHT_TOKENS}} }
        :root[data-theme="dark"] { {{DARK_TOKENS}} }
        /* ADR-321 — dark Discovery. The page stands on the bare wall, which at night is dark while
           paper stays cream, so text on the wall takes the wall's ink and text on a paper surface
           keeps the paper's: the same two polarities the app shell carries (ADR-141). */
        :root { --paper-ink: var(--text); --paper-ink-2: var(--t2); --paper-ink-3: var(--t3); }
        :root[data-theme="dark"] body { --text: var(--wood-ink); --t2: var(--wood-ink-soft); --t3: var(--wood-ink-soft); }
        @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) body { --text: var(--wood-ink); --t2: var(--wood-ink-soft); --t3: var(--wood-ink-soft); } }
        :is(.topbar, .search input, .project-card.featured, .segments a[aria-current], .stage-empty-action, .reading-menu) { --text: var(--paper-ink); --t2: var(--paper-ink-2); --t3: var(--paper-ink-3); color: var(--text); }
        {{FONT_FACES}}
        {{PC_CSS}}
        * { box-sizing: border-box; }
        .sr-only { position:absolute!important; width:1px!important; height:1px!important; padding:0!important; margin:-1px!important; overflow:hidden!important; clip:rect(0,0,0,0)!important; white-space:nowrap!important; border:0!important; }
        html { scroll-behavior: smooth; }
        body { min-height:100vh; margin:0; display:flex; flex-direction:column; background:var(--canvas); color:var(--text); font-family:var(--font-sans); line-height:1.5; }
        main[data-layout="editorial"] { flex:1 0 auto; }
        a { color:inherit; }
        img { display:block; max-width:100%; }
        .wrap { width:min(1380px, calc(100% - 40px)); margin:0 auto; }
        .grow { flex:1; }
        .topbar { position:sticky; top:0; z-index:20; min-height:68px; padding:10px max(20px, calc((100vw - 1380px)/2)); display:flex; align-items:center; gap:20px; background:var(--paper-bright); border-bottom:1px solid var(--rule-ink-soft); box-shadow:var(--shadow-paper-sm); }
        .brand { display:flex; align-items:center; gap:9px; font-family:var(--font-display); font-weight:700; text-decoration:none; white-space:nowrap; }
        .brand img { width:28px; height:28px; object-fit:contain; }
        .main-nav { display:flex; align-items:center; gap:20px; font-size:13px; }
        .main-nav a { text-decoration:none; color:var(--t2); padding:8px 0; border-bottom:2px solid transparent; }
        .main-nav a:hover,.main-nav a.active { color:var(--accent); border-color:var(--accent); }
        .search { flex:1 1 360px; max-width:560px; min-width:280px; display:grid; grid-template-columns:auto minmax(180px,1fr); align-items:center; gap:9px; }
        .search label { color:var(--brass); font:700 11px var(--font-sans); white-space:nowrap; }
        .search input { width:100%; height:38px; border:var(--border-paper); border-radius:var(--radius-field); background:var(--sheet); color:var(--text); padding:0 12px; font:inherit; font-size:13px; box-shadow:var(--shadow-field-inset); }
        .search input:focus-visible,.segments a:focus-visible,.shuffle:focus-visible,.stage-empty-action:focus-visible,.start:focus-visible { outline:2px solid var(--focus-halo); outline-offset:2px; }
        .topbar .pc-controls { margin-left:auto; }
        .sign { font-size:13px; text-decoration:none; white-space:nowrap; }
        .start { display:inline-flex; align-items:center; justify-content:center; min-height:40px; padding:8px 15px; color:var(--text-on-pine); background:var(--grad-pine); border:1px solid var(--pine-deep); border-radius:var(--radius-plaque); font-weight:700; font-size:13px; text-decoration:none; box-shadow:var(--shadow-pine-btn); white-space:nowrap; }
        .stage { display:grid; grid-template-columns:minmax(0,1fr); background:var(--pine-deep); color:var(--text-on-pine); min-height:330px; }
        .stage.has-rail { grid-template-columns:minmax(0,1fr) minmax(250px,300px); }
        .stage-feature { grid-column:1; display:grid; grid-template-columns:minmax(0,1.08fr) minmax(300px,1fr); min-height:270px; overflow:hidden; }
        .stage-feature>img { width:100%; height:100%; min-height:270px; object-fit:cover; }
        .stage-shade { display:none; }
        .stage-copy { align-self:center; max-width:590px; padding:28px 34px; }
        .eyebrow { display:block; font:700 11px var(--font-mono); letter-spacing:.16em; text-transform:uppercase; color:var(--brass); }
        .eyebrow svg { vertical-align:-2px; margin-right:6px; }
        .stage-copy h2 { margin:10px 0 18px; padding-bottom:13px; border-bottom:1px solid color-mix(in srgb,var(--brass) 55%,transparent); font:700 clamp(30px,4vw,48px)/1 var(--font-display); }
        .stage-copy p { margin:0 0 18px; font:17px var(--font-serif); }
        .stage-by { display:flex; align-items:center; gap:10px; margin:0 0 18px; font-size:13px; }
        .stage-by i { color:var(--brass); font-style:normal; padding:0 4px; }
        .stage-avatar { width:38px; height:38px; border-radius:50%; object-fit:cover; border:1px solid var(--brass); }
        .stage-copy>a { display:inline-flex; align-items:center; gap:12px; min-height:42px; padding:9px 16px; color:#fff; background:linear-gradient(180deg,#d5a245,#b97b21); border:1px solid #dfb763; border-radius:var(--radius-plaque); box-shadow:0 2px 0 #6e4618; font-weight:700; text-decoration:none; }
        .stage-rail { padding:14px; display:flex; flex-direction:column; gap:8px; border-left:1px solid rgba(255,255,255,.13); background:color-mix(in srgb,var(--pine-deep) 85%,black); }
        .stage-rail a { display:grid; grid-template-columns:76px minmax(0,1fr); gap:10px; min-height:64px; padding:6px; color:var(--text-on-pine); text-decoration:none; border:1px solid rgba(255,255,255,.13); border-radius:var(--radius-paper); background:rgba(255,255,255,.045); }
        .stage-rail img { width:76px; height:54px; object-fit:cover; border-radius:calc(var(--radius-paper) - 2px); }
        .stage-rail span { align-self:center; min-width:0; }
        .stage-rail small { display:block; color:var(--brass); font:9px var(--font-mono); text-transform:uppercase; letter-spacing:.08em; }
        .stage-rail b { display:block; margin-top:4px; font:14px var(--font-display); overflow:hidden; text-overflow:ellipsis; }
        .stage-empty { grid-column:1/-1; align-self:center; justify-self:center; text-align:center; max-width:680px; padding:44px 24px; }
        .stage-empty h2 { margin:10px 0; font:700 clamp(32px,5vw,54px)/1 var(--font-display); }
        .stage-empty p { margin:0 auto 20px; max-width:58ch; }
        .stage-empty-action { display:inline-flex; align-items:center; gap:9px; min-height:42px; padding:9px 16px; border:1px solid var(--brass); border-radius:var(--radius-plaque); background:var(--paper-bright); color:var(--accent); font-weight:700; text-decoration:none; }
        .stage-controls { grid-column:1/-1; display:flex; align-items:center; justify-content:flex-start; gap:12px; min-height:60px; padding:10px max(24px,calc((100vw - 1380px)/2)); border-top:1px solid rgba(255,255,255,.14); }
        .segments { display:flex; padding:4px; border:1px solid rgba(255,255,255,.18); border-radius:var(--radius-plaque); }
        .segments a { min-height:34px; padding:8px 18px; border-radius:calc(var(--radius-plaque) - 2px); text-decoration:none; font-size:13px; color:color-mix(in srgb,var(--text-on-pine) 70%,transparent); }
        .segments a[aria-current] { background:var(--paper-bright); color:var(--accent); }
        .shuffle { display:flex; align-items:center; gap:8px; color:var(--text-on-pine); text-decoration:none; font-size:13px; }
        .feed { display:grid; grid-template-columns:1fr 1fr; gap:0; padding:18px 0 24px; }
        .feed.is-single { grid-template-columns:minmax(0,760px); }
        .feed.is-single .feed-col { padding-inline:0; border-right:0; }
        .feed-col { min-width:0; }
        .feed-col:first-child { padding-right:48px; border-right:1px solid var(--rule-ink); }
        .feed-col:last-child { padding-left:48px; }
        .column-note { min-height:20px; margin:0 0 7px; color:var(--t2); font-size:12px; }
        .section-head { display:flex; align-items:flex-end; justify-content:space-between; gap:18px; padding-bottom:12px; margin-bottom:18px; border-bottom:1px solid var(--rule-ink); }
        .section-head>div { display:flex; align-items:baseline; gap:12px; }
        .section-head span { color:var(--accent); font:11px var(--font-mono); }
        .section-head h2 { margin:0; font:700 25px var(--font-display); }
        .section-head>a { font-size:12px; color:var(--accent); text-decoration:none; }
        .blog-list,.project-list { display:flex; flex-direction:column; gap:14px; }
        .blog-card { display:grid; grid-template-columns:minmax(170px,38%) minmax(0,1fr); gap:16px; padding:0 0 10px; border-bottom:1px solid var(--rule-ink-soft); }
        .blog-thumb img { width:100%; height:116px; object-fit:cover; border-radius:var(--radius-paper); border:var(--border-paper); }
        .blog-card h3,.project-card h3 { margin:3px 0 5px; font:700 18px/1.2 var(--font-display); }
        .blog-card h3 a,.project-card h3 a { text-decoration:none; }
        .blog-card p,.project-card p { margin:0; color:var(--t2); font-size:13px; display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; overflow:hidden; }
        .project-meta { color:var(--t3); font:10px var(--font-mono); text-transform:uppercase; letter-spacing:.04em; }
        .tag-row { display:flex; gap:5px; flex-wrap:wrap; margin-top:8px; }
        .tag-row span { padding:2px 6px; background:var(--asoft); color:var(--accent); border-radius:var(--radius-stamp); font:9px var(--font-mono); }
        .project-card { display:grid; grid-template-columns:64px minmax(0,1fr); min-height:70px; padding:8px 0; background:transparent; border:0; border-bottom:1px solid var(--rule-ink-soft); border-radius:0; overflow:hidden; box-shadow:none; }
        .project-card.featured { grid-template-columns:230px minmax(0,1fr); min-height:220px; }
        .project-card.featured { padding:8px; background:var(--sheet); border:var(--border-paper); border-radius:var(--radius-paper); box-shadow:var(--shadow-paper-sm); }
        .project-image { position:relative; min-height:54px; }
        .project-image img { width:100%; height:100%; object-fit:cover; }
        .project-image span { position:absolute; left:10px; top:10px; padding:3px 7px; background:rgba(16,38,25,.86); color:#fff; border-radius:var(--radius-stamp); font:9px var(--font-mono); }
        .project-card:not(.featured) .project-image span { display:none; }
        .project-card:not(.featured) .project-body { padding:2px 12px; }
        .project-card:not(.featured) .project-body>p { display:none; }
        .project-body { padding:18px; align-self:center; }
        .linked { display:block; margin-top:12px; color:var(--accent); font-size:11px; text-decoration:none; }
        .devlogs,.categories { padding:46px 0 24px; }
        .devlog-grid { display:grid; grid-template-columns:1fr 1fr; gap:18px 32px; }
        .category-grid { display:grid; grid-template-columns:repeat(4,1fr); gap:14px; }
        .category-card { position:relative; min-height:150px; overflow:hidden; border-radius:var(--radius-paper); border:var(--border-paper); color:#fff; text-decoration:none; }
        .category-card:after { content:""; position:absolute; inset:0; background:linear-gradient(transparent 30%,rgba(8,20,13,.88)); }
        .category-card img { width:100%; height:150px; object-fit:cover; }
        .category-card span { position:absolute; z-index:1; left:14px; right:14px; bottom:12px; display:flex; align-items:end; gap:8px; }
        .category-card b { font:700 15px var(--font-display); }
        .category-card small { margin-left:auto; font:10px var(--font-mono); color:#e3c784; }
        .disabled { min-height:70vh; display:flex; flex-direction:column; align-items:center; justify-content:center; text-align:center; }
        .disabled img { width:180px; height:110px; object-fit:cover; border-radius:var(--radius-paper); margin-bottom:20px; }
        .disabled h1 { font:700 40px var(--font-display); margin:8px 0; }
        footer { margin-top:64px; color:var(--rail-ink); background:var(--rail-mid); background-image:var(--tex-wood),var(--surface-rail); border-top:2px solid var(--rail-edge); }
        .footer-inner { min-height:88px; display:flex; align-items:center; gap:22px; font-size:12px; }
        .footer-inner>a:not(.brand) { color:var(--rail-ink-soft); text-decoration:none; }
        @media(max-width:1050px){ .main-nav{display:none}.stage.has-rail{grid-template-columns:1fr 250px}.stage-feature{grid-template-columns:1fr}.stage-feature>img{height:230px;min-height:230px}.stage-copy{padding:26px}.feed-col:first-child{padding-right:28px}.feed-col:last-child{padding-left:28px}.category-grid{grid-template-columns:repeat(3,1fr)} }
        @media(max-width:760px){ .topbar{gap:10px;flex-wrap:wrap}.sign{margin-left:auto}.topbar .lang{display:none}.stage.has-rail{grid-template-columns:1fr}.stage-feature{grid-column:1}.stage-rail{display:grid;grid-template-columns:1fr 1fr;border-left:0}.stage-controls{flex-wrap:wrap}.search{flex-basis:100%;max-width:none}.feed,.feed.is-single{grid-template-columns:1fr}.feed-col:first-child{padding-right:0;border-right:0}.feed-col:last-child{padding-left:0;padding-top:38px}.devlog-grid{grid-template-columns:1fr}.category-grid{grid-template-columns:1fr 1fr}.footer-inner{flex-wrap:wrap;padding:22px 0}.footer-inner .grow{display:none}.project-card.featured{grid-template-columns:135px 1fr} }
        @media(max-width:480px){ .brand span{display:none}.start{padding:8px 10px}.search{min-width:0;grid-template-columns:1fr}.search label{white-space:normal}.stage-rail{grid-template-columns:1fr}.segments{width:100%}.segments a{flex:1;text-align:center;padding-inline:10px}.shuffle{width:100%;justify-content:center}.blog-card{grid-template-columns:96px 1fr}.blog-thumb img{width:96px;height:88px}.project-card.featured{grid-template-columns:1fr}.project-card.featured .project-image{height:180px}.wrap{width:min(100% - 28px,1380px)} }
        @media(max-width:340px){.category-grid{grid-template-columns:1fr}}
        @media(prefers-reduced-motion:reduce){html{scroll-behavior:auto}}
        """;
}
