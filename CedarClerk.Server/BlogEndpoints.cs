using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class BlogEndpoints
{
    private const int CommentMaxLength = 2000;
    private const int AuthorNameMaxLength = 60;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private record ReactRequest(string? AnnotationId, string Kind);
    private record CommentRequest(string? AnnotationId, string? AuthorName, string Text);

    public static void MapBlogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/drafts").RequireAuthorization();

        group.MapPost("/{id:guid}/publish-blog", async (Guid id, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            if (!draft.IsBlogPublished || draft.BlogSlug is null)
                draft.BlogSlug = await GenerateUniqueSlugAsync(db, draft.Id, draft.Title);
            
            draft.BlogPublishedAt ??= DateTime.UtcNow;
            draft.IsBlogPublished = true;
            await db.SaveChangesAsync();

            var blogHost = cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;
            return Results.Ok(new { slug = draft.BlogSlug, url = $"https://{blogHost}/{draft.BlogSlug}" });
        });

        group.MapPost("/{id:guid}/unpublish-blog", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.IsBlogPublished = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        
        group.MapGet("/{id:guid}/comments", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var comments = await db.Comments.Where(c => c.DraftId == id)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.AnnotationId, c.AuthorName, c.Text, c.CreatedAt })
                .ToListAsync();
            
            var reactionCounts = await db.Reactions.Where(r => r.DraftId == id)
                .GroupBy(r => r.Kind)
                .Select(g => new { Kind = g.Key, Count = g.Count() })
                .ToListAsync();

            return Results.Ok(new
            {
                reactions = new
                {
                    likes = reactionCounts.FirstOrDefault(r => r.Kind == "like")?.Count ?? 0,
                    dislikes = reactionCounts.FirstOrDefault(r => r.Kind == "dislike")?.Count ?? 0,
                },
                comments,
            });
        });

        var commentsGroup = app.MapGroup("/api/comments").RequireAuthorization();
        commentsGroup.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id);
            if (comment is null) return Results.NotFound();

            var owns = await db.Drafts.AnyAsync(d => d.Id == comment.DraftId && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            db.Comments.Remove(comment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<string> GenerateUniqueSlugAsync(CedarDbContext db, Guid draftId, string title)
    {
        var baseSlug = SlugGenerator.Slugify(title);
        var candidate = baseSlug;
        var n = 2;
        while (await db.Drafts.AnyAsync(d => d.BlogSlug == candidate && d.Id != draftId))
        {
            candidate = $"{baseSlug}-{n}";
            n++;
        }
        return candidate;
    }

    public static async Task HandleRequest(HttpContext ctx)
    {
        var db = ctx.RequestServices.GetRequiredService<CedarDbContext>();
        var path = ctx.Request.Path.Value?.Trim('/') ?? "";
        var segments = path.Length == 0 ? [] : path.Split('/');

        if (segments is ["api", "posts", var slug, var action])
        {
            if (action == "annotations" && ctx.Request.Method == HttpMethods.Get)
                await GetAnnotationsAsync(ctx, db, slug);
            else if (action == "react" && ctx.Request.Method == HttpMethods.Post)
                await PostReactionAsync(ctx, db, slug);
            else if (action == "comments" && ctx.Request.Method == HttpMethods.Post)
                await PostCommentAsync(ctx, db, slug);
            else
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (ctx.Request.Method != HttpMethods.Get)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (segments.Length == 0)
        {
            await RenderIndexAsync(ctx, db);
            return;
        }

        if (segments.Length == 1)
        {
            await RenderPostAsync(ctx, db, segments[0]);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static string VisitorHash(HttpContext ctx)
    {
        var ip = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip + ":" + Consts.General.VisitorHashSalt)));
    }

    private static async Task GetAnnotationsAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var visitor = VisitorHash(ctx);
        var reactions = await db.Reactions.Where(r => r.DraftId == draft.Id).ToListAsync();
        var comments = await db.Comments.Where(c => c.DraftId == draft.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        object BuildGroup(string? annotationId)
        {
            var group = reactions.Where(r => r.AnnotationId == annotationId).ToList();
            var counts = group.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
            var myVote = group.FirstOrDefault(r => r.VisitorHash == visitor)?.Kind;
            var groupComments = comments.Where(c => c.AnnotationId == annotationId)
                .Select(c => new { c.Id, authorName = DisplayName(c.AuthorName), c.Text, c.CreatedAt });
            return new { counts, myVote, comments = groupComments };
        }

        var annotationIds = reactions.Select(r => r.AnnotationId)
            .Concat(comments.Select(c => c.AnnotationId))
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        var result = new
        {
            article = BuildGroup(null),
            annotations = annotationIds.ToDictionary(id => id!, id => BuildGroup(id)),
        };

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, result, JsonOpts);
    }

    private static async Task PostReactionAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ReactRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<ReactRequest>(ctx.Request.Body, JsonOpts);
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (req is null || string.IsNullOrWhiteSpace(req.Kind))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var annotationId = string.IsNullOrEmpty(req.AnnotationId) ? null : req.AnnotationId;
        var visitor = VisitorHash(ctx);

        var existing = await db.Reactions.FirstOrDefaultAsync(r =>
            r.DraftId == draft.Id && r.AnnotationId == annotationId && r.VisitorHash == visitor);

        if (existing is null)
            db.Reactions.Add(new Reaction { DraftId = draft.Id, AnnotationId = annotationId, Kind = req.Kind, VisitorHash = visitor });
        else if (existing.Kind == req.Kind)
            db.Reactions.Remove(existing);
        else
            existing.Kind = req.Kind;
        await db.SaveChangesAsync();

        var group = await db.Reactions.Where(r => r.DraftId == draft.Id && r.AnnotationId == annotationId).ToListAsync();
        var counts = group.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
        var myVote = group.FirstOrDefault(r => r.VisitorHash == visitor)?.Kind;

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new { counts, myVote }, JsonOpts);
    }

    private static async Task PostCommentAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        CommentRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<CommentRequest>(ctx.Request.Body, JsonOpts);
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var text = req?.Text?.Trim() ?? "";
        if (text.Length == 0 || text.Length > CommentMaxLength)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var authorName = req?.AuthorName?.Trim();
        if (authorName is { Length: > AuthorNameMaxLength })
            authorName = authorName[..AuthorNameMaxLength];

        var annotationId = string.IsNullOrEmpty(req?.AnnotationId) ? null : req.AnnotationId;

        var comment = new Comment { DraftId = draft.Id, AnnotationId = annotationId, AuthorName = authorName, Text = text };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = StatusCodes.Status201Created;
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new
        {
            comment.Id,
            authorName = DisplayName(comment.AuthorName),
            comment.Text,
            comment.CreatedAt,
        }, JsonOpts);
    }

    private static string DisplayName(string? authorName) =>
        string.IsNullOrWhiteSpace(authorName) ? "Anonymous" : authorName;

    private static List<string> SplitTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string TagFilterUrl(IEnumerable<string> tags)
    {
        var list = tags.Distinct().ToList();
        return list.Count == 0 ? "/" : "/?tags=" + string.Join(",", list.Select(Uri.EscapeDataString));
    }

    private static async Task RenderIndexAsync(HttpContext ctx, CedarDbContext db)
    {
        var posts = await db.Drafts.Where(d => d.IsBlogPublished)
            .OrderByDescending(d => d.BlogPublishedAt)
            .Select(d => new
            {
                d.Title, d.BlogSlug, d.BlogPublishedAt, d.Tags,
                TranslationLanguages = db.DraftTranslations.Where(t => t.DraftId == d.Id).Select(t => t.Language).ToList(),
            })
            .ToListAsync();

        var allTags = posts.SelectMany(p => SplitTags(p.Tags)).Distinct().OrderBy(t => t).ToList();
        var selectedTags = (ctx.Request.Query["tags"].FirstOrDefault() ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(allTags.Contains)
            .Distinct()
            .ToList();

        var filtered = selectedTags.Count == 0
            ? posts
            : posts.Where(p => { var pt = SplitTags(p.Tags); return selectedTags.All(pt.Contains); }).ToList();

        var sb = new StringBuilder();

        if (allTags.Count > 0)
        {
            sb.Append("<div class=\"tag-bar\">");
            foreach (var tag in allTags)
            {
                var isSelected = selectedTags.Contains(tag);
                var toggled = isSelected ? selectedTags.Where(t => t != tag) : selectedTags.Append(tag);
                sb.Append("<a class=\"tag-chip").Append(isSelected ? " selected" : "").Append("\" href=\"")
                  .Append(TagFilterUrl(toggled)).Append("\">#")
                  .Append(System.Net.WebUtility.HtmlEncode(tag)).Append(isSelected ? " &times;" : "").Append("</a>");
            }
            sb.Append("</div>");
        }

        if (filtered.Count == 0)
        {
            sb.Append(posts.Count == 0
                ? "<p class=\"empty\">Nothing published yet.</p>"
                : "<p class=\"empty\">No posts match the selected tags.</p>");
        }
        else
        {
            static DateTime MonthOf(DateTime? dt) => new((dt ?? DateTime.MinValue).Year, (dt ?? DateTime.MinValue).Month, 1);
            foreach (var monthGroup in filtered.GroupBy(p => MonthOf(p.BlogPublishedAt)).OrderByDescending(g => g.Key))
            {
                sb.Append("<h2 class=\"timeline-month\">")
                  .Append(monthGroup.Key.ToString("MMMM yyyy", CultureInfo.InvariantCulture))
                  .Append("</h2><ul class=\"post-list\">");

                foreach (var p in monthGroup)
                {
                    sb.Append("<li><a href=\"/").Append(p.BlogSlug).Append("\">")
                      .Append(System.Net.WebUtility.HtmlEncode(p.Title)).Append("</a> <time>")
                      .Append(p.BlogPublishedAt?.ToString("d MMM", CultureInfo.InvariantCulture) ?? "").Append("</time>");

                    sb.Append(" <span class=\"lang-badges\"><a class=\"lang-badge\" href=\"/").Append(p.BlogSlug).Append("\">RU</a>");
                    foreach (var lang in p.TranslationLanguages.OrderBy(l => l))
                    {
                        sb.Append("<a class=\"lang-badge\" href=\"/").Append(p.BlogSlug).Append("?lang=").Append(lang).Append("\">")
                          .Append(lang.ToUpperInvariant()).Append("</a>");
                    }
                    sb.Append("</span>");

                    foreach (var tag in SplitTags(p.Tags))
                    {
                        sb.Append(" <a class=\"tag-chip small\" href=\"").Append(TagFilterUrl(selectedTags.Append(tag)))
                          .Append("\">#").Append(System.Net.WebUtility.HtmlEncode(tag)).Append("</a>");
                    }
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }
        }

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell("Blog", sb.ToString(), Languages.Primary));
    }

    private static async Task RenderPostAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(PageShell("Not found", "<p>Post not found.</p>", Languages.Primary));
            return;
        }

        var availableLanguages = await db.DraftTranslations.Where(t => t.DraftId == draft.Id)
            .Select(t => t.Language)
            .ToListAsync();

        var requestedLang = ctx.Request.Query["lang"].FirstOrDefault();
        var lang = Languages.Primary;
        var title = draft.Title;
        var cedarJson = draft.CedarJson;
        if (requestedLang is not null && requestedLang != Languages.Primary && availableLanguages.Contains(requestedLang))
        {
            var translation = await db.DraftTranslations.FirstAsync(t => t.DraftId == draft.Id && t.Language == requestedLang);
            lang = translation.Language;
            title = translation.Title;
            cedarJson = translation.CedarJson;
        }

        var langSwitch = "";
        if (availableLanguages.Count > 0)
        {
            var items = new List<string>();
            items.Add(lang == Languages.Primary
                ? "<span class=\"lang-badge current\">RU</span>"
                : $"<a class=\"lang-badge\" href=\"/{draft.BlogSlug}\">RU</a>");
            foreach (var l in availableLanguages.OrderBy(l => l))
            {
                items.Add(lang == l
                    ? $"<span class=\"lang-badge current\">{l.ToUpperInvariant()}</span>"
                    : $"<a class=\"lang-badge\" href=\"/{draft.BlogSlug}?lang={l}\">{l.ToUpperInvariant()}</a>");
            }
            langSwitch = $"<p class=\"lang-badges lang-switch\">{string.Join("", items)}</p>";
        }

        var tags = SplitTags(draft.Tags);
        var tagsLine = tags.Count == 0 ? "" :
            "<p class=\"post-tags\">" + string.Join(" ", tags.Select(t =>
                $"<a class=\"tag-chip small\" href=\"{TagFilterUrl([t])}\">#{System.Net.WebUtility.HtmlEncode(t)}</a>")) + "</p>";

        var signature = await db.Users.Where(u => u.Id == draft.OwnerId).Select(u => u.PostSignature).FirstOrDefaultAsync();
        var signatureBlock = string.IsNullOrWhiteSpace(signature) ? "" :
            $"<p class=\"post-signature\">{System.Net.WebUtility.HtmlEncode(signature)}</p>";

        var body = CedarToBlogHtmlRenderer.Render(cedarJson, $"https://{Consts.URLs.BlogHost}");
        var dateLine = draft.BlogPublishedAt is { } published
            ? $"<p class=\"post-date\">{published:d MMM yyyy}</p>"
            : "";
        var telegramLink = draft is { LastTelegramUsername: not null, LastTelegramMessageId: not null }
            ? $"<p class=\"telegram-link\"><a href=\"https://t.me/{draft.LastTelegramUsername}/{draft.LastTelegramMessageId}\" target=\"_blank\" rel=\"noopener\">View this post on Telegram &#8594;</a></p>"
            : "";
        var articleBlock = "<div class=\"annotation article-annotation\" data-annotation-id=\"\">"
            + CedarToBlogHtmlRenderer.AnnotationControlsHtml() + "</div>";
        var html = $"<article><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>{dateLine}{langSwitch}{tagsLine}{body}{signatureBlock}{telegramLink}{articleBlock}</article>";

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(title, html, lang));
    }

    // Plain (non-interpolated) raw string — title/body are substituted via Replace so the
    // CSS's braces don't need interpolation-escaping.
    private const string ShellTemplate = """
        <!doctype html>
        <html lang="{{LANG}}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{TITLE}}</title>
        <script>
        (function () {
            var saved = localStorage.getItem('cedar-blog-theme');
            if (saved) document.documentElement.setAttribute('data-theme', saved);
        })();
        </script>
        <style>
        :root { color-scheme: light dark; --bg: #fdfcfa; --fg: #1c1a16; }
        @media (prefers-color-scheme: dark) { :root { --bg: #17140f; --fg: #eae6db; } }
        :root[data-theme="light"] { color-scheme: light; --bg: #fdfcfa; --fg: #1c1a16; }
        :root[data-theme="dark"] { color-scheme: dark; --bg: #17140f; --fg: #eae6db; }
        body { max-width: 680px; margin: 40px auto; padding: 0 20px; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; line-height: 1.6; background: var(--bg); color: var(--fg); }
        img, video { max-width: 100%; height: auto; }
        a { color: inherit; }
        .spoiler { background: currentColor; color: transparent; border-radius: 3px; cursor: pointer; }
        .spoiler:hover, .spoiler:focus { background: transparent; }
        .post-date, time { opacity: 0.6; font-size: 0.9em; }
        .post-list { list-style: none; padding: 0; }
        .post-list li { margin-bottom: 12px; }
        blockquote { border-left: 3px solid currentColor; opacity: 0.85; margin: 0; padding-left: 16px; }
        pre { overflow-x: auto; padding: 12px; background: rgba(128,128,128,0.15); border-radius: 6px; }
        .math-tex { margin: 16px 0; overflow-x: auto; }
        div.math-tex { text-align: center; }
        .collage { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 6px; }
        .collage img { width: 100%; height: 160px; object-fit: cover; border-radius: 6px; }
        .carousel { position: relative; margin: 16px 0; }
        .carousel-viewport img { width: 100%; display: block; border-radius: 6px; }
        .carousel-prev, .carousel-next { position: absolute; top: 50%; transform: translateY(-50%); background: rgba(0,0,0,0.5); color: #fff; border: none; width: 32px; height: 32px; border-radius: 50%; cursor: pointer; font-size: 18px; line-height: 1; }
        .carousel-prev { left: 8px; }
        .carousel-next { right: 8px; }
        .carousel-dots { display: flex; justify-content: center; gap: 6px; margin-top: 8px; }
        .carousel-dot { width: 8px; height: 8px; border-radius: 50%; border: none; background: rgba(128,128,128,0.4); cursor: pointer; padding: 0; }
        .carousel-dot.active { background: currentColor; }
        .telegram-link { margin-top: 24px; font-size: 0.9em; opacity: 0.8; }
        .telegram-link a { text-decoration: underline; }
        .annotation { border-left: 3px solid rgba(91,110,70,0.5); background: rgba(91,110,70,0.08); padding: 10px 14px; margin: 16px 0; border-radius: 4px; }
        .article-annotation { border-left: none; margin-top: 48px; padding: 20px; border-top: 2px solid rgba(128,128,128,0.3); background: rgba(128,128,128,0.06); border-radius: 0 0 8px 8px; }
        .article-annotation::before { content: "Reactions & comments"; display: block; font-size: 0.75em; text-transform: uppercase; letter-spacing: .06em; opacity: 0.55; margin-bottom: 12px; }
        .annotation-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-top: 8px; font-size: 0.9em; }
        .react-btn { background: none; border: 1px solid rgba(128,128,128,0.3); border-radius: 999px; padding: 3px 10px; cursor: pointer; color: inherit; font: inherit; }
        .react-btn.active { border-color: currentColor; background: rgba(128,128,128,0.15); }
        .comment-count-label { font-size: 0.9em; opacity: 0.8; }
        .comment-box { margin-top: 10px; }
        .comment-item { padding: 8px 0; border-top: 1px solid rgba(128,128,128,0.15); }
        .comment-meta { font-size: 0.8em; opacity: 0.6; margin-bottom: 2px; }
        .comment-load-more { display: block; margin: 8px 0; background: none; border: 1px solid rgba(128,128,128,0.3); border-radius: 6px; padding: 4px 10px; cursor: pointer; color: inherit; font: inherit; font-size: 0.85em; }
        .comment-form { display: flex; flex-direction: column; gap: 6px; margin-top: 8px; max-width: 360px; }
        .comment-form input, .comment-form textarea { font: inherit; padding: 6px 8px; border: 1px solid rgba(128,128,128,0.3); border-radius: 6px; background: transparent; color: inherit; }
        .comment-form textarea { min-height: 60px; resize: vertical; }
        .comment-form button { align-self: flex-start; padding: 5px 14px; border-radius: 6px; border: 1px solid currentColor; background: none; color: inherit; cursor: pointer; }
        .theme-toggle-btn { position: fixed; top: 14px; right: 14px; width: 34px; height: 34px; border-radius: 50%; border: 1px solid rgba(128,128,128,0.3); background: var(--bg); color: var(--fg); cursor: pointer; font-size: 15px; z-index: 100; }
        .lang-badges { display: inline-flex; gap: 4px; vertical-align: middle; }
        .lang-badge { display: inline-block; font-size: 0.7em; letter-spacing: .05em; padding: 1px 7px; border: 1px solid rgba(128,128,128,0.35); border-radius: 999px; text-decoration: none; opacity: 0.75; }
        a.lang-badge:hover { opacity: 1; border-color: currentColor; }
        .lang-badge.current { border-color: currentColor; opacity: 1; font-weight: 600; }
        .lang-switch { margin: -6px 0 18px; }
        .timeline-month { font-size: 0.85em; text-transform: uppercase; letter-spacing: .08em; opacity: 0.55; margin: 32px 0 10px; border-bottom: 1px solid rgba(128,128,128,0.2); padding-bottom: 4px; }
        .tag-bar { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 8px; }
        .tag-chip { display: inline-block; font-size: 0.8em; padding: 2px 10px; border: 1px solid rgba(128,128,128,0.35); border-radius: 999px; text-decoration: none; opacity: 0.8; }
        .tag-chip:hover { opacity: 1; border-color: currentColor; }
        .tag-chip.selected { border-color: currentColor; background: rgba(128,128,128,0.15); opacity: 1; font-weight: 600; }
        .tag-chip.small { font-size: 0.7em; padding: 1px 7px; }
        .post-tags { margin: -6px 0 18px; }
        .post-signature { white-space: pre-line; opacity: 0.7; margin-top: 32px; font-size: 0.95em; }
        </style>
        {{MATH_ASSETS}}
        </head>
        <body>
        <button type="button" class="theme-toggle-btn" id="themeToggleBtn" title="Toggle theme">&#9789;</button>
        {{BODY}}
        <script>
        document.querySelectorAll('.carousel').forEach(function (car) {
            var imgs = car.querySelectorAll('.carousel-viewport img');
            var dots = car.querySelectorAll('.carousel-dot');
            var i = 0;
            function show(n) {
                i = (n + imgs.length) % imgs.length;
                imgs.forEach(function (img, idx) { img.style.display = idx === i ? '' : 'none'; });
                dots.forEach(function (d, idx) { d.classList.toggle('active', idx === i); });
            }
            var prev = car.querySelector('.carousel-prev');
            var next = car.querySelector('.carousel-next');
            if (prev) prev.addEventListener('click', function () { show(i - 1); });
            if (next) next.addEventListener('click', function () { show(i + 1); });
            dots.forEach(function (d, idx) { d.addEventListener('click', function () { show(idx); }); });
            if (imgs.length) show(0);
        });

        (function () {
            var themeBtn = document.getElementById('themeToggleBtn');
            if (themeBtn) {
                var mql = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)');
                function currentTheme() {
                    return document.documentElement.getAttribute('data-theme') || (mql && mql.matches ? 'dark' : 'light');
                }
                function updateIcon() { themeBtn.textContent = currentTheme() === 'dark' ? String.fromCharCode(9728) : String.fromCharCode(9789); }
                updateIcon();
                themeBtn.addEventListener('click', function () {
                    var next = currentTheme() === 'dark' ? 'light' : 'dark';
                    document.documentElement.setAttribute('data-theme', next);
                    localStorage.setItem('cedar-blog-theme', next);
                    updateIcon();
                });
            }
        })();

        (function () {
            var annEls = document.querySelectorAll('.annotation');
            if (!annEls.length) return;
            var slug = location.pathname.replace(/^\/|\/$/g, '');
            if (!slug) return;

            var PAGE_SIZE = 20;

            function renderCommentsPage(listEl, moreBtn, comments, shownCount) {
                listEl.innerHTML = '';
                comments.slice(0, shownCount).forEach(function (c) {
                    var item = document.createElement('div');
                    item.className = 'comment-item';
                    var meta = document.createElement('div');
                    meta.className = 'comment-meta';
                    meta.textContent = (c.authorName || 'Anonymous') + ' — ' + new Date(c.createdAt).toLocaleDateString();
                    var text = document.createElement('div');
                    text.className = 'comment-text';
                    text.textContent = c.text;
                    item.appendChild(meta);
                    item.appendChild(text);
                    listEl.appendChild(item);
                });
                if (moreBtn) moreBtn.hidden = shownCount >= comments.length;
            }

            function hydrate(el, annotationId, info) {
                var counts = info.counts || {};
                el.querySelectorAll('.react-btn').forEach(function (btn) {
                    var kind = btn.getAttribute('data-kind');
                    var countEl = btn.querySelector('.count');
                    if (countEl) countEl.textContent = counts[kind] || 0;
                    btn.classList.toggle('active', info.myVote === kind);
                    btn.addEventListener('click', function () {
                        fetch('/api/posts/' + encodeURIComponent(slug) + '/react', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ annotationId: annotationId || null, kind: kind })
                        })
                            .then(function (r) { return r.json(); })
                            .then(function (res) {
                                el.querySelectorAll('.react-btn').forEach(function (b) {
                                    var k = b.getAttribute('data-kind');
                                    var c = b.querySelector('.count');
                                    if (c) c.textContent = (res.counts && res.counts[k]) || 0;
                                    b.classList.toggle('active', res.myVote === k);
                                });
                            })
                            .catch(function () {});
                    });
                });

                var commentList = el.querySelector('.comment-list');
                var moreBtn = el.querySelector('.comment-load-more');
                var commentCountEl = el.querySelector('.comment-count');
                var comments = (info.comments || []).slice();
                var shown = Math.min(PAGE_SIZE, comments.length);
                if (commentCountEl) commentCountEl.textContent = comments.length;
                renderCommentsPage(commentList, moreBtn, comments, shown);

                if (moreBtn) {
                    moreBtn.addEventListener('click', function () {
                        shown = Math.min(shown + PAGE_SIZE, comments.length);
                        renderCommentsPage(commentList, moreBtn, comments, shown);
                    });
                }

                var form = el.querySelector('.comment-form');
                if (form) {
                    form.addEventListener('submit', function (e) {
                        e.preventDefault();
                        var authorInput = form.querySelector('.comment-author');
                        var textInput = form.querySelector('.comment-text');
                        var text = textInput.value.trim();
                        if (!text) return;
                        fetch('/api/posts/' + encodeURIComponent(slug) + '/comments', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ annotationId: annotationId || null, authorName: authorInput.value.trim(), text: text })
                        })
                            .then(function (r) { if (!r.ok) throw new Error('failed'); return r.json(); })
                            .then(function (c) {
                                comments.unshift(c);
                                shown = Math.min(shown + 1, comments.length);
                                if (commentCountEl) commentCountEl.textContent = comments.length;
                                renderCommentsPage(commentList, moreBtn, comments, shown);
                                textInput.value = '';
                                authorInput.value = '';
                            })
                            .catch(function () {});
                    });
                }
            }

            fetch('/api/posts/' + encodeURIComponent(slug) + '/annotations')
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    annEls.forEach(function (el) {
                        var id = el.getAttribute('data-annotation-id') || '';
                        var info = id ? (data.annotations[id] || { counts: {}, myVote: null, comments: [] }) : data.article;
                        hydrate(el, id, info);
                    });
                })
                .catch(function () {});
        })();
        </script>
        </body>
        </html>
        """;

    private const string MathAssets = """
        <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css">
        <script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js" onload="document.querySelectorAll('.math-tex').forEach(function (el) { try { katex.render(el.textContent, el, { displayMode: el.dataset.display === 'true', throwOnError: false }); } catch (e) {} });"></script>
        """;

    private static string PageShell(string title, string bodyHtml, string lang)
    {
        var mathAssets = bodyHtml.Contains("math-tex") ? MathAssets : "";
        return ShellTemplate
            .Replace("{{LANG}}", lang)
            .Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{MATH_ASSETS}}", mathAssets)
            .Replace("{{BODY}}", bodyHtml);
    }
}
