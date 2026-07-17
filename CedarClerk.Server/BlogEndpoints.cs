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
    private const int ExcerptMaxLength = 140;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private record ReactRequest(string? AnnotationId, string Kind);
    private record CommentRequest(string? AnnotationId, string? AuthorName, string Text);
    private record BlogChannelInfo(string Title, string? Username, int? MemberCount);

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

        // All comments + reaction totals across every draft the user owns — backs the /comments
        // page, which replaced the editor's per-draft right-hand "Comments & likes" panel.
        commentsGroup.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draftTitleById = await db.Drafts.Where(d => d.OwnerId == uid)
                .Select(d => new { d.Id, d.Title })
                .ToDictionaryAsync(d => d.Id, d => d.Title);
            var draftIds = draftTitleById.Keys.ToList();

            var comments = await db.Comments.Where(c => draftIds.Contains(c.DraftId))
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.DraftId, c.AnnotationId, c.AuthorName, c.Text, c.CreatedAt })
                .ToListAsync();

            var reactionCounts = await db.Reactions.Where(r => draftIds.Contains(r.DraftId))
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
                comments = comments.Select(c => new
                {
                    c.Id,
                    c.DraftId,
                    DraftTitle = draftTitleById.GetValueOrDefault(c.DraftId, "Untitled"),
                    c.AnnotationId,
                    c.AuthorName,
                    c.Text,
                    c.CreatedAt,
                }),
            });
        });

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

    private static string Excerpt(string cedarJson)
    {
        string text;
        try
        {
            text = string.Join(" ", TipTapTextNodes.ExtractTexts(cedarJson)).Trim();
        }
        catch (Exception)
        {
            return "";
        }

        if (text.Length <= ExcerptMaxLength)
            return text;

        var cut = text[..ExcerptMaxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 0)
            cut = cut[..lastSpace];
        return cut + "…";
    }

    // The blog is single-channel per self-hosted instance: whichever channel belongs to an
    // owner with at least one published post represents the header identity.
    private static async Task<BlogChannelInfo?> GetBlogChannelInfoAsync(CedarDbContext db)
    {
        var ownerIds = await db.Drafts.Where(d => d.IsBlogPublished).Select(d => d.OwnerId).Distinct().ToListAsync();
        if (ownerIds.Count == 0)
            return null;

        var channel = await db.Channels.FirstOrDefaultAsync(c => ownerIds.Contains(c.OwnerId));
        if (channel is null)
            return null;

        var memberCount = await db.ChannelStatSnapshots
            .Where(s => s.ChannelId == channel.Id)
            .OrderByDescending(s => s.TakenAt)
            .Select(s => (int?)s.MemberCount)
            .FirstOrDefaultAsync();

        return new BlogChannelInfo(channel.Title, channel.Username, memberCount);
    }

    private static string RenderHeader(BlogChannelInfo? channel)
    {
        string identity;
        string openInTelegram = "";

        if (channel is null)
        {
            identity = """
                <div class="channel-avatar brand">
                <svg width="16" height="16" viewBox="0 0 24 24"><polygon points="12,2 19,11 5,11" fill="currentColor"></polygon><polygon points="12,7 21,18 3,18" fill="currentColor" opacity=".75"></polygon><rect x="10.6" y="18" width="2.8" height="4" rx="1" fill="currentColor" opacity=".9"></rect></svg>
                </div>
                <div class="channel-id"><div class="channel-name">Cedar Clerk Blog</div></div>
                """;
        }
        else
        {
            var initial = channel.Title.Length > 0 ? channel.Title[..1].ToUpperInvariant() : "?";
            var meta = channel.Username is null
                ? ""
                : channel.MemberCount is { } mc
                    ? $"@{System.Net.WebUtility.HtmlEncode(channel.Username)} · {mc} subscribers"
                    : $"@{System.Net.WebUtility.HtmlEncode(channel.Username)}";
            identity = $"""
                <div class="channel-avatar">{System.Net.WebUtility.HtmlEncode(initial)}</div>
                <div class="channel-id">
                <div class="channel-name">{System.Net.WebUtility.HtmlEncode(channel.Title)}</div>
                {(meta.Length == 0 ? "" : $"<div class=\"channel-meta\">{meta}</div>")}
                </div>
                """;

            if (channel.Username is not null)
            {
                openInTelegram = $"""
                    <a class="tg-open-btn" href="https://t.me/{System.Net.WebUtility.HtmlEncode(channel.Username)}" target="_blank" rel="noopener">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m22 2-7 20-4-9-9-4Z"></path><path d="M22 2 11 13"></path></svg>
                    <span class="tg-open-label">Open in Telegram</span>
                    </a>
                    """;
            }
        }

        return $"""
            <div class="site-header"><div class="site-header-inner">
            {identity}
            <div class="spacer"></div>
            {openInTelegram}
            <button type="button" class="theme-toggle-btn" id="themeToggleBtn" title="Toggle theme">&#9789;</button>
            </div></div>
            """;
    }

    private static async Task RenderIndexAsync(HttpContext ctx, CedarDbContext db)
    {
        var posts = await db.Drafts.Where(d => d.IsBlogPublished)
            .OrderByDescending(d => d.BlogPublishedAt)
            .Select(d => new
            {
                d.Id, d.Title, d.BlogSlug, d.BlogPublishedAt, d.Tags, d.CedarJson, d.ViewCount,
                TranslationLanguages = db.DraftTranslations.Where(t => t.DraftId == d.Id).Select(t => t.Language).ToList(),
            })
            .ToListAsync();

        var likeCounts = await db.Reactions.Where(r => r.Kind == "like")
            .GroupBy(r => r.DraftId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        var commentCounts = await db.Comments
            .GroupBy(c => c.DraftId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

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
            sb.Append("<div class=\"post-list\">");
            foreach (var p in filtered)
            {
                var tags = SplitTags(p.Tags);
                var likes = likeCounts.GetValueOrDefault(p.Id);
                var comments = commentCounts.GetValueOrDefault(p.Id);
                var excerpt = Excerpt(p.CedarJson);

                sb.Append("<a class=\"post-card\" href=\"/").Append(p.BlogSlug).Append("\">");
                sb.Append("<div class=\"post-card-meta\">");
                sb.Append("<span class=\"post-card-date\">")
                  .Append(p.BlogPublishedAt?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "")
                  .Append("</span>");

                sb.Append("<span class=\"post-card-langs\">RU");
                foreach (var lang in p.TranslationLanguages.OrderBy(l => l))
                    sb.Append(" · ").Append(lang.ToUpperInvariant());
                sb.Append("</span>");

                if (tags.Count > 0)
                    sb.Append("<span class=\"post-card-tag\">· ").Append(System.Net.WebUtility.HtmlEncode(tags[0])).Append("</span>");

                sb.Append("</div>");
                sb.Append("<div class=\"post-card-title\">").Append(System.Net.WebUtility.HtmlEncode(p.Title)).Append("</div>");
                if (excerpt.Length > 0)
                    sb.Append("<div class=\"post-card-excerpt\">").Append(System.Net.WebUtility.HtmlEncode(excerpt)).Append("</div>");
                sb.Append("<div class=\"post-card-stats\">&#128065; ").Append(p.ViewCount)
                  .Append(" &middot; &#128077; ").Append(likes).Append(" &middot; &#128172; ").Append(comments).Append("</div>");
                sb.Append("</a>");
            }
            sb.Append("</div>");
        }

        var channel = await GetBlogChannelInfoAsync(db);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell("Blog", sb.ToString(), Languages.Primary, RenderHeader(channel)));
    }

    private static async Task RenderPostAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var channel = await GetBlogChannelInfoAsync(db);
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(PageShell("Not found", "<p class=\"empty\">Post not found.</p>", Languages.Primary, RenderHeader(channel)));
            return;
        }

        // Atomic UPDATE (not draft.ViewCount++ + SaveChanges) so concurrent page views don't lose
        // updates to each other. Shared across RU/EN — see ADR-023, docs/DECISIONS.md.
        var viewCount = draft.ViewCount + 1;
        await db.Drafts.Where(d => d.Id == draft.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ViewCount, d => d.ViewCount + 1));

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
                ? "<span class=\"lang-switch-btn current\">RU</span>"
                : $"<a class=\"lang-switch-btn\" href=\"/{draft.BlogSlug}\">RU</a>");
            foreach (var l in availableLanguages.OrderBy(l => l))
            {
                items.Add(lang == l
                    ? $"<span class=\"lang-switch-btn current\">{l.ToUpperInvariant()}</span>"
                    : $"<a class=\"lang-switch-btn\" href=\"/{draft.BlogSlug}?lang={l}\">{l.ToUpperInvariant()}</a>");
            }
            langSwitch = $"<div class=\"lang-switch-track\">{string.Join("", items)}</div>";
        }

        var tags = SplitTags(draft.Tags);
        var tagsLine = tags.Count == 0 ? "" :
            "<span class=\"post-tag-caps\">" + string.Join(" &middot; ", tags.Select(System.Net.WebUtility.HtmlEncode)) + "</span>";

        var signature = await db.Users.Where(u => u.Id == draft.OwnerId).Select(u => u.PostSignature).FirstOrDefaultAsync();
        var signatureBlock = string.IsNullOrWhiteSpace(signature) ? "" :
            $"<span class=\"post-signature\">{System.Net.WebUtility.HtmlEncode(signature)}</span>";

        var body = CedarToBlogHtmlRenderer.Render(cedarJson, $"https://{Consts.URLs.BlogHost}");
        var dateLine = draft.BlogPublishedAt is { } published
            ? $"<span class=\"post-card-date\">{published:d MMM yyyy}</span>"
            : "";
        var telegramLink = draft is { LastTelegramUsername: not null, LastTelegramMessageId: not null }
            ? $"<a class=\"telegram-link\" href=\"https://t.me/{draft.LastTelegramUsername}/{draft.LastTelegramMessageId}\" target=\"_blank\" rel=\"noopener\">View in Telegram &#8594;</a>"
            : "";
        var viewsLine = $"<span class=\"post-card-views\">&#128065; {viewCount}</span>";

        var metaRow = $"<div class=\"post-meta-row\">{dateLine}{viewsLine}{langSwitch}{tagsLine}</div>";
        var footerRow = (signatureBlock.Length > 0 || telegramLink.Length > 0)
            ? $"<div class=\"post-footer-row\">{signatureBlock}<div class=\"spacer\"></div>{telegramLink}</div>"
            : "";

        var postSheet = $"""
            <div class="post-sheet">
            {metaRow}
            <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
            {body}
            {footerRow}
            </div>
            """;

        var articleBlock = "<div class=\"annotation article-annotation\" data-annotation-id=\"\">"
            + CedarToBlogHtmlRenderer.AnnotationControlsHtml() + "</div>";

        var html = $"""
            <a class="back-link" href="/">&larr; All posts</a>
            {postSheet}
            {articleBlock}
            """;

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(title, html, lang, RenderHeader(channel)));
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
        :root {
            color-scheme: light dark;
            --bg: #ECE9E2; --canvas: #E2DED4; --surface: #F7F5EF; --sheet: #FCFBF8; --alt: #EFECE4;
            --border: #DBD5C8; --text: #26231D; --t2: #6B655A; --t3: #9F988A;
            --accent: #5B6E46; --danger: #B4452C; --ok: #3E7A4E;
            --shadow: 0 1px 3px rgba(40,35,25,.10);
            --font-sans: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
            --font-mono: ui-monospace, Menlo, Consolas, monospace;
            --asoft: color-mix(in srgb, var(--accent) 13%, var(--surface));
            --abord: color-mix(in srgb, var(--accent) 38%, var(--border));
        }
        @media (prefers-color-scheme: dark) {
            :root {
                --bg: #1D1B17; --canvas: #171511; --surface: #25221B; --sheet: #211E18; --alt: #2F2C23;
                --border: #3C382D; --text: #EAE6DB; --t2: #A69F8F; --t3: #776F5F;
                --accent: color-mix(in srgb, #5B6E46 55%, #E8F0E8 45%); --danger: #E2745C; --ok: #82BB8C;
                --shadow: 0 1px 3px rgba(0,0,0,.45);
            }
        }
        :root[data-theme="light"] {
            --bg: #ECE9E2; --canvas: #E2DED4; --surface: #F7F5EF; --sheet: #FCFBF8; --alt: #EFECE4;
            --border: #DBD5C8; --text: #26231D; --t2: #6B655A; --t3: #9F988A;
            --accent: #5B6E46; --danger: #B4452C; --ok: #3E7A4E;
            --shadow: 0 1px 3px rgba(40,35,25,.10);
        }
        :root[data-theme="dark"] {
            --bg: #1D1B17; --canvas: #171511; --surface: #25221B; --sheet: #211E18; --alt: #2F2C23;
            --border: #3C382D; --text: #EAE6DB; --t2: #A69F8F; --t3: #776F5F;
            --accent: color-mix(in srgb, #5B6E46 55%, #E8F0E8 45%); --danger: #E2745C; --ok: #82BB8C;
            --shadow: 0 1px 3px rgba(0,0,0,.45);
        }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--canvas); color: var(--text); font-family: var(--font-sans); line-height: 1.6; }
        a { color: var(--accent); text-decoration: none; }
        img, video { max-width: 100%; height: auto; }
        .spacer { flex: 1; }

        .site-header { position: sticky; top: 0; z-index: 10; background: var(--surface); border-bottom: 1px solid var(--border); }
        .site-header-inner { max-width: 760px; margin: 0 auto; display: flex; align-items: center; gap: 10px; height: 54px; padding: 0 20px; }
        .channel-avatar { width: 30px; height: 30px; border-radius: 50%; background: #C98A3B; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 13px; font-weight: 700; flex: none; }
        .channel-avatar.brand { background: var(--asoft); color: var(--accent); }
        .channel-id { min-width: 0; }
        .channel-name { font-size: 14.5px; font-weight: 700; letter-spacing: -.01em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .channel-meta { font-size: 11px; color: var(--t3); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .tg-open-btn { display: flex; align-items: center; gap: 6px; border: 1px solid var(--abord); background: var(--asoft); border-radius: 8px; padding: 5px 12px; font-size: 12.5px; font-weight: 500; color: var(--text); white-space: nowrap; flex: none; }
        .tg-open-btn:hover { filter: brightness(.97); }
        .theme-toggle-btn { display: flex; align-items: center; justify-content: center; width: 30px; height: 30px; border: none; background: none; border-radius: 8px; color: var(--t2); cursor: pointer; font-size: 15px; }
        .theme-toggle-btn:hover { background: rgba(128,120,100,.14); }

        .site-main { max-width: 760px; margin: 0 auto; padding: 26px 20px 60px; }
        .empty { color: var(--t2); }

        .tag-bar { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 18px; }
        .tag-chip { display: inline-block; border: 1px solid var(--border); background: var(--sheet); color: var(--t2); border-radius: 999px; padding: 4px 13px; font-size: 12px; font-weight: 500; }
        .tag-chip:hover { border-color: var(--t3); }
        .tag-chip.selected { border-color: var(--abord); background: var(--asoft); color: var(--accent); }

        .post-list { display: flex; flex-direction: column; gap: 14px; }
        .post-card { display: block; background: var(--sheet); border-radius: 12px; box-shadow: var(--shadow); padding: 20px 24px; border: 1px solid transparent; color: var(--text); }
        .post-card:hover { border-color: var(--abord); }
        .post-card-meta { display: flex; align-items: center; gap: 8px; margin: 0 0 6px; font-size: 11.5px; color: var(--t3); }
        .post-card-langs { font-size: 10px; font-weight: 600; letter-spacing: .04em; color: var(--accent); background: var(--asoft); border-radius: 4px; padding: 2px 6px; }
        .post-card-title { font-size: 19px; font-weight: 700; letter-spacing: -.01em; line-height: 1.3; margin: 0 0 6px; }
        .post-card-excerpt { font-size: 14px; color: var(--t2); line-height: 1.55; margin: 0 0 10px; }
        .post-card-stats { font-size: 12px; color: var(--t3); }

        .back-link { display: inline-flex; align-items: center; gap: 5px; font-size: 13px; font-weight: 500; padding: 4px 0; margin: 0 0 12px; }
        .back-link:hover { text-decoration: underline; }
        .post-sheet { background: var(--sheet); border-radius: 12px; box-shadow: var(--shadow); padding: 32px 40px 28px; }
        .post-sheet h1 { font-size: 27px; font-weight: 700; letter-spacing: -.015em; line-height: 1.22; margin: 0 0 16px; }
        .post-sheet h2 { font-size: 20px; font-weight: 600; letter-spacing: -.01em; margin: 24px 0 8px; }
        .post-sheet p { font-size: 16px; line-height: 1.65; margin: 0 0 14px; }
        .post-meta-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin: 0 0 10px; font-size: 12px; color: var(--t3); }
        .lang-switch-track { display: flex; gap: 2px; background: var(--alt); border-radius: 7px; padding: 2px; }
        .lang-switch-btn { border: none; background: none; border-radius: 5px; padding: 3px 11px; font-size: 11.5px; font-weight: 600; color: var(--t2); }
        .lang-switch-btn.current { background: var(--sheet); box-shadow: var(--shadow); color: var(--text); }
        .post-tag-caps { font-size: 11px; font-weight: 600; letter-spacing: .05em; text-transform: uppercase; color: var(--accent); }
        .post-footer-row { display: flex; align-items: center; gap: 10px; border-top: 1px solid var(--border); padding: 14px 0 0; margin-top: 14px; }
        .post-signature { font-size: 13.5px; font-style: italic; color: var(--t2); white-space: pre-line; }
        .telegram-link { font-size: 12.5px; font-weight: 500; color: var(--accent); }

        .spoiler { background: var(--t3); color: transparent; border-radius: 4px; padding: 0 5px; cursor: pointer; transition: background .2s; }
        .spoiler:hover, .spoiler:focus { background: var(--alt); color: inherit; }
        .post-sheet code { font-family: var(--font-mono); font-size: .85em; background: var(--alt); border-radius: 4px; padding: 1px 6px; }
        .post-sheet pre { background: #22201A; color: #C9C08C; border-radius: 8px; padding: 12px 14px; overflow-x: auto; }
        .post-sheet pre code { background: none; padding: 0; font-size: 13.5px; line-height: 1.55; }
        .post-sheet blockquote { border-left: 3px solid var(--abord); padding: 2px 0 2px 14px; color: var(--t2); margin: 0 0 16px; }
        .post-sheet ul, .post-sheet ol { font-size: 16px; line-height: 1.7; padding-left: 20px; margin: 0 0 16px; }
        .post-sheet figure { margin: 0 0 16px; }
        .post-sheet figcaption { text-align: center; font-size: 13px; color: var(--t2); margin-top: 6px; }
        .post-sheet table { width: 100%; border-collapse: collapse; font-size: 14.5px; margin: 0 0 16px; overflow-x: auto; display: block; }
        .post-sheet th, .post-sheet td { border: 1px solid var(--border); padding: 7px 11px; text-align: left; vertical-align: top; }
        .post-sheet th { background: var(--alt); font-weight: 600; }
        .post-sheet tr:nth-child(even) td { background: color-mix(in srgb, var(--alt) 40%, transparent); }
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
        .carousel-dot.active { background: var(--accent); }
        .footnotes { font-size: 12.5px; color: var(--t2); border-top: 1px solid var(--border); padding: 10px 0 0; margin: 0 0 4px; }
        .footnotes sup, .post-sheet sup { color: var(--accent); font-weight: 600; }

        .annotation { border-left: 3px solid var(--abord); background: var(--asoft); padding: 10px 14px; margin: 16px 0; border-radius: 4px; }
        .article-annotation { border-left: none; background: none; padding: 0; margin: 16px 0 0; }
        .annotation-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 8px; }
        .article-annotation > .annotation-controls { margin-bottom: 14px; }
        .react-btn { display: flex; align-items: center; gap: 7px; border: 1px solid var(--border); background: var(--sheet); border-radius: 999px; padding: 7px 16px; font-size: 14px; cursor: pointer; color: var(--text); font-family: inherit; }
        .react-btn:hover { border-color: var(--abord); }
        .react-btn.active { border-color: var(--abord); background: var(--asoft); }
        .react-btn .count { font-weight: 600; font-variant-numeric: tabular-nums; }
        .comment-count-label { font-size: 13px; color: var(--t3); }
        .comment-box { background: var(--sheet); border-radius: 12px; box-shadow: var(--shadow); padding: 20px 24px; }
        .comment-box-label { font-size: 10.5px; letter-spacing: .07em; text-transform: uppercase; font-weight: 600; color: var(--t3); margin: 0 0 12px; }
        .comment-list { display: flex; flex-direction: column; gap: 4px; margin: 0 0 14px; }
        .comment-item { display: flex; gap: 10px; padding: 8px 10px; border-radius: 9px; transition: background .25s; }
        .comment-item.glow { background: var(--asoft); }
        .comment-avatar { width: 28px; height: 28px; border-radius: 50%; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 700; flex: none; }
        .comment-meta { display: flex; align-items: baseline; gap: 7px; font-size: 13px; font-weight: 600; }
        .comment-meta time { font-size: 11px; font-weight: 400; color: var(--t3); }
        .comment-anchor { font-size: 11px; color: var(--accent); background: var(--asoft); border-radius: 5px; padding: 2px 7px; display: inline-block; margin: 3px 0 1px; }
        .comment-text { font-size: 14px; line-height: 1.5; }
        .comment-load-more { display: block; margin: 0 0 10px; background: none; border: 1px solid var(--border); border-radius: 6px; padding: 4px 10px; cursor: pointer; color: var(--text); font: inherit; font-size: 12.5px; }
        .comment-form { display: flex; gap: 8px; }
        .comment-form input, .comment-form textarea { flex: 1; border: 1px solid var(--border); background: var(--sheet); color: var(--text); border-radius: 8px; padding: 9px 12px; font-size: 13.5px; font-family: inherit; outline: none; resize: vertical; }
        .comment-form input:focus, .comment-form textarea:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--asoft); }
        .comment-form .comment-author { flex: none; width: 140px; }
        .comment-form button { align-self: flex-start; border: none; background: var(--accent); color: #F4F2EA; border-radius: 8px; padding: 9px 18px; font-size: 13.5px; font-weight: 500; cursor: pointer; font-family: inherit; }
        .comment-form button:hover { filter: brightness(1.08); }

        .site-footer { border-top: 1px solid var(--border); background: var(--surface); }
        .site-footer-inner { max-width: 760px; margin: 0 auto; display: flex; align-items: center; justify-content: center; gap: 8px; padding: 16px 20px; font-size: 12px; color: var(--t3); }

        /* .post-sheet's fixed 40px side padding left barely any room for the 3-wide comment
           form (name input + textarea + button) on phone-width screens — stack it instead. */
        @media (max-width: 480px) {
            .post-sheet { padding: 22px 16px 20px; }
            .comment-box { padding: 16px; }
            .tg-open-btn span.tg-open-label { display: none; }
            .comment-form { flex-direction: column; }
            .comment-form .comment-author { width: 100%; }
            .comment-form button { align-self: stretch; }
        }
        </style>
        {{MATH_ASSETS}}
        </head>
        <body>
        {{HEADER}}
        <main class="site-main">
        {{BODY}}
        </main>
        <div class="site-footer"><div class="site-footer-inner">
        <svg width="14" height="14" viewBox="0 0 24 24"><polygon points="12,2 19,11 5,11" fill="var(--accent)"></polygon><polygon points="12,7 21,18 3,18" fill="var(--accent)" opacity="0.75"></polygon><rect x="10.6" y="18" width="2.8" height="4" rx="1" fill="var(--accent)" opacity="0.9"></rect></svg>
        <span>Made with <a href="https://cedarclerk.mooexe.dev" style="font-weight:500">Cedar Clerk</a> — write here, publish there. Moo.</span>
        </div></div>
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
            var AVATAR_COLORS = ['#7A5A3A', '#375D74', '#3E7A4E', '#8A4A6B', '#5B6E46'];
            function avatarColor(name) {
                var hash = 0;
                for (var i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) >>> 0;
                return AVATAR_COLORS[hash % AVATAR_COLORS.length];
            }

            function renderCommentsPage(listEl, moreBtn, comments, shownCount) {
                listEl.innerHTML = '';
                comments.slice(0, shownCount).forEach(function (c) {
                    var name = c.authorName || 'Anonymous';
                    var item = document.createElement('div');
                    item.className = 'comment-item';
                    var avatar = document.createElement('div');
                    avatar.className = 'comment-avatar';
                    avatar.style.background = avatarColor(name);
                    avatar.textContent = name.charAt(0).toUpperCase();
                    var body = document.createElement('div');
                    var meta = document.createElement('div');
                    meta.className = 'comment-meta';
                    var nameEl = document.createElement('span');
                    nameEl.textContent = name;
                    var timeEl = document.createElement('time');
                    timeEl.textContent = new Date(c.createdAt).toLocaleDateString();
                    meta.appendChild(nameEl);
                    meta.appendChild(timeEl);
                    var text = document.createElement('div');
                    text.className = 'comment-text';
                    text.textContent = c.text;
                    body.appendChild(meta);
                    body.appendChild(text);
                    item.appendChild(avatar);
                    item.appendChild(body);
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
                        var textInput = form.querySelector('textarea.comment-text');
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

    private static string PageShell(string title, string bodyHtml, string lang, string headerHtml)
    {
        var mathAssets = bodyHtml.Contains("math-tex") ? MathAssets : "";
        return ShellTemplate
            .Replace("{{LANG}}", lang)
            .Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{MATH_ASSETS}}", mathAssets)
            .Replace("{{HEADER}}", headerHtml)
            .Replace("{{BODY}}", bodyHtml);
    }
}
