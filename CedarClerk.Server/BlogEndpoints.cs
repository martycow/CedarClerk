using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace CedarClerk.Server;

public static partial class BlogEndpoints
{
    private const int CommentMaxLength = 2000;
    private const int AuthorNameMaxLength = 60;
    private const int ExcerptMaxLength = 140;
    private const int RssItemLimit = 30;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Hardcoded rather than CultureInfo("ru-RU") — the production runtime install is bare (no SDK,
    // see .claude/rules/production-environment.md) and the rest of the codebase never reaches
    // for a non-invariant CultureInfo, so avoid depending on ICU data being present for this.
    private record ReactRequest(string? AnnotationId, string Kind);
    private record PollVoteRequest(string PollId, string Option);
    private record CommentRequest(string? AnnotationId, string? AuthorName, string Text, Guid? ParentCommentId = null);
    private record RegistrationRequest(string? Name, string? Nickname, string? Email, string? SocialLink, Dictionary<string, string>? Answers);
    private record BlogChannelInfo(string Title, string? Username, int? MemberCount, string? AvatarUrl);
    private record MarkSeenRequest(DateTime? SeenAt);
    // ADR-065 — language → the fingerprint of the version the owner was shown before confirming.
    // NotifySubscribers is the export modal's opt-in toggle: mailing the blog-wide list is a
    // decision per post, never a side effect of publishing.
    public record PublishBlogRequest(Dictionary<string, string>? ConfirmedFingerprints = null, bool NotifySubscribers = false);

    public static void MapBlogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/drafts").RequireAuthorization();

        group.MapPost("/{id:guid}/publish-blog", async (Guid id, PublishBlogRequest? req, ClaimsPrincipal user,
            CedarDbContext db, IConfiguration cfg, Email.ResendEmailProvider mailer, ILoggerFactory logger,
            BlogSubscriberNotifier notifier) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            // ADR-102 — the blog is deliberately not an IPublishTarget (docs/tech/ARCHITECTURE.md), so it
            // does not inherit the refusal on the networks' shared path and needs its own. Without
            // this, working material could not be sent to a channel but could be put on a public web
            // page, which is the more exposing of the two.
            if (!DocumentTypes.IsPublishable(draft.DocumentType))
                return Results.Json(new { error = ErrorMessages.DocumentTypeNotPublishable }, statusCode: StatusCodes.Status400BadRequest);

            var translations = await db.DraftTranslations.Where(t => t.DraftId == id).ToListAsync();
            var languages = new List<string> { draft.PrimaryLanguage };
            languages.AddRange(translations.Select(t => t.Language));

            // ADR-065 — one click republishes every language, so every language that is already
            // live has to be confirmed. ADR-064 claimed this guard covered the blog; it did not,
            // which left the exact path of the 29.07 data-loss incident unprotected.
            var stale = new List<DraftRevisionService.PublishPreview>();
            foreach (var lang in languages)
            {
                var confirmed = req?.ConfirmedFingerprints?.GetValueOrDefault(lang);
                if (await DraftRevisionService.ConfirmationSatisfiedAsync(db, draft, lang, DraftRevisionService.Kinds.Blog, null, confirmed))
                    continue;
                if (await DraftRevisionService.PreviewAsync(db, draft, lang, DraftRevisionService.Kinds.Blog, null) is { } preview)
                    stale.Add(preview);
            }
            if (stale.Count > 0)
                return Results.Json(new { error = ErrorMessages.PublishConfirmationStale, previews = stale }, statusCode: StatusCodes.Status409Conflict);

            if (!draft.IsBlogPublished || draft.BlogSlug is null)
                draft.BlogSlug = await GenerateUniqueSlugAsync(db, uid, draft.Id, draft.Title);

            // T-297 — followers hear about a devlog once, when it first goes up. Republishing to fix
            // a typo is not news, and a subscription that mails on every save would be uninstalled
            // by its first reader.
            var firstPublish = draft.BlogPublishedAt is null;
            draft.BlogPublishedAt ??= DateTime.UtcNow;
            draft.IsBlogPublished = true;
            await DraftRevisionService.RecordAsync(db, id, draft.PrimaryLanguage, draft.Title, draft.CedarJson, DraftRevisionService.Kinds.Blog);
            foreach (var translation in translations)
                await DraftRevisionService.RecordAsync(db, id, translation.Language, translation.Title, translation.CedarJson, DraftRevisionService.Kinds.Blog);
            await db.SaveChangesAsync();

            // An account with no name of its own has no blog host to point at yet; the post is
            // published, the URL is simply not knowable, and inventing one would name a domain that
            // belongs to somebody else.
            var host = await BlogTenant.HostForOwnerAsync(db, cfg, uid);
            if (firstPublish && host is not null)
                await NotifyFollowersAsync(db, mailer, logger, draft, new BlogSite(uid, host));

            // Item 7 — queued, never sent inline: EnqueueAsync writes a Pending row and kicks the
            // job, so a slow mail burst can never hold this response. First publish only, and only
            // when the owner ticked the toggle.
            if (firstPublish && !draft.IsPrivate && host is not null && req?.NotifySubscribers == true)
                await notifier.EnqueueAsync(db, draft);

            return Results.Ok(new { slug = draft.BlogSlug, url = host is null ? null : $"https://{host}/{draft.BlogSlug}" });
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

        var blogStatsGroup = app.MapGroup("/api/blog").RequireAuthorization();

        // Channel-agnostic blog growth — the counterpart to GET /api/channels/{id}/stats (see
        // ADR-025/ADR in docs/DECISIONS.md), backing the "Blog" tab on /stats. Scoped by OwnerId
        // rather than ChannelId since blog views aren't tied to any one Telegram channel.
        blogStatsGroup.MapGet("/stats", async (ClaimsPrincipal user, CedarDbContext db, int days = 30) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            // Unlike a Telegram channel (which gets its first snapshot the moment it's connected,
            // ChannelEndpoints.cs), there's no "connect" moment for the blog — take today's snapshot
            // on demand here if the nightly job (SnapshotChannelStatsJob) hasn't run yet today, so
            // opening this tab for the first time doesn't just show "—" until tomorrow.
            var today = DateTime.UtcNow.Date;
            var hasToday = await db.BlogStatSnapshots.AnyAsync(s => s.OwnerId == uid && s.TakenAt.Date == today);
            if (!hasToday)
            {
                var ownDraftIds = await db.Drafts.Where(d => d.OwnerId == uid).Select(d => d.Id).ToListAsync();
                if (ownDraftIds.Count > 0)
                {
                    var viewCount = await db.Drafts.Where(d => d.OwnerId == uid).SumAsync(d => d.ViewCount);
                    var likeCount = await db.Reactions.CountAsync(r => ownDraftIds.Contains(r.DraftId) && r.Kind == "like");
                    var commentCount = await db.Comments.CountAsync(c => ownDraftIds.Contains(c.DraftId));
                    db.BlogStatSnapshots.Add(new BlogStatSnapshot { OwnerId = uid, ViewCount = viewCount, LikeCount = likeCount, CommentCount = commentCount });
                    await db.SaveChangesAsync();
                }
            }

            var snapshots = await db.BlogStatSnapshots
                .Where(s => s.OwnerId == uid)
                .OrderByDescending(s => s.TakenAt)
                .Take(days)
                .OrderBy(s => s.TakenAt)
                .Select(s => new { s.TakenAt, s.ViewCount, s.LikeCount, s.CommentCount })
                .ToListAsync();

            var now = DateTime.UtcNow;
            var currentViews = snapshots.Count > 0 ? snapshots[^1].ViewCount : (int?)null;
            var currentLikes = snapshots.Count > 0 ? snapshots[^1].LikeCount : (int?)null;
            var currentComments = snapshots.Count > 0 ? snapshots[^1].CommentCount : (int?)null;
            var deltaWeekViews = ChannelStatsCalculator.DeltaOverDays(snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.ViewCount)).ToList(), 7, now);
            var deltaWeekLikes = ChannelStatsCalculator.DeltaOverDays(snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.LikeCount)).ToList(), 7, now);
            var deltaWeekComments = ChannelStatsCalculator.DeltaOverDays(snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.CommentCount)).ToList(), 7, now);

            // Audience split over the same window (Marty, 08.08.2026). Unlike the series above it
            // is a sum over the period, not a running total: "who read me this month", not "how
            // many readers do I have". Rows are few (countries x languages x days), so the grouping
            // happens in memory rather than as two more SQLite round trips.
            var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
            var geoRows = await db.BlogViewGeoDailies
                .Where(v => v.OwnerId == uid && v.Day >= since)
                .Select(v => new { v.Country, v.Language, v.ViewCount })
                .ToListAsync();

            var countries = geoRows.GroupBy(r => r.Country)
                .Select(g => new { code = g.Key, views = g.Sum(r => r.ViewCount) })
                .OrderByDescending(c => c.views).ThenBy(c => c.code)
                .ToList();
            var languages = geoRows.GroupBy(r => r.Language)
                .Select(g => new { code = g.Key, views = g.Sum(r => r.ViewCount) })
                .OrderByDescending(l => l.views).ThenBy(l => l.code)
                .ToList();

            return Results.Ok(new
            {
                currentViews, deltaWeekViews,
                currentLikes, deltaWeekLikes,
                currentComments, deltaWeekComments,
                snapshots,
                countries, languages,
            });
        });

        var commentsGroup = app.MapGroup("/api/comments").RequireAuthorization();

        // All comments + reaction totals across every draft the user owns — backs the /comments
        // page, which replaced the editor's per-draft right-hand "Comments & likes" panel.
        commentsGroup.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            // N8 — the watermark is only READ here. Marking things seen is an explicit action
            // (hovering a row), so opening the page can't silently clear the highlights.
            var seenAt = await db.Users.Where(u => u.Id == uid).Select(u => u.FeedbackSeenAt).FirstOrDefaultAsync();
            var draftTitleById = await db.Drafts.Where(d => d.OwnerId == uid)
                .Select(d => new { d.Id, d.Title })
                .ToDictionaryAsync(d => d.Id, d => d.Title);
            var draftIds = draftTitleById.Keys.ToList();

            var comments = await db.Comments.Where(c => draftIds.Contains(c.DraftId))
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.DraftId, c.AnnotationId, c.AuthorName, c.Text, c.CreatedAt })
                .ToListAsync();

            var reactions = await db.Reactions.Where(r => draftIds.Contains(r.DraftId))
                .Select(r => new { r.DraftId, r.Kind, r.CreatedAt })
                .ToListAsync();

            int Count(string kind, bool onlyNew) => reactions
                .Count(r => r.Kind == kind && (!onlyNew || (seenAt is null || r.CreatedAt > seenAt)));

            // Per-draft split so the feedback tab can group by post instead of showing one
            // undifferentiated total. Only drafts that actually have reactions appear.
            var byDraft = reactions.GroupBy(r => r.DraftId).Select(g => new
            {
                DraftId = g.Key,
                DraftTitle = draftTitleById.GetValueOrDefault(g.Key, "Untitled"),
                Likes = g.Count(r => r.Kind == "like"),
                Dislikes = g.Count(r => r.Kind == "dislike"),
                NewLikes = g.Count(r => r.Kind == "like" && (seenAt is null || r.CreatedAt > seenAt)),
                NewDislikes = g.Count(r => r.Kind == "dislike" && (seenAt is null || r.CreatedAt > seenAt)),
            }).ToList();

            return Results.Ok(new
            {
                reactions = new
                {
                    likes = Count("like", false),
                    dislikes = Count("dislike", false),
                    newLikes = Count("like", true),
                    newDislikes = Count("dislike", true),
                },
                reactionsByDraft = byDraft,
                comments = comments.Select(c => new
                {
                    c.Id,
                    c.DraftId,
                    DraftTitle = draftTitleById.GetValueOrDefault(c.DraftId, "Untitled"),
                    c.AnnotationId,
                    c.AuthorName,
                    c.Text,
                    c.CreatedAt,
                    IsNew = seenAt is null || c.CreatedAt > seenAt,
                }),
            });
        });

        // Just the counts behind the attention badges (N3) — the full feedback list is far too
        // much to fetch for a number in a corner, and this is polled from more than one screen.
        commentsGroup.MapGet("/new-count", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var seenAt = await db.Users.Where(u => u.Id == uid).Select(u => u.FeedbackSeenAt).FirstOrDefaultAsync();
            var draftIds = await db.Drafts.Where(d => d.OwnerId == uid).Select(d => d.Id).ToListAsync();

            var newComments = await db.Comments
                .CountAsync(c => draftIds.Contains(c.DraftId) && (seenAt == null || c.CreatedAt > seenAt));
            var newReactions = await db.Reactions
                .CountAsync(r => draftIds.Contains(r.DraftId) && (seenAt == null || r.CreatedAt > seenAt));

            return Results.Ok(new { newComments, newReactions });
        });

        // Moves the watermark forward only (N8) — a stale request from a tab left open overnight
        // must not un-see feedback the user has already read elsewhere.
        commentsGroup.MapPost("/seen", async (MarkSeenRequest req, ClaimsPrincipal principal, CedarDbContext db) =>
        {
            var uid = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user is null) return Results.Unauthorized();

            var seenAt = req.SeenAt ?? DateTime.UtcNow;
            if (user.FeedbackSeenAt is null || seenAt > user.FeedbackSeenAt)
            {
                user.FeedbackSeenAt = seenAt;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { feedbackSeenAt = user.FeedbackSeenAt });
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

    // Paths HandleRequest answers before it ever tries the one-segment slug branch — a post whose
    // title slugified into one of these would be published and unreachable.
    private static readonly HashSet<string> ReservedSlugs =
        new(StringComparer.OrdinalIgnoreCase) { "search", "subscribe", "sitemap.xml" };

    private static async Task<string> GenerateUniqueSlugAsync(CedarDbContext db, string ownerId, Guid draftId, string title)
    {
        var baseSlug = SlugGenerator.Slugify(title);
        var candidate = baseSlug;
        var n = 2;
        while (ReservedSlugs.Contains(candidate)
               || await db.Drafts.AnyAsync(d => d.BlogSlug == candidate && d.OwnerId == ownerId && d.Id != draftId))
        {
            candidate = $"{baseSlug}-{n}";
            n++;
        }
        return candidate;
    }

    public static async Task HandleRequest(HttpContext ctx)
    {
        var db = ctx.RequestServices.GetRequiredService<CedarDbContext>();

        // Whose blog this is, named in every query below rather than left to the ambient filter.
        // Only a resolved subdomain reaches here, so this cannot fail without the routing being wrong.
        if (BlogTenant.SiteOf(ctx) is not { } site)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var path = ctx.Request.Path.Value?.Trim('/') ?? "";
        string[] segments = path.Length == 0 ? [] : path.Split('/');

        // T-300 — on a project's own domain the showcase is the site, so its paths lose the
        // /games/{slug} prefix. Putting the prefix back here rather than duplicating every branch
        // below keeps one set of routes: the domain decides the address, not the behaviour.
        if (ctx.RequestServices.GetService<TenantContext>()?.ShowcaseSlug is { } domainSlug)
            segments = segments switch
            {
                [] => ["games", domainSlug],
                ["rss.xml"] or ["follow"] or ["confirm"] or ["unsubscribe"] or ["press"] => ["games", domainSlug, segments[0]],
                ["go", var goIndex] => ["games", domainSlug, "go", goIndex],
                ["press", "pack.zip"] => ["games", domainSlug, "press", "pack.zip"],
                _ => segments,
            };

        if (segments is ["api", "posts", var slug, var action])
        {
            if (action == "annotations" && ctx.Request.Method == HttpMethods.Get)
                await GetAnnotationsAsync(ctx, db, site, slug);
            else if (action == "react" && ctx.Request.Method == HttpMethods.Post)
                await PostReactionAsync(ctx, db, site, slug);
            else if (action == "comments" && ctx.Request.Method == HttpMethods.Post)
                await PostCommentAsync(ctx, db, site, slug);
            else if (action == "register" && ctx.Request.Method == HttpMethods.Post)
                await PostRegistrationAsync(ctx, db, site, slug);
            else if (action == "poll" && ctx.Request.Method == HttpMethods.Post)
                await PostPollVoteAsync(ctx, db, site, slug);
            else
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // T-297 — the follow form posts as a plain form and answers with a redirect, so the page
        // needs no JavaScript to collect an address. Above the GET gate for that reason.
        if (segments is ["games", var followSlug, "follow"] && ctx.Request.Method == HttpMethods.Post)
        {
            await PostFollowAsync(ctx, db, site, followSlug);
            return;
        }

        // Item 7 — the blog-wide subscribe box posts the same way the follow form does, and for the
        // same reason: no JavaScript on the page just to collect an address.
        if (segments is ["subscribe"] && ctx.Request.Method == HttpMethods.Post)
        {
            await PostSubscribeAsync(ctx, db, site);
            return;
        }

        // HEAD is a GET whose body is thrown away, and Kestrel does the throwing — so it renders the
        // same page and answers the same status. Refusing it told every uptime monitor the blog was
        // gone while a browser saw it fine (13.08.2026: UptimeRobot HEADs by default, `curl -I` 404,
        // `curl` 200). Anything else still gets 404 rather than 405: a page is not a form.
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (segments.Length == 0)
        {
            await RenderIndexAsync(ctx, db, site);
            return;
        }

        if (segments is ["rss.xml"])
        {
            await RenderRssAsync(ctx, db, site);
            return;
        }

        if (segments is ["sitemap.xml"])
        {
            await RenderSitemapAsync(ctx, db, site);
            return;
        }

        // Before the one-segment slug branch on purpose — "search" is a path, never a post, and
        // GenerateUniqueSlugAsync refuses to mint it as one.
        if (segments is ["search"])
        {
            await RenderSearchAsync(ctx, db, site);
            return;
        }

        if (segments is ["subscribe", "confirm"])
        {
            await ConfirmSubscriberAsync(ctx, db, site);
            return;
        }

        if (segments is ["subscribe", "leave"])
        {
            await RemoveSubscriberAsync(ctx, db, site);
            return;
        }

        // Item 1 — the generated OG card, served and cached by OgImageEndpoint (lane-data).
        if (segments is ["og", var ogFile])
        {
            await OgImageEndpoint.HandleAsync(ctx, db, site, ogFile);
            return;
        }

        if (segments.Length == 1)
        {
            await RenderPostAsync(ctx, db, site, segments[0]);
            return;
        }

        // ADR-125 — the series landing is the first (and so far only) two-segment page.
        if (segments is ["series", var seriesSlug])
        {
            await RenderSeriesAsync(ctx, db, site, seriesSlug);
            return;
        }

        // ADR-134 — the public project showcase (T-159).
        if (segments is ["games", var gameSlug])
        {
            await RenderShowcaseAsync(ctx, db, site, gameSlug);
            return;
        }

        // ADR-216 — the showcase's own feed (T-298), its counted store links (T-296) and the two
        // links a follow mail carries (T-297).
        if (segments is ["games", var feedSlug, "rss.xml"])
        {
            await RenderShowcaseRssAsync(ctx, db, site, feedSlug);
            return;
        }

        if (segments is ["games", var linkSlug, "go", var linkIndex])
        {
            await RedirectShowcaseLinkAsync(ctx, db, site, linkSlug, linkIndex);
            return;
        }

        if (segments is ["games", var confirmSlug, "confirm"])
        {
            await ConfirmFollowAsync(ctx, db, site, confirmSlug);
            return;
        }

        if (segments is ["games", var byeSlug, "unsubscribe"])
        {
            await UnsubscribeFollowerAsync(ctx, db, site, byeSlug);
            return;
        }

        // Item 6 — the press kit page and its downloadable pack (PressPackEndpoint, lane-data).
        if (segments is ["games", var pressSlug, "press"])
        {
            await RenderPressAsync(ctx, db, site, pressSlug);
            return;
        }

        if (segments is ["games", var packSlug, "press", "pack.zip"])
        {
            await PressPackEndpoint.HandleAsync(ctx, db, site, packSlug);
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

    // Shared by every "look up a Draft by slug" call site (RenderPostAsync, GetAnnotationsAsync,
    // PostReactionAsync, PostCommentAsync) — see the ADR following ADR-040, docs/DECISIONS.md.
    // A private draft is only visible once the invite-grant cookie has been set (RenderPostAsync
    // is the only place that sets it, after validating a ?invite= token).
    // T-023 — the cookie carries a signed grant now, not the bare "1" it used to. A value that
    // proves nothing could be forged by anyone who knew a draft's id; this one cannot be written
    // without the server's key. Cookies issued before this change stop working, and their readers
    // meet the gate again — which is the honest cost of closing it.
    private static bool HasPrivateAccess(HttpContext ctx, Draft draft)
    {
        if (!draft.IsPrivate) return true;

        var access = ctx.RequestServices.GetRequiredService<PrivateAccess>();
        return access.IsValid(ctx.Request.Cookies[PrivateAccess.CookieName(draft.Id)], draft.Id, out _);
    }

    private static void GrantPrivateAccess(HttpContext ctx, Guid draftId, string token)
    {
        var access = ctx.RequestServices.GetRequiredService<PrivateAccess>();
        ctx.Response.Cookies.Append(PrivateAccess.CookieName(draftId), access.Grant(draftId, token), new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(90),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });
    }

    // Folds one counted view into today's (country, language) bucket. Hand-rolled upsert: EF has
    // no INSERT..ON CONFLICT, so bump first and insert only when no bucket existed yet. Two first
    // views of the same day can race into the insert — the unique index rejects the loser, which
    // then bumps the winner's row instead of dropping the view.
    private static async Task RecordViewGeoAsync(CedarDbContext db, HttpContext ctx, string ownerId)
    {
        var day = DateTime.UtcNow.Date;
        var country = ReaderGeo.NormalizeCountry(ctx.Request.Headers["CF-IPCountry"].FirstOrDefault());
        var language = ReaderGeo.NormalizeLanguage(ctx.Request.Headers.AcceptLanguage.FirstOrDefault());

        Task<int> BumpAsync() => db.BlogViewGeoDailies
            .Where(v => v.OwnerId == ownerId && v.Day == day && v.Country == country && v.Language == language)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ViewCount, v => v.ViewCount + 1));

        if (await BumpAsync() > 0) return;

        var row = new BlogViewGeoDaily { OwnerId = ownerId, Day = day, Country = country, Language = language, ViewCount = 1 };
        db.BlogViewGeoDailies.Add(row);
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

    private static async Task GetAnnotationsAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished);
        if (draft is null || !HasPrivateAccess(ctx, draft))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var visitor = VisitorHash(ctx);
        var reactions = await db.Reactions.Where(r => r.DraftId == draft.Id).ToListAsync();
        var comments = await db.Comments.Where(c => c.DraftId == draft.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        // NF5 — every poll on the page in one request, alongside the reactions/comments this
        // endpoint already bootstraps; keyed by poll id since a post can hold more than one.
        var pollVotes = await db.PollVotes.Where(v => v.DraftId == draft.Id).ToListAsync();
        var polls = pollVotes.GroupBy(v => v.PollId).ToDictionary(g => g.Key, g => new
        {
            counts = g.GroupBy(v => v.Option).ToDictionary(gg => gg.Key, gg => gg.Count()),
            myVote = g.FirstOrDefault(v => v.VisitorHash == visitor)?.Option,
        });

        object BuildGroup(string? annotationId)
        {
            var group = reactions.Where(r => r.AnnotationId == annotationId).ToList();
            var counts = group.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
            var myVote = group.FirstOrDefault(r => r.VisitorHash == visitor)?.Kind;
            var groupComments = comments.Where(c => c.AnnotationId == annotationId)
                .Select(c => new { c.Id, authorName = DisplayName(c.AuthorName), c.Text, c.CreatedAt, c.ParentCommentId });
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
            polls,
        };

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, result, JsonOpts);
    }

    // Opt-in DM to the post owner via the bot on genuinely new engagement (see the ADR following
    // ADR-039, docs/DECISIONS.md) — not on a toggled-off reaction, not on "dislike". Never lets a
    // failed/unreachable DM affect the anonymous visitor's request; only logs.
    private static async Task NotifyOwnerAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug, string message)
    {
        var bot = ctx.RequestServices.GetRequiredService<TelegramBotService>();
        if (!bot.IsRunning) return;

        var ownerId = site.OwnerId;
        var owner = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => new { u.TelegramUserId, u.NotifyOnEngagement }).FirstOrDefaultAsync();
        if (owner is not { NotifyOnEngagement: true, TelegramUserId: { } chatId }) return;

        try
        {
            await bot.Client.SendMessage(chatId, $"{message}\n{site.PostUrl(slug)}");
        }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILogger<TelegramBotService>>()
                .LogWarning(ex, "Engagement notification DM failed for owner {OwnerId}", ownerId);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static async Task PostReactionAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished);
        if (draft is null || !HasPrivateAccess(ctx, draft))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // T-039 — enforced here, not only by hiding the buttons: the controls are client-side
        // markup and a POST needs no button to be sent.
        if (draft.DisableReactions)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
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

        var isNewLike = existing is null && req.Kind == "like";
        if (existing is null)
            db.Reactions.Add(new Reaction { DraftId = draft.Id, OwnerId = draft.OwnerId, AnnotationId = annotationId, Kind = req.Kind, VisitorHash = visitor });
        else if (existing.Kind == req.Kind)
            db.Reactions.Remove(existing);
        else
            existing.Kind = req.Kind;
        await db.SaveChangesAsync();

        if (isNewLike)
            await NotifyOwnerAsync(ctx, db, site, slug, $"👍 Someone liked your post \"{draft.Title}\"");

        var group = await db.Reactions.Where(r => r.DraftId == draft.Id && r.AnnotationId == annotationId).ToListAsync();
        var counts = group.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
        var myVote = group.FirstOrDefault(r => r.VisitorHash == visitor)?.Kind;

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new { counts, myVote }, JsonOpts);
    }

    // NF5 — one vote per (poll, visitor). Changing your answer updates the existing row; there is
    // no "unvote", unlike a reaction toggle — a poll has no meaningful "no answer" state to return
    // to once you've picked one, the way a like does.
    private static async Task PostPollVoteAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished);
        if (draft is null || !HasPrivateAccess(ctx, draft))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        PollVoteRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<PollVoteRequest>(ctx.Request.Body, JsonOpts);
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (req is null || string.IsNullOrWhiteSpace(req.PollId) || string.IsNullOrWhiteSpace(req.Option))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var visitor = VisitorHash(ctx);
        var existing = await db.PollVotes.FirstOrDefaultAsync(v =>
            v.DraftId == draft.Id && v.PollId == req.PollId && v.VisitorHash == visitor);

        if (existing is null)
            db.PollVotes.Add(new PollVote { DraftId = draft.Id, OwnerId = draft.OwnerId, PollId = req.PollId, Option = req.Option, VisitorHash = visitor });
        else
            existing.Option = req.Option;
        await db.SaveChangesAsync();

        var group = await db.PollVotes.Where(v => v.DraftId == draft.Id && v.PollId == req.PollId).ToListAsync();
        var counts = group.GroupBy(v => v.Option).ToDictionary(g => g.Key, g => g.Count());
        var myVote = group.FirstOrDefault(v => v.VisitorHash == visitor)?.Option;

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new { counts, myVote }, JsonOpts);
    }

    // Registration-form submission on a private post (B3). Grants access immediately by setting
    // the same cookie a valid invite token sets — the form collects an audience, it isn't a
    // verification step (nothing confirms the email). See the ADR following ADR-041.
    private static async Task PostRegistrationAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished);
        // Only private posts that actually have a form configured accept submissions.
        // Validated against the form the visitor was actually shown (FI4.1) — a required question
        // that only exists in one language must not be enforced against a reader of another.
        var submitLang = ctx.Request.Query["lang"].FirstOrDefault() is { } sq && Languages.IsContentLanguage(sq)
            ? sq
            : draft?.PrimaryLanguage ?? Languages.Russian;
        if (draft is null || !draft.IsPrivate
            || RegistrationFormSet.Pick(draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson, submitLang) is not { } form)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        RegistrationRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<RegistrationRequest>(ctx.Request.Body, JsonOpts);
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (req is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var visitor = VisitorHash(ctx);
        var since = DateTime.UtcNow - Consts.RegistrationForm.SubmissionWindow;
        var recent = await db.PostRegistrations
            .CountAsync(r => r.DraftId == draft.Id && r.VisitorHash == visitor && r.CreatedAt >= since);
        if (recent >= Consts.RegistrationForm.MaxSubmissionsPerVisitor)
        {
            await WriteJsonErrorAsync(ctx, StatusCodes.Status429TooManyRequests, "Too many submissions — try again later.");
            return;
        }

        var name = Trim(req.Name);
        var nickname = Trim(req.Nickname);
        var email = Trim(req.Email);
        var social = Trim(req.SocialLink);

        // Mirror whatever the owner marked required — the client enforces it too, but a form
        // POST is trivially replayable outside the browser.
        if ((form.RequireName && name is null) || (form.RequireNickname && nickname is null)
            || (form.RequireEmail && email is null) || (form.RequireSocial && social is null))
        {
            await WriteJsonErrorAsync(ctx, StatusCodes.Status400BadRequest, "Please fill in every required field.");
            return;
        }
        if (form.RequireEmail && email is not null && !email.Contains('@'))
        {
            await WriteJsonErrorAsync(ctx, StatusCodes.Status400BadRequest, "Enter a valid email address.");
            return;
        }
        // N6 — applies whenever a name was given, not only when it was required: a junk name in
        // an optional field is still junk in the owner's audience list.
        if (name is not null && !RegistrationFieldValidator.IsValidName(name))
        {
            await WriteJsonErrorAsync(ctx, StatusCodes.Status400BadRequest,
                "Name must be at least 2 letters and may only contain letters, spaces and hyphens.");
            return;
        }

        var answers = req.Answers is { Count: > 0 }
            ? JsonSerializer.Serialize(req.Answers, JsonOpts)
            : null;
        if (answers is { Length: > Consts.RegistrationForm.AnswersJsonMaxChars })
        {
            await WriteJsonErrorAsync(ctx, StatusCodes.Status400BadRequest, "Answers are too long.");
            return;
        }
        foreach (var q in form.Questions.Where(q => q.Required))
        {
            var answered = req.Answers is not null && req.Answers.TryGetValue(q.Id, out var a) && !string.IsNullOrWhiteSpace(a)
                // A replayed POST can carry a literal empty array where the browser would have
                // sent "" — an unticked required checkbox group is still unanswered.
                && (q.Type != RegistrationQuestionType.Multi || MultiAnswer.Split(a).Count > 0);
            if (!answered)
            {
                await WriteJsonErrorAsync(ctx, StatusCodes.Status400BadRequest, "Please answer every required question.");
                return;
            }
        }

        var registration = new PostRegistration
        {
            OwnerId = draft.OwnerId,
            AccessToken = PrivateAccess.NewToken(),
            DraftId = draft.Id,
            Name = name,
            Nickname = nickname,
            Email = email,
            SocialLink = social,
            AnswersJson = answers,
            VisitorHash = visitor,
        };
        db.PostRegistrations.Add(registration);
        await db.SaveChangesAsync();

        // N11 — same opt-in plumbing as comment/like notifications (ADR-040): a failed or
        // unreachable DM only logs, it never turns a successful registration into an error.
        var who = name ?? nickname ?? email ?? "Someone";
        await NotifyOwnerAsync(ctx, db, site, slug, $"📝 {who} filled in the form for \"{draft.Title}\"");

        // T-033 — the respondent's copy, when they left an address and the owner wrote something
        // to send. Same rule as the owner's DM above: a mail failure never turns a successful
        // registration into an error, because the registration is what the reader is waiting on.
        if (!string.IsNullOrWhiteSpace(email) && form?.ResponseEmailBody is { Length: > 0 } responseBody)
        {
            var mailer = ctx.RequestServices.GetRequiredService<Email.ResendEmailProvider>();
            var subject = string.IsNullOrWhiteSpace(form.ResponseEmailSubject)
                ? draft.Title
                : form.ResponseEmailSubject!;
            try
            {
                // The owner's own words, escaped and line-broken — never rendered as HTML they
                // authored, because this is a form field, not a template editor.
                var html = "<p>" + System.Net.WebUtility.HtmlEncode(responseBody)
                    .Replace("\r\n", "<br>").Replace("\n", "<br>") + "</p>";
                await mailer.SendAsync(email!, subject, html);
            }
            catch (Exception ex)
            {
                ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FormResponseEmail")
                    .LogWarning(ex, "Could not send the form response email to {Email}", email);
            }
        }

        GrantPrivateAccess(ctx, draft.Id, registration.AccessToken);

        ctx.Response.StatusCode = StatusCodes.Status201Created;
        ctx.Response.ContentType = "application/json";
        // T-064 — the reader's own link. The cookie covers this browser; the link is what carries
        // the access to the next one, which is exactly what the reported incident needed.
        await JsonSerializer.SerializeAsync(ctx.Response.Body,
            new { ok = true, accessUrl = $"/{slug}?access={registration.AccessToken}" }, JsonOpts);
    }

    private static string? Trim(string? s)
    {
        var t = s?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > Consts.RegistrationForm.FieldMaxLength ? t[..Consts.RegistrationForm.FieldMaxLength] : t;
    }

    private static async Task WriteJsonErrorAsync(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new { error = message }, JsonOpts);
    }

    private static async Task PostCommentAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished);
        if (draft is null || !HasPrivateAccess(ctx, draft))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // T-039 — same reason as reactions above.
        if (draft.DisableComments)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
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

        // Reserve the channel owner's display name (Phase 8 Step 7) so a visitor can't post under
        // it and be mistaken for the real author — a plain case-insensitive match against the live
        // profile value, not a separate reservation table (see the ADR following ADR-035,
        // docs/DECISIONS.md). Skipped entirely when the owner hasn't set a display name.
        var ownerName = await db.Users.Where(u => u.Id == draft.OwnerId).Select(u => u.AuthorDisplayName).FirstAsync();
        if (!string.IsNullOrWhiteSpace(ownerName) && !string.IsNullOrWhiteSpace(authorName)
            && string.Equals(authorName, ownerName, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            ctx.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(ctx.Response.Body, new { error = ErrorMessages.NameReservedForAuthor }, JsonOpts);
            return;
        }

        var annotationId = string.IsNullOrEmpty(req?.AnnotationId) ? null : req.AnnotationId;

        // One level of nesting only — a reply's parent must itself be a top-level comment on the
        // same draft, otherwise silently treat the submission as top-level rather than erroring on
        // a stale/tampered parentId (see the ADR following ADR-035, docs/DECISIONS.md).
        Guid? parentCommentId = null;
        if (req?.ParentCommentId is { } pid)
        {
            var parent = await db.Comments.FirstOrDefaultAsync(c => c.Id == pid && c.DraftId == draft.Id);
            if (parent is not null && parent.ParentCommentId is null)
                parentCommentId = pid;
        }

        var comment = new Comment { DraftId = draft.Id, OwnerId = draft.OwnerId, AnnotationId = annotationId, AuthorName = authorName, Text = text, ParentCommentId = parentCommentId };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        await NotifyOwnerAsync(ctx, db, site, slug, $"💬 New comment on \"{draft.Title}\": {Truncate(text, 100)}");

        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = StatusCodes.Status201Created;
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new
        {
            comment.Id,
            authorName = DisplayName(comment.AuthorName),
            comment.Text,
            comment.CreatedAt,
            comment.ParentCommentId,
        }, JsonOpts);
    }

    private static string DisplayName(string? authorName) =>
        string.IsNullOrWhiteSpace(authorName) ? "Anonymous" : authorName;

    internal static List<string> SplitTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // The index's own chrome, in the language the reader picked (ADR-202). Only the languages this
    // table names can be offered as an index language — an English "Newest first" over a Russian
    // list is the mixed page the site-wide toggle was removed for.
    private sealed record IndexChrome(
        string Order, string SortNew, string SortOld, string SortPopular, string SortUnpopular,
        string LanguageGroup, string AllLanguages, string Available,
        string ShowMore, string NothingYet, string NoMatch, string ReadIn);

    private static readonly IReadOnlyDictionary<string, IndexChrome> IndexLabels =
        new Dictionary<string, IndexChrome>
        {
            ["en"] = new("Order", "Newest first", "Oldest first", "Most popular", "Least popular",
                "Language", "All languages", "available",
                "Show {0} more of {1}", "Nothing published yet.", "No posts match.", "Read in"),
            ["ru"] = new("Порядок", "Сначала новые", "Сначала старые", "Самые читаемые", "Наименее читаемые",
                "Язык", "Все языки", "есть перевод",
                "Показать ещё {0} из {1}", "Пока ничего не опубликовано.", "Ничего не найдено.", "Читать на"),
        };

    private static string TagFilterUrl(IEnumerable<string> tags)
    {
        var list = tags.Distinct().ToList();
        return list.Count == 0 ? "/" : "/?tags=" + string.Join(",", list.Select(Uri.EscapeDataString));
    }

    // ADR-192/193 — the index toolbar (sort dropdown, language filter, tag chips, "show more") all
    // link back into the same parameters, so any one of them can change without the others resetting.
    // Changing a filter drops `shown` on purpose — a narrower list should start from its own top.
    private static string IndexFilterUrl(IEnumerable<string> tags, string sort, string? avail, int? shown = null, string? lang = null)
    {
        var parts = new List<string>();
        var tagList = tags.Distinct().ToList();
        if (tagList.Count > 0) parts.Add("tags=" + string.Join(",", tagList.Select(Uri.EscapeDataString)));
        if (sort != "new") parts.Add("sort=" + sort);
        if (avail is not null) parts.Add("avail=" + Uri.EscapeDataString(avail));
        if (shown is > 10) parts.Add("shown=" + shown);
        // English is the default and says nothing, so the plain address stays the plain address.
        if (lang is not null && lang != Languages.English) parts.Add("lang=" + Uri.EscapeDataString(lang));
        return parts.Count == 0 ? "/" : "/?" + string.Join("&", parts);
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

    // The header identity is the blog owner's first channel by title. Ordered rather than "whichever
    // comes back first" so a second channel cannot rename the blog; letting an owner pick which one
    // speaks for the blog is a separate job and would need a column.
    private static async Task<BlogChannelInfo?> GetBlogChannelInfoAsync(CedarDbContext db, BlogSite site)
    {
        var channel = await db.Channels
            .Where(c => c.OwnerId == site.OwnerId)
            .OrderBy(c => c.Title).ThenBy(c => c.Id)
            .FirstOrDefaultAsync();
        if (channel is null)
            return null;

        var memberCount = await db.ChannelStatSnapshots
            .Where(s => s.ChannelId == channel.Id)
            .OrderByDescending(s => s.TakenAt)
            .Select(s => (int?)s.MemberCount)
            .FirstOrDefaultAsync();

        return new BlogChannelInfo(channel.Title, channel.Username, memberCount, channel.AvatarPath is null ? null : "/media/" + channel.AvatarPath);
    }

    private sealed record ReadingChrome(
        string Menu, string Theme, string Day, string Night, string System, string Size, string Face);

    // ADR-181 — the reading menu's four labels. English is the fallback for any code not listed,
    // the same rule the registration gate follows (ADR-050): an untranslated menu in English beats
    // one in a language the reader definitely did not ask for. Day/Night rather than Light/Dark in
    // every language on purpose — the product's own word for its two looks is the lamp, not the
    // luminance, and a reader who sees "Ночь" here reads the same word the app uses.
    //
    // T-013's caveat holds for uk/be/ka exactly as it does on the gate: no native speaker has read
    // them, and they are here because an English menu on a Ukrainian post is the worse default.
    //
    // ADR-192 adds Face — the group label above the serif/sans row. The two buttons underneath it
    // are not translated: they read "Literata" and "Source Sans 3", the faces' own names, the same
    // choice the app's own typeface picker makes.
    private static readonly IReadOnlyDictionary<string, ReadingChrome> ReadingLabels =
        new Dictionary<string, ReadingChrome>
        {
            ["ru"] = new("Чтение", "Тема", "День", "Ночь", "Система", "Размер текста", "Гарнитура"),
            ["en"] = new("Reading", "Theme", "Day", "Night", "System", "Text size", "Face"),
            ["de"] = new("Lesen", "Design", "Tag", "Nacht", "System", "Textgröße", "Schriftart"),
            ["fr"] = new("Lecture", "Thème", "Jour", "Nuit", "Système", "Taille du texte", "Police"),
            ["es"] = new("Lectura", "Tema", "Día", "Noche", "Sistema", "Tamaño del texto", "Tipografía"),
            ["ja"] = new("表示", "テーマ", "昼", "夜", "システム", "文字サイズ", "書体"),
            ["uk"] = new("Читання", "Тема", "День", "Ніч", "Системна", "Розмір тексту", "Гарнітура"),
            ["be"] = new("Чытанне", "Тэма", "Дзень", "Ноч", "Сістэмная", "Памер тэксту", "Гарнітура"),
            ["ka"] = new("კითხვა", "თემა", "დღე", "ღამე", "სისტემური", "ტექსტის ზომა", "შრიფტი"),
        };

    private static string RenderHeader(BlogChannelInfo? channel, string lang)
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
            // The channel's own picture when it has been copied down, the initial letter as the
            // plate it always had otherwise — a header that waits for a download is worse than a
            // header with a letter in it.
            var face = channel.AvatarUrl is null
                ? $"<div class=\"channel-avatar\">{System.Net.WebUtility.HtmlEncode(initial)}</div>"
                : $"<img class=\"channel-avatar channel-photo\" src=\"{System.Net.WebUtility.HtmlEncode(channel.AvatarUrl)}\" alt=\"\" width=\"30\" height=\"30\">";
            identity = $"""
                {face}
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

        // The feed already existed (ADR-024) and was reachable only by the <link rel="alternate">
        // in <head> — i.e. by a reader that already knew to look. A visible button is the whole
        // difference between "there is a feed" and "you can subscribe".
        const string rssButton = """
            <a class="tg-open-btn rss-btn" href="/rss.xml" title="RSS feed">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><circle cx="6.2" cy="17.8" r="2.2"></circle><path d="M4 10.2v3.1a6.7 6.7 0 0 1 6.7 6.7h3.1A9.8 9.8 0 0 0 4 10.2Z"></path><path d="M4 4v3.1A12.9 12.9 0 0 1 16.9 20H20A16 16 0 0 0 4 4Z"></path></svg>
            <span class="tg-open-label">RSS</span>
            </a>
            """;

        var reading = ReadingMenuHtml(lang);

        return $"""
            <div class="site-header"><div class="site-header-inner">
            {identity}
            <div class="spacer"></div>
            {rssButton}
            {openInTelegram}
            {reading}
            </div></div>
            """;
    }

    // ADR-181 — one control for the two things a reader may change about the page, in the place the
    // theme toggle used to stand alone. Everything it sets is written on <html> and remembered in
    // localStorage; the head script applies both before the first paint, so neither flashes.
    private static string ReadingMenuHtml(string lang)
    {
        var t = ReadingLabels.TryGetValue(lang, out var found) ? found : ReadingLabels["en"];
        string Esc(string v) => System.Net.WebUtility.HtmlEncode(v);

        return $"""
            <div class="reading-anchor">
            <button type="button" class="reading-btn" id="readingBtn" aria-haspopup="true" aria-expanded="false"
                    aria-controls="readingMenu" title="{Esc(t.Menu)}" aria-label="{Esc(t.Menu)}">Aa</button>
            <div class="reading-menu" id="readingMenu" role="group" aria-label="{Esc(t.Menu)}" hidden>
            <div class="reading-title">{Esc(t.Menu)}</div>
            <div class="reading-group">
            <div class="reading-label" id="readingThemeLabel">{Esc(t.Theme)}</div>
            <div class="seg" role="group" aria-labelledby="readingThemeLabel" data-seg="theme">
            <button type="button" data-value="light" aria-pressed="false">{Esc(t.Day)}</button>
            <button type="button" data-value="dark" aria-pressed="false">{Esc(t.Night)}</button>
            <button type="button" data-value="" aria-pressed="false">{Esc(t.System)}</button>
            </div>
            </div>
            <div class="reading-group">
            <div class="reading-label" id="readingSizeLabel">{Esc(t.Size)}</div>
            <!-- The three A's are the control: each is drawn at the size it sets, which says what
                 the step does without a word that would need translating. -->
            <div class="seg seg-size" role="group" aria-labelledby="readingSizeLabel" data-seg="read">
            <button type="button" data-value="s" aria-pressed="false"><span style="font-size:13px">A</span></button>
            <button type="button" data-value="m" aria-pressed="false"><span style="font-size:15px">A</span></button>
            <button type="button" data-value="l" aria-pressed="false"><span style="font-size:18px">A</span></button>
            </div>
            </div>
            <div class="reading-group">
            <div class="reading-label" id="readingFaceLabel">{Esc(t.Face)}</div>
            <!-- ADR-192 — the two names are the faces' own, unlocalized, same as the app's own picker. -->
            <div class="seg seg-face" role="group" aria-labelledby="readingFaceLabel" data-seg="face">
            <button type="button" data-value="" aria-pressed="false" style="font-family:var(--font-serif)">Literata</button>
            <button type="button" data-value="sans" aria-pressed="false" style="font-family:var(--font-sans)">Source Sans 3</button>
            </div>
            </div>
            </div>
            </div>
            """;
    }

    private static async Task RenderIndexAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        // ADR-202 — the index, and only the index, carries a language pick again: it sets the chrome
        // and which translation a card previews in. English is the default and the fallback, and a
        // post page still has no such control — there the post's own switch is the answer.
        var pickedLang = ctx.Request.Query["lang"].FirstOrDefault();

        // Private posts never appear in the public list (see the ADR following ADR-040,
        // docs/DECISIONS.md) — listing one would leak its existence even though the single-post
        // page itself 404s for anyone not invited.
        // A private post is listed only when its owner asked for it (IsListedWhilePrivate) — the
        // card advertises that the post exists, the gate still decides who reads it.
        var posts = await db.Drafts.Where(d => d.OwnerId == site.OwnerId && d.IsBlogPublished && (!d.IsPrivate || d.IsListedWhilePrivate))
            .OrderByDescending(d => d.BlogPublishedAt)
            .Select(d => new
            {
                d.Id, d.Title, d.ArticleTitle, d.BlogSlug, d.BlogPublishedAt, d.Tags, d.CedarJson, d.ViewCount,
                d.IsPrivate, d.PrimaryLanguage,
                TranslationLanguages = db.DraftTranslations.Where(t => t.DraftId == d.Id).Select(t => t.Language).ToList(),
            })
            .ToListAsync();

        // Every language any post is either written in or translated into, narrowed to the ones the
        // index chrome has words for: offering a language the toolbar cannot speak would produce
        // exactly the half-translated page ADR-193 removed.
        var offeredLangs = posts.Select(p => p.PrimaryLanguage).Concat(posts.SelectMany(p => p.TranslationLanguages))
            .Where(IndexLabels.ContainsKey).Distinct().OrderBy(l => l == Languages.English ? 0 : 1).ThenBy(l => l).ToList();
        var indexLang = pickedLang is not null && offeredLangs.Contains(pickedLang) ? pickedLang : Languages.English;
        var chrome = IndexLabels[IndexLabels.ContainsKey(indexLang) ? indexLang : Languages.English];

        var postIds = posts.Select(p => p.Id).ToList();
        // One batched lookup for every post's translation into the index language, not N+1 — the
        // same shape as the wikilink-target query below it in RenderPostAsync.
        var previews = postIds.Count == 0
            ? new Dictionary<Guid, DraftTranslation>()
            : await db.DraftTranslations.Where(t => postIds.Contains(t.DraftId) && t.Language == indexLang)
                .ToDictionaryAsync(t => t.DraftId, t => t);

        var likeCounts = await db.Reactions.Where(r => r.OwnerId == site.OwnerId && r.Kind == "like")
            .GroupBy(r => r.DraftId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        var commentCounts = await db.Comments.Where(c => c.OwnerId == site.OwnerId)
            .GroupBy(c => c.DraftId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

        var allTags = posts.SelectMany(p => SplitTags(p.Tags)).Distinct().OrderBy(t => t).ToList();
        var selectedTags = (ctx.Request.Query["tags"].FirstOrDefault() ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(allTags.Contains)
            .Distinct()
            .ToList();

        // Every language any post is either written in or translated into — what "available in
        // language X" in the sort dropdown can actually mean.
        var allLanguages = posts.Select(p => p.PrimaryLanguage).Concat(posts.SelectMany(p => p.TranslationLanguages))
            .Distinct().OrderBy(l => l).ToList();
        var avail = ctx.Request.Query["avail"].FirstOrDefault();
        avail = avail is not null && allLanguages.Contains(avail) ? avail : null;

        var sortOptions = new (string Key, string Label)[]
        {
            ("new", chrome.SortNew), ("old", chrome.SortOld),
            ("popular", chrome.SortPopular), ("unpopular", chrome.SortUnpopular),
        };
        var sort = ctx.Request.Query["sort"].FirstOrDefault();
        sort = sortOptions.Any(o => o.Key == sort) ? sort! : "new";

        var filtered = selectedTags.Count == 0
            ? posts
            : posts.Where(p => { var pt = SplitTags(p.Tags); return selectedTags.All(pt.Contains); }).ToList();

        if (avail is not null)
            filtered = filtered.Where(p => p.PrimaryLanguage == avail || p.TranslationLanguages.Contains(avail)).ToList();

        // The DB query above already sorts newest-first, which "new" keeps as-is; the others
        // reorder the already-filtered, already-materialized list rather than re-querying, since
        // the whole set is small enough that a second round trip would buy nothing.
        filtered = sort switch
        {
            "old" => Enumerable.Reverse(filtered).ToList(),
            "popular" => filtered.OrderByDescending(p => p.ViewCount).ToList(),
            "unpopular" => filtered.OrderBy(p => p.ViewCount).ToList(),
            _ => filtered,
        };

        // The index used to render every published post on one page, which got slow as the list
        // grew. "Show 10 more" reveals the next batch via a plain link (?shown=), not client-side
        // JS: the HTML this page sends is only ever as big as what the reader asked to see.
        var shown = int.TryParse(ctx.Request.Query["shown"].FirstOrDefault(), out var shownParsed) && shownParsed > 10
            ? shownParsed
            : 10;
        var pageItems = filtered.Take(shown).ToList();
        var remaining = filtered.Count - pageItems.Count;

        var sb = new StringBuilder();

        // ADR-193 — the sort/filter control: a paper popover off one button, the same open/close
        // mechanism the reading menu already uses, extended to cover order and language-availability
        // together rather than as two separate controls.
        var currentSortLabel = sortOptions.First(o => o.Key == sort).Label;
        // The order control and the tag leaves are one row: they are the two things a reader may
        // change about this list, and stacking them put a lone button on a line of its own above a
        // block of chips that answered the same question.
        sb.Append("<div class=\"index-bar\">");
        // The pick lives here and only here: on a post page the language is the post's own, and a
        // second control saying the same thing differently is how the site-wide one went wrong.
        if (offeredLangs.Count > 1)
        {
            sb.Append("<div class=\"index-lang\" role=\"group\" aria-label=\"").Append(chrome.ReadIn).Append("\">");
            foreach (var l in offeredLangs)
            {
                sb.Append("<a class=\"lang-chip").Append(l == indexLang ? " current" : "").Append("\"")
                  .Append(l == indexLang ? " aria-current=\"true\"" : "")
                  .Append(" href=\"").Append(IndexFilterUrl(selectedTags, sort, avail, null, l)).Append("\">")
                  .Append(l.ToUpperInvariant()).Append("</a>");
            }
            sb.Append("</div>");
        }
        if (allTags.Count > 0)
        {
            sb.Append("<div class=\"tag-bar\">");
            foreach (var tag in allTags)
            {
                var chipSelected = selectedTags.Contains(tag);
                var chipToggled = chipSelected ? selectedTags.Where(t => t != tag) : selectedTags.Append(tag);
                sb.Append("<a class=\"tag-chip").Append(chipSelected ? " selected" : "").Append("\" href=\"")
                  .Append(IndexFilterUrl(chipToggled, sort, avail, null, indexLang)).Append("\">#")
                  .Append(System.Net.WebUtility.HtmlEncode(tag)).Append(chipSelected ? " &times;" : "").Append("</a>");
            }
            sb.Append("</div>");
        }
        sb.Append("<div class=\"index-toolbar\">");
        // Item 2 — the way into /search from the one page every reader starts on. A plain GET
        // form, same as every other control on this bar.
        sb.Append(SearchFormHtml("", indexLang != Languages.Russian));
        sb.Append("<div class=\"sort-anchor\">");
        sb.Append("<button type=\"button\" class=\"index-sort-btn\" id=\"sortBtn\" aria-haspopup=\"true\" aria-expanded=\"false\" aria-controls=\"sortMenu\">")
          .Append(BlogIcons.Sort).Append("<span>").Append(currentSortLabel).Append("</span></button>");
        sb.Append("<div class=\"sort-menu\" id=\"sortMenu\" role=\"group\" hidden>");
        sb.Append("<div class=\"reading-label\">").Append(chrome.Order).Append("</div><div class=\"sort-menu-group\">");
        foreach (var (key, label) in sortOptions)
        {
            sb.Append("<a class=\"sort-menu-item").Append(key == sort ? " current" : "").Append("\" href=\"")
              .Append(IndexFilterUrl(selectedTags, key, avail, null, indexLang)).Append("\">").Append(label).Append("</a>");
        }
        sb.Append("</div>");
        if (allLanguages.Count > 1)
        {
            sb.Append("<div class=\"reading-label\">").Append(chrome.LanguageGroup).Append("</div><div class=\"sort-menu-group\">");
            sb.Append("<a class=\"sort-menu-item").Append(avail is null ? " current" : "").Append("\" href=\"")
              .Append(IndexFilterUrl(selectedTags, sort, null, null, indexLang)).Append("\">").Append(chrome.AllLanguages).Append("</a>");
            foreach (var l in allLanguages)
            {
                sb.Append("<a class=\"sort-menu-item").Append(avail == l ? " current" : "").Append("\" href=\"")
                  .Append(IndexFilterUrl(selectedTags, sort, l, null, indexLang)).Append("\">")
                  .Append(l.ToUpperInvariant()).Append(' ').Append(chrome.Available).Append("</a>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></div></div></div>");

        // T-294 — the games this blog is about, above the posts that are about them. Without it the
        // showcase is a page only a reader who already knows its URL can reach.
        sb.Append(await RenderGamesStripAsync(db, site, indexLang));

        if (pageItems.Count == 0)
        {
            sb.Append("<p class=\"empty\">").Append(posts.Count == 0 ? chrome.NothingYet : chrome.NoMatch).Append("</p>");
        }
        else
        {
            sb.Append("<div class=\"post-list timeline\">");
            string? lastMonthKey = null;
            foreach (var p in pageItems)
            {
                // The month spine only makes sense under "newest/oldest first" — under a popularity
                // sort, consecutive cards are not chronologically adjacent, so a heading here would
                // claim an order the list is not actually in.
                if (sort is "new" or "old")
                {
                    var monthKey = DisplayTime.ToZone(p.BlogPublishedAt)?.ToString("yyyy-MM") ?? "";
                    if (monthKey != lastMonthKey)
                    {
                        lastMonthKey = monthKey;
                        if (p.BlogPublishedAt is { } monthDate)
                        {
                            var monthLabel = BlogDateFormatter.MonthHeadingLocal(monthDate, indexLang);
                            sb.Append("<div class=\"timeline-month-sep\"><span class=\"sep-line\"></span><span class=\"sep-label\">")
                              .Append(monthLabel).Append("</span><span class=\"sep-line\"></span></div>");
                        }
                    }
                }

                var tags = SplitTags(p.Tags);
                var likes = likeCounts.GetValueOrDefault(p.Id);
                var comments = commentCounts.GetValueOrDefault(p.Id);
                // I1 — an English reader sees an English preview when the post has one, the same
                // translation `RenderPostAsync` would show them; the primary-language draft is what
                // renders when there is no translation, exactly as it always has.
                var preview = p.PrimaryLanguage != indexLang && previews.TryGetValue(p.Id, out var t) ? t : null;
                var cardTitle = preview?.Title ?? (p.ArticleTitle ?? p.Title);
                var cardCedarJson = preview?.CedarJson ?? p.CedarJson;
                // No teaser for a gated post: the card says a post exists and what it is called,
                // and the excerpt is the one part of it that would be actual content. Deliberate,
                // and the easy thing to reverse if the teaser turns out to be the point.
                var excerpt = p.IsPrivate ? "" : Excerpt(cardCedarJson);

                sb.Append("<div class=\"timeline-item\"><span class=\"timeline-dot\"></span>");
                sb.Append("<a class=\"post-card\" href=\"/").Append(p.BlogSlug).Append("\">");
                sb.Append("<div class=\"post-card-meta\">");
                sb.Append("<span class=\"post-card-date\">")
                  .Append(p.BlogPublishedAt is { } cardDate ? BlogDateFormatter.DateLocal(cardDate, indexLang) : "")
                  .Append("</span>");

                // T-186 — the primary badge said "RU" for every post while the primary language has
                // been per-draft since ADR-064. A translation row must never shadow the primary
                // (ADR-065), but the query does not enforce it, so filter rather than trust.
                sb.Append("<span class=\"post-card-langs\">").Append(p.PrimaryLanguage.ToUpperInvariant());
                foreach (var lang in p.TranslationLanguages.Where(l => l != p.PrimaryLanguage).OrderBy(l => l))
                    sb.Append(" · ").Append(lang.ToUpperInvariant());
                sb.Append("</span>");

                // Idea #8 — every tag, not just the first. The single-post page's own tag row
                // has always shown them all; the card silently truncated to tags[0], so a post
                // filed under three tags looked like it had one.
                foreach (var tag in tags)
                    sb.Append("<span class=\"post-card-tag\">· ").Append(System.Net.WebUtility.HtmlEncode(tag)).Append("</span>");

                if (p.IsPrivate)
                    sb.Append("<span class=\"post-card-locked\">").Append(BlogIcons.Lock).Append("</span>");

                sb.Append("</div>");
                sb.Append("<div class=\"post-card-title\">").Append(System.Net.WebUtility.HtmlEncode(cardTitle)).Append("</div>");
                if (excerpt.Length > 0)
                    sb.Append("<div class=\"post-card-excerpt\">").Append(System.Net.WebUtility.HtmlEncode(excerpt)).Append("</div>");
                sb.Append("<div class=\"post-card-stats\">").Append(BlogIcons.Eye).Append("<span class=\"num\">").Append(p.ViewCount)
                  .Append("</span>").Append(BlogIcons.ThumbUp).Append("<span class=\"num\">").Append(likes)
                  .Append("</span>").Append(BlogIcons.Chat).Append("<span class=\"num\">").Append(comments).Append("</span></div>");
                sb.Append("</a></div>");
            }
            sb.Append("</div>");

            if (remaining > 0)
            {
                var moreUrl = IndexFilterUrl(selectedTags, sort, avail, shown + 10, indexLang);
                sb.Append("<div class=\"show-more-row\"><a class=\"show-more-btn\" href=\"").Append(moreUrl)
                  .Append("\">")
                  .Append(string.Format(chrome.ShowMore, Math.Min(10, remaining), remaining))
                  .Append("</a></div>");
            }
        }

        // Item 7 — the subscribe box on the index foot; the post page carries its twin.
        sb.Append(RenderSubscribeBox(ctx, indexLang != Languages.Russian, "/"));

        var channel = await GetBlogChannelInfoAsync(db, site);
        var blogBase = site.BaseUrl;
        var indexMeta = OgMetaBuilder.Build(new OgMetaInput(
            channel?.Title ?? "Blog", null, blogBase + "/",
            $"{blogBase}/og-default.png", 1200, 630,
            channel?.Title ?? "Cedar Clerk", indexLang,
            [], null, null, null, IsArticle: false), OgMetaPolicy.Full);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell("Blog", sb.ToString(), indexLang, RenderHeader(channel, indexLang), indexMeta));
    }

    // ADR-125 — the series landing: what the index would show, narrowed to one series and ordered
    // by its own order rather than by date. An empty (or fully invisible) series is an honest
    // empty page, not a 404 — the URL is printed on every member post.
    private static async Task RenderSeriesAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var channel = await GetBlogChannelInfoAsync(db, site);
        // Series.Slug is unique per owner, so the owner is part of the lookup and not an extra check.
        var series = await db.Series.FirstOrDefaultAsync(s => s.Slug == slug && s.OwnerId == site.OwnerId);
        if (series is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(PageShell("Not found", "<p class=\"empty\">Series not found.</p>", Languages.Russian, RenderHeader(channel, Languages.Russian)));
            return;
        }

        var pageLang = ctx.Request.Query["lang"].ToString() is { Length: > 0 } requested
                       && Languages.IsContentLanguage(requested)
            ? requested
            : Languages.Russian;
        var en = pageLang == Languages.English;

        var posts = await db.Drafts
            .Where(d => d.OwnerId == site.OwnerId && d.SeriesId == series.Id && d.IsBlogPublished && (!d.IsPrivate || d.IsListedWhilePrivate))
            .OrderBy(d => d.SeriesOrder).ThenBy(d => d.BlogPublishedAt)
            .Select(d => new { d.Title, d.ArticleTitle, d.BlogSlug, d.BlogPublishedAt, d.CedarJson, d.IsPrivate, d.ViewCount })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.Append("<div class=\"series-head\"><h1>").Append(System.Net.WebUtility.HtmlEncode(series.Name)).Append("</h1>");
        if (series.Description is { Length: > 0 } desc)
            sb.Append("<p class=\"series-desc\">").Append(System.Net.WebUtility.HtmlEncode(desc)).Append("</p>");
        sb.Append("<span class=\"series-count\">").Append(posts.Count).Append(en ? " parts" : " частей").Append("</span></div>");

        if (posts.Count == 0)
        {
            sb.Append(en ? "<p class=\"empty\">Nothing published in this series yet.</p>"
                         : "<p class=\"empty\">В этой серии пока ничего не опубликовано.</p>");
        }
        else
        {
            sb.Append("<div class=\"post-list timeline\">");
            var part = 0;
            foreach (var p in posts)
            {
                part++;
                var excerpt = p.IsPrivate ? "" : Excerpt(p.CedarJson);
                sb.Append("<div class=\"timeline-item\"><span class=\"timeline-dot\"></span>");
                sb.Append("<a class=\"post-card\" href=\"/").Append(p.BlogSlug).Append("\">");
                sb.Append("<div class=\"post-card-meta\">");
                sb.Append("<span class=\"series-part-no\">").Append(en ? "Part " : "Часть ").Append(part).Append("</span>");
                sb.Append("<span class=\"post-card-date\">")
                  .Append(p.BlogPublishedAt is { } cardDate ? BlogDateFormatter.DateLocal(cardDate, pageLang) : "")
                  .Append("</span>");
                if (p.IsPrivate)
                    sb.Append("<span class=\"post-card-locked\">").Append(BlogIcons.Lock).Append("</span>");
                sb.Append("</div>");
                sb.Append("<div class=\"post-card-title\">").Append(System.Net.WebUtility.HtmlEncode(p.ArticleTitle ?? p.Title)).Append("</div>");
                if (excerpt.Length > 0)
                    sb.Append("<div class=\"post-card-excerpt\">").Append(System.Net.WebUtility.HtmlEncode(excerpt)).Append("</div>");
                sb.Append("</a></div>");
            }
            sb.Append("</div>");
        }

        var backLinkLabel = en ? "All posts" : "Все посты";
        var body = $"<a class=\"back-link\" href=\"/\">&larr; {backLinkLabel}</a>{sb}";

        var blogBase = site.BaseUrl;
        var meta = OgMetaBuilder.Build(new OgMetaInput(
            series.Name, series.Description, $"{blogBase}/series/{series.Slug}",
            $"{blogBase}/og-default.png", 1200, 630,
            channel?.Title ?? "Cedar Clerk", pageLang,
            [], null, null, null, IsArticle: false), OgMetaPolicy.Full);

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(series.Name, body, pageLang, RenderHeader(channel, pageLang), meta));
    }

    // ADR-134 (T-159) — the public game page. Everything on it is opt-in: the page exists only
    // while ShowcaseSlug is set and the project is not archived; the feed reuses the index's exact
    // visibility filter, so this surface shows nothing the index does not; the roadmap carries only
    // ticked tasks, title and status — a task's description is working material and never leaves.
    /// <summary>`Label|https://url` lines; anything not shaped like that is skipped, not rendered.</summary>
    public static List<(string Label, string Url)> ParseShowcaseLinks(string raw)
    {
        var links = new List<(string, string)>();
        foreach (var line in raw.Split('\n'))
        {
            var split = line.IndexOf('|');
            if (split <= 0) continue;
            var label = line[..split].Trim();
            var url = line[(split + 1)..].Trim();
            if (label.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) continue;
            links.Add((label, url));
        }
        return links;
    }

    private static async Task RenderRssAsync(HttpContext ctx, CedarDbContext db, BlogSite site)
    {
        // Public posts only, including the listed-private ones: an RSS item carries an excerpt
        // and is pulled by readers that never see the gate, so listing a gated post here would
        // hand out the thing the gate exists to withhold.
        var posts = await db.Drafts.Where(d => d.OwnerId == site.OwnerId && d.IsBlogPublished && !d.IsPrivate)
            .OrderByDescending(d => d.BlogPublishedAt)
            .Take(RssItemLimit)
            .Select(d => new { d.Title, d.ArticleTitle, d.BlogSlug, d.BlogPublishedAt, d.CedarJson })
            .ToListAsync();

        var channel = await GetBlogChannelInfoAsync(db, site);
        var siteTitle = System.Net.WebUtility.HtmlEncode(channel?.Title ?? "Cedar Clerk Blog");
        var siteUrl = $"{site.BaseUrl}/";

        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""").Append('\n');
        sb.Append("<rss version=\"2.0\"><channel>");
        sb.Append("<title>").Append(siteTitle).Append("</title>");
        sb.Append("<link>").Append(siteUrl).Append("</link>");
        sb.Append("<description>").Append(siteTitle).Append("</description>");
        sb.Append("<language>ru</language>");
        sb.Append("<atom:link xmlns:atom=\"http://www.w3.org/2005/Atom\" href=\"").Append(siteUrl)
          .Append("rss.xml\" rel=\"self\" type=\"application/rss+xml\" />");

        foreach (var p in posts)
        {
            var url = $"{siteUrl}{p.BlogSlug}";
            var excerpt = Excerpt(p.CedarJson);
            sb.Append("<item>");
            sb.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(p.ArticleTitle ?? p.Title)).Append("</title>");
            sb.Append("<link>").Append(url).Append("</link>");
            sb.Append("<guid isPermaLink=\"true\">").Append(url).Append("</guid>");
            if (p.BlogPublishedAt is { } published)
                sb.Append("<pubDate>").Append(published.ToString("R", CultureInfo.InvariantCulture)).Append("</pubDate>");
            if (excerpt.Length > 0)
                sb.Append("<description>").Append(System.Net.WebUtility.HtmlEncode(excerpt)).Append("</description>");
            sb.Append("</item>");
        }

        sb.Append("</channel></rss>");

        ctx.Response.ContentType = "application/rss+xml; charset=utf-8";
        await ctx.Response.WriteAsync(sb.ToString());
    }

    private static async Task RenderPostAsync(HttpContext ctx, CedarDbContext db, BlogSite site, string slug)
    {
        var channel = await GetBlogChannelInfoAsync(db, site);
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.OwnerId == site.OwnerId && d.IsBlogPublished);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(PageShell("Not found", "<p class=\"empty\">Post not found.</p>", Languages.Russian, RenderHeader(channel, Languages.Russian)));
            return;
        }

        var blogBase = site.BaseUrl;

        // ADR-124 — the gate and the private not-found page are exactly what a crawler sees: a
        // *listed* private post still advertises its title and the stock image there; an unlisted
        // one reveals nothing, same as a nonexistent slug.
        // Item 1 — the generated card replaces the static og-default here too: TitleImageOnly
        // already puts the title in the meta, so a card drawing that same title reveals nothing new.
        string SemiPublicMeta(string pageLang) => draft.IsListedWhilePrivate
            ? OgMetaBuilder.Build(new OgMetaInput(
                draft.ArticleTitle ?? draft.Title, null, $"{blogBase}/{draft.BlogSlug}",
                OgImageEndpoint.ImageUrl(site, draft.BlogSlug!, draft.ArticleTitle ?? draft.Title, channel?.Title ?? "Cedar Clerk"), 1200, 630,
                channel?.Title ?? "Cedar Clerk", pageLang, [], null, null, null, IsArticle: true),
                OgMetaPolicy.TitleImageOnly)
            : "";

        // Private posts (see the ADR following ADR-040, docs/DECISIONS.md): a ?invite= token
        // matching one of this draft's PostInvites grants a long-lived cookie; anything else gets
        // the exact same "Post not found" as a nonexistent slug, so a private post's existence
        // isn't distinguishable from a 404 to anyone not invited.
        if (!HasPrivateAccess(ctx, draft))
        {
            var inviteToken = ctx.Request.Query["invite"].FirstOrDefault();
            var validInvite = inviteToken is not null
                && await db.PostInvites.AnyAsync(pi => pi.DraftId == draft.Id && pi.Token == inviteToken);

            // T-064 — a reader's own key from an earlier registration, which is what makes the
            // link work in a second browser. Revocable per reader, unlike the owner's invites.
            var accessToken = ctx.Request.Query["access"].FirstOrDefault();
            var grantToken = inviteToken;
            if (!validInvite && accessToken is not null
                && await db.PostRegistrations.AnyAsync(r => r.DraftId == draft.Id && r.AccessToken == accessToken && !r.IsRevoked))
            {
                validInvite = true;
                grantToken = accessToken;
            }

            if (!validInvite)
            {
                // With a registration form configured the post is "locked", not "hidden" (B3) —
                // a deliberate departure from the indistinguishable-from-404 behaviour above,
                // which still applies when no form is set. See the ADR following ADR-041.
                // FI4.1 — the gate answers in the language the reader asked for: their own form
                // if the owner wrote one for it, the primary-language form otherwise. It used to
                // hardcode the primary language, so an EN reader of a private post was greeted
                // in Russian even when an EN form existed.
                var gateLang = ctx.Request.Query["lang"].FirstOrDefault() is { } q && Languages.IsContentLanguage(q)
                    ? q
                    : draft.PrimaryLanguage;
                if (RegistrationFormSet.Pick(draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson, gateLang) is { } form)
                {
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    var gateTitle = draft.ArticleTitle ?? draft.Title;
                    // The reader of a private post can't reach another language any other way -
                    // the body they would normally switch from is behind this very form.
                    var gateLanguages = RegistrationFormSet.LanguagesWithForm(
                        draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson);
                    await ctx.Response.WriteAsync(PageShell(gateTitle,
                        CedarToBlogHtmlRenderer.RegistrationFormHtml(form, gateTitle, gateLang, gateLanguages),
                        gateLang, RenderHeader(channel, gateLang), SemiPublicMeta(gateLang)));
                    return;
                }

                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(PageShell("Not found", "<p class=\"empty\">Post not found.</p>", Languages.Russian, RenderHeader(channel, Languages.Russian), SemiPublicMeta(Languages.Russian)));
                return;
            }

            GrantPrivateAccess(ctx, draft.Id, grantToken ?? "");
        }

        // Atomic UPDATE (not draft.ViewCount++ + SaveChanges) so concurrent page views don't lose
        // updates to each other. Shared across RU/EN — see ADR-023, docs/DECISIONS.md.
        // Gated by a short-lived per-post cookie so switching RU<->EN (a full page reload back
        // to this same handler) doesn't count as an extra view.
        var viewedCookieName = Consts.General.ViewedCookiePrefix + draft.Id;
        var viewCount = draft.ViewCount;
        if (!ctx.Request.Cookies.ContainsKey(viewedCookieName))
        {
            viewCount++;
            await db.Drafts.Where(d => d.Id == draft.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ViewCount, d => d.ViewCount + 1));
            ctx.Response.Cookies.Append(viewedCookieName, "1", new CookieOptions
            {
                MaxAge = TimeSpan.FromMinutes(30),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });
            await RecordViewGeoAsync(db, ctx, draft.OwnerId);
        }

        var availableLanguages = await db.DraftTranslations.Where(t => t.DraftId == draft.Id)
            .Select(t => t.Language)
            .ToListAsync();

        var requestedLang = ctx.Request.Query["lang"].FirstOrDefault();
        var lang = draft.PrimaryLanguage;
        // Idea #4 - the reader sees the article title when one is set; draft.Title is the name
        // the owner files it under, which is not the same thing.
        var title = draft.ArticleTitle ?? draft.Title;
        var cedarJson = draft.CedarJson;
        var notTranslatedNotice = "";
        if (requestedLang is not null && requestedLang != draft.PrimaryLanguage && Languages.ContentLanguages.Contains(requestedLang))
        {
            if (availableLanguages.Contains(requestedLang))
            {
                var translation = await db.DraftTranslations.FirstAsync(t => t.DraftId == draft.Id && t.Language == requestedLang);
                lang = translation.Language;
                title = translation.Title;
                cedarJson = translation.CedarJson;
            }
            else
            {
                // Requested a translation that doesn't exist (stale link, or removed after
                // sharing) — fall back to showing the original rather than a blank/broken page.
                notTranslatedNotice = "<div class=\"not-translated-notice\">Not translated yet — showing the original.</div>";
            }
        }

        var langSwitch = "";
        if (availableLanguages.Count > 0)
        {
            var items = new List<string>();
            items.Add(lang == draft.PrimaryLanguage
                ? $"<span class=\"lang-switch-btn current\">{draft.PrimaryLanguage.ToUpperInvariant()}</span>"
                : $"<a class=\"lang-switch-btn\" href=\"/{draft.BlogSlug}\">{draft.PrimaryLanguage.ToUpperInvariant()}</a>");
            foreach (var l in availableLanguages.OrderBy(l => l))
            {
                items.Add(lang == l
                    ? $"<span class=\"lang-switch-btn current\">{l.ToUpperInvariant()}</span>"
                    : $"<a class=\"lang-switch-btn\" href=\"/{draft.BlogSlug}?lang={l}\">{l.ToUpperInvariant()}</a>");
            }
            langSwitch = $"<div class=\"lang-switch-track\">{string.Join("", items)}</div>";
        }

        var tags = SplitTags(draft.Tags);
        var tagsRow = tags.Count == 0 ? "" :
            "<div class=\"post-tags-row\">" + string.Join("", tags.Select(t =>
                $"<a class=\"post-tag-chip\" href=\"{TagFilterUrl([t])}\">#{System.Net.WebUtility.HtmlEncode(t)}</a>")) + "</div>";

        var owner = await db.Users.Where(u => u.Id == draft.OwnerId)
            .Select(u => new
            {
                u.PostSignature, u.PostSignatureUrl, u.PostSignatureTranslationsJson, u.AuthorDisplayName, u.ProfileUrl, u.ProfileLocation,
                u.HeaderSlot1Type, u.HeaderSlot2Type, u.HeaderSlot3Type, u.PlanTier, u.PlanExpiresAt,
                u.TelegramLinkText,
                u.TelegramLinkTextTranslationsJson,
                u.AvatarUrl,
            })
            .FirstAsync();
        var ownerPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
        // FI5 — a signature is read at the bottom of whichever language's post it is.
        var localizedSignature = LocalizedTextMap.Pick(owner.PostSignature, owner.PostSignatureTranslationsJson, lang);
        var signatureBlock = SignatureHtml(PlanLimitations.ResolveSignature(ownerPlan, localizedSignature, owner.PostSignatureUrl), "span");

        var headerSlotsLine = RenderHeaderSlotsLine(owner.HeaderSlot1Type, owner.HeaderSlot2Type, owner.HeaderSlot3Type,
            owner.AuthorDisplayName, owner.ProfileUrl, owner.ProfileLocation,
            ownerPlan, draft.BlogPublishedAt, cedarJson, viewCount);

        // Idea #11 - the owner's glossary for the language being shown. Empty for an owner who
        // never defined one, which costs a single indexed read and changes nothing downstream.
        // T-125 — a post in a project also renders with that project's own terms.
        var glossary = await GlossaryEndpoints.LoadForAsync(db, draft.OwnerId, lang, draft.ProjectId, draft.Id);
        // ADR-128 — wikilink targets this page may link to: one query over the referenced ids,
        // filtered by exactly the index visibility rule. Anything not in the map renders as text.
        var wikiIds = WikiLinkRefs.Collect(cedarJson);
        var wikiTargets = wikiIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Drafts
                .Where(d => wikiIds.Contains(d.Id) && d.OwnerId == draft.OwnerId
                    && d.IsBlogPublished && d.BlogSlug != null && (!d.IsPrivate || d.IsListedWhilePrivate))
                .ToDictionaryAsync(d => d.Id, d => d.BlogSlug!);
        var body = CedarToBlogHtmlRenderer.Render(cedarJson, blogBase, lang, glossary, wikiTargets);
        var dateLine = draft.BlogPublishedAt is { } published
            ? $"<span class=\"post-card-date\">{BlogDateFormatter.DateTimeLocal(published, lang)}</span>"
            : "";
        // I15 — author's own wording when set; escaped, unlike the built-in defaults which carry
        // their own arrow entity.
        var viewInTelegramLabel = LocalizedTextMap.Pick(owner.TelegramLinkText, owner.TelegramLinkTextTranslationsJson, lang) is not { } tgLabel
            ? (lang == Languages.English ? Consts.CrossLinks.DefaultTelegramLinkTextEn : Consts.CrossLinks.DefaultTelegramLinkTextRu)
            : System.Net.WebUtility.HtmlEncode(tgLabel);
        var telegramLink = draft is { LastTelegramUsername: not null, LastTelegramMessageId: not null }
            ? $"<a class=\"telegram-link\" href=\"https://t.me/{draft.LastTelegramUsername}/{draft.LastTelegramMessageId}\" target=\"_blank\" rel=\"noopener\">{viewInTelegramLabel}</a>"
            : "";
        var viewsLine = $"<span class=\"post-card-views\">{BlogIcons.Eye}<span class=\"num\">{viewCount}</span></span>";

        // ADR-192/193 — a plain client-side copy, at the top of the post rather than the bottom
        // (feedback: a reader who wants the link rarely wants to scroll for it first).
        var copyLinkLabel = lang == Languages.English ? "Copy link" : "Скопировать ссылку";
        var copiedLabel = lang == Languages.English ? "Copied!" : "Скопировано!";
        var copyLinkBtn = $"""
            <button type="button" class="copy-link-btn" data-copied-label="{System.Net.WebUtility.HtmlEncode(copiedLabel)}">
            {BlogIcons.Link}<span>{System.Net.WebUtility.HtmlEncode(copyLinkLabel)}</span>
            </button>
            """;

        var metaRow = $"<div class=\"post-meta-row\">{dateLine}{viewsLine}{langSwitch}<div class=\"spacer\"></div>{copyLinkBtn}</div>";
        var footerRow = (signatureBlock.Length > 0 || telegramLink.Length > 0)
            ? $"<div class=\"post-footer-row\">{signatureBlock}<div class=\"spacer\"></div>{telegramLink}</div>"
            : "";

        // ADR-125 — the series line and prev/next: numbered over the *visible* ordered members,
        // so an unlisted private part never shifts the numbering a stranger sees. A member that is
        // itself invisible (owner previewing an unlisted part) simply shows no series chrome.
        var seriesLine = "";
        var seriesNav = "";
        // Item 3 — the posts the nav blocks below already offer; "read next" must not repeat them.
        var shownNeighbourSlugs = new HashSet<string>(StringComparer.Ordinal);
        if (draft.SeriesId is { } draftSeriesId
            && await db.Series.FirstOrDefaultAsync(s => s.Id == draftSeriesId && s.OwnerId == site.OwnerId) is { } series)
        {
            var members = await db.Drafts
                .Where(d => d.OwnerId == site.OwnerId && d.SeriesId == draftSeriesId && d.IsBlogPublished && (!d.IsPrivate || d.IsListedWhilePrivate))
                .OrderBy(d => d.SeriesOrder).ThenBy(d => d.BlogPublishedAt)
                .Select(d => new { d.Id, d.BlogSlug, d.Title, d.ArticleTitle })
                .ToListAsync();
            var position = members.FindIndex(m => m.Id == draft.Id);
            if (position >= 0)
            {
                var isEn = lang == Languages.English;
                var partLabel = isEn
                    ? $"Part {position + 1} of {members.Count}"
                    : $"Часть {position + 1} из {members.Count}";
                seriesLine = $"<div class=\"series-line\"><a href=\"/series/{series.Slug}\">"
                    + System.Net.WebUtility.HtmlEncode(series.Name) + $"</a> · {partLabel}</div>";

                var prev = position > 0 ? members[position - 1] : null;
                var next = position < members.Count - 1 ? members[position + 1] : null;
                if (prev?.BlogSlug is { } prevSlug) shownNeighbourSlugs.Add(prevSlug);
                if (next?.BlogSlug is { } nextSlug) shownNeighbourSlugs.Add(nextSlug);
                if (prev is not null || next is not null)
                {
                    var prevHtml = prev is null ? "<span></span>"
                        : $"<a href=\"/{prev.BlogSlug}\"><span class=\"nav-label\">&larr; {(isEn ? "Previous" : "Предыдущая")}</span>"
                          + System.Net.WebUtility.HtmlEncode(prev.ArticleTitle ?? prev.Title) + "</a>";
                    var nextHtml = next is null ? ""
                        : $"<a class=\"nav-next\" href=\"/{next.BlogSlug}\"><span class=\"nav-label\">{(isEn ? "Next" : "Следующая")} &rarr;</span>"
                          + System.Net.WebUtility.HtmlEncode(next.ArticleTitle ?? next.Title) + "</a>";
                    seriesNav = $"<div class=\"series-nav\">{prevHtml}{nextHtml}</div>";
                }
            }
        }

        // ADR-192 — blog-wide prev/next, by publish date, for the post that is not in a series.
        // A post already inside one has --series-nav for exactly this job, scoped to that series;
        // showing both here would be two navigations answering the same question at once.
        var neighboursHtml = "";
        if (draft.SeriesId is null && draft.BlogPublishedAt is { } thisPublished)
        {
            var neighbourRows = await db.Drafts
                .Where(d => d.OwnerId == site.OwnerId && d.IsBlogPublished && d.Id != draft.Id && (!d.IsPrivate || d.IsListedWhilePrivate) && d.BlogPublishedAt != null)
                .Select(d => new { d.BlogSlug, d.Title, d.ArticleTitle, d.BlogPublishedAt })
                .ToListAsync();
            var newer = neighbourRows.Where(n => n.BlogPublishedAt > thisPublished).OrderBy(n => n.BlogPublishedAt).FirstOrDefault();
            var older = neighbourRows.Where(n => n.BlogPublishedAt < thisPublished).OrderByDescending(n => n.BlogPublishedAt).FirstOrDefault();
            if (newer?.BlogSlug is { } newerSlug) shownNeighbourSlugs.Add(newerSlug);
            if (older?.BlogSlug is { } olderSlug) shownNeighbourSlugs.Add(olderSlug);
            if (newer is not null || older is not null)
            {
                var isEn = lang == Languages.English;
                string Card(string blogSlug, string title2, DateTime publishedUtc, string dirLabel) => $"""
                    <a class="neighbour-card" href="/{blogSlug}">
                    <div class="neighbour-dir">{dirLabel}</div>
                    <div class="neighbour-title">{System.Net.WebUtility.HtmlEncode(title2)}</div>
                    <div class="neighbour-date">{BlogDateFormatter.DateLocal(publishedUtc, lang)}</div>
                    </a>
                    """;
                var newerCard = newer is null ? "<span></span>"
                    : Card(newer.BlogSlug!, newer.ArticleTitle ?? newer.Title, newer.BlogPublishedAt!.Value, isEn ? "Newer post" : "Следующая запись");
                var olderCard = older is null ? ""
                    : Card(older.BlogSlug!, older.ArticleTitle ?? older.Title, older.BlogPublishedAt!.Value, isEn ? "Older post" : "Предыдущая запись");
                neighboursHtml = $"<div class=\"post-neighbours\">{newerCard}{olderCard}</div>";
            }
        }

        // Item 3 — up to three "read next" cards sharing at least one tag, newest first. Computed
        // in memory over the owner's published public posts; the set is index-sized, and a tag
        // intersection is not a query SQLite would do better. Private posts never appear here,
        // listed or not — a teaser row is not the index, and it links without the gate's context.
        var relatedHtml = "";
        if (tags.Count > 0)
        {
            var relatedCandidates = await db.Drafts
                .Where(d => d.OwnerId == site.OwnerId && d.IsBlogPublished && !d.IsPrivate
                    && d.Id != draft.Id && d.BlogSlug != null && d.Tags != "")
                .Select(d => new { d.BlogSlug, d.Title, d.ArticleTitle, d.BlogPublishedAt, d.Tags })
                .ToListAsync();
            var related = relatedCandidates
                .Where(c => !shownNeighbourSlugs.Contains(c.BlogSlug!))
                .Where(c => SplitTags(c.Tags).Intersect(tags, StringComparer.OrdinalIgnoreCase).Any())
                .OrderByDescending(c => c.BlogPublishedAt)
                .Take(3)
                .ToList();
            if (related.Count > 0)
            {
                var relatedSb = new StringBuilder();
                relatedSb.Append("<div class=\"related-posts\"><div class=\"related-title\">")
                  .Append(lang == Languages.English ? "Read next" : "Читать дальше")
                  .Append("</div><div class=\"related-grid\">");
                foreach (var r in related)
                {
                    relatedSb.Append("<a class=\"neighbour-card\" href=\"/").Append(r.BlogSlug).Append("\">");
                    relatedSb.Append("<div class=\"neighbour-title\">")
                      .Append(System.Net.WebUtility.HtmlEncode(r.ArticleTitle ?? r.Title)).Append("</div>");
                    if (r.BlogPublishedAt is { } relatedDate)
                        relatedSb.Append("<div class=\"neighbour-date\">")
                          .Append(BlogDateFormatter.DateLocal(relatedDate, lang)).Append("</div>");
                    relatedSb.Append("</a>");
                }
                relatedSb.Append("</div></div>");
                relatedHtml = relatedSb.ToString();
            }
        }

        var titleHeading = HeadingOutline.StartsWithHeading(cedarJson)
            ? ""
            : $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>";

        // Only draw the book-style divider when there's an actual title block above it to
        // separate from the body — the doc's-own-first-heading case (titleHeading == "") with
        // no header slots either has nothing here worth underlining.
        var titleBlock = titleHeading.Length == 0 && headerSlotsLine.Length == 0
            ? ""
            : $"{titleHeading}{headerSlotsLine}<div class=\"post-title-divider\"><span class=\"tdl\"></span><i></i><span class=\"tdl\"></span></div>";

        // I7 — private posts only: the watermark exists to discourage redistribution of something
        // handed out per invite, so it has no job on a public page. Drawn over the content (the
        // overlay is the sheet's last child and sits above it), never behind it.
        var watermark = draft.IsPrivate ? WatermarkRenderer.OverlayHtml(draft.WatermarkText) : "";

        // Copy protection (same private-only family as the watermark above): selection, copy/cut
        // and the context menu are blocked on the post sheet only — the comment/annotation UI
        // below it stays fully usable. A deterrent, not protection: the source is one Ctrl+U away.
        //
        // The image viewer is covered too, and had to be: its overlay is appended to <body>, so a
        // guard bound to `.post-sheet` alone would have left every picture right-clickable the
        // moment it was enlarged — a hole opened by a feature that has nothing to do with copying.
        // Hence a document-level listener filtered by `closest`: the overlay does not exist yet
        // when this runs.
        var copyGuard = draft is { IsPrivate: true, DisableCopy: true }
            ? """
              <style>.post-sheet,.lightbox{-webkit-user-select:none;user-select:none}.post-sheet img,.lightbox img{-webkit-user-drag:none;user-drag:none}</style>
              <script>(function(){if(!document.querySelector('.post-sheet'))return;
              ['contextmenu','copy','cut','dragstart'].forEach(function(ev){document.addEventListener(ev,function(e){
              if(e.target&&e.target.closest&&e.target.closest('.post-sheet,.lightbox'))e.preventDefault();});});})();</script>
              """
            : "";

        var postSheet = $"""
            <div class="post-sheet">
            {metaRow}
            {tagsRow}
            {notTranslatedNotice}
            {seriesLine}
            {titleBlock}
            {body}
            {seriesNav}
            {footerRow}
            {watermark}
            </div>
            """;

        // T-039 — an informational post shows nothing to react with. Both off means the block
        // itself is gone rather than an empty bordered box asking to be filled.
        var articleBlock = draft.DisableReactions && draft.DisableComments
            ? ""
            : "<div class=\"annotation article-annotation\" data-annotation-id=\"\""
              + (draft.DisableReactions ? " data-no-reactions=\"1\"" : "")
              + (draft.DisableComments ? " data-no-comments=\"1\"" : "") + ">"
              + CedarToBlogHtmlRenderer.AnnotationControlsHtml(lang, owner.AuthorDisplayName, draft.BlogPublishedAt) + "</div>";

        var backLinkLabel = lang == Languages.English ? "All posts" : "Все посты";
        var backToTopLabel = lang == Languages.English ? "Back to top" : "Наверх";
        var floatingNav = $"""
            <div class="floating-nav">
            <a class="floating-nav-btn" href="/" title="{backLinkLabel}" aria-label="{backLinkLabel}">{BlogIcons.List}</a>
            <button type="button" class="floating-nav-btn back-to-top-btn" title="{backToTopLabel}" aria-label="{backToTopLabel}">{BlogIcons.ArrowUp}</button>
            </div>
            """;
        // ADR-192 — the reader sits on the wood board, the one material the header and footer
        // already stood on; the sheet itself keeps ADR-179's flat, unrotated paper (a published
        // post is finished, not a draft pinned up to be worked on).
        var html = $"""
            <a class="back-link" href="/">{BlogIcons.ArrowLeft} {backLinkLabel}</a>
            {await RenderPostGameLinkAsync(db, site, draft, lang)}
            <div class="post-reader">
            <span class="post-pin left" aria-hidden="true"></span>
            <span class="post-pin right" aria-hidden="true"></span>
            {postSheet}
            {neighboursHtml}
            {relatedHtml}
            </div>
            {copyGuard}
            {articleBlock}
            {RenderSubscribeBox(ctx, lang != Languages.Russian, "/" + draft.BlogSlug)}
            {floatingNav}
            """;

        // ADR-124 — what the page reveals to link-preview crawlers is a function of the post's own
        // flags, never of the reader's cookie: a crawler holding a valid invite is still a crawler.
        var metaPolicy = draft.IsPrivate
            ? draft.IsListedWhilePrivate ? OgMetaPolicy.TitleImageOnly : OgMetaPolicy.None
            : OgMetaPolicy.Full;
        var metaHtml = "";
        if (metaPolicy != OgMetaPolicy.None)
        {
            string LangUrl(string l) => l == draft.PrimaryLanguage
                ? $"{blogBase}/{draft.BlogSlug}"
                : $"{blogBase}/{draft.BlogSlug}?lang={l}";

            string? image = null;
            int? imageW = null, imageH = null;
            if (metaPolicy == OgMetaPolicy.Full && CedarImageRefs.Collect(cedarJson).FirstOrDefault() is { } cover)
                image = cover.Src.StartsWith('/') ? blogBase + cover.Src : cover.Src;
            // Item 1 — a post with no image of its own gets the generated title card instead of the
            // static og-default (or the owner's avatar, which said nothing about the post). The URL
            // carries ?v={hash} so a title edit busts every cache between here and the reader.
            if (image is null)
            {
                image = OgImageEndpoint.ImageUrl(site, draft.BlogSlug!, title, channel?.Title ?? "Cedar Clerk");
                imageW = 1200;
                imageH = 630;
            }

            var alternates = new List<(string, string)> { (draft.PrimaryLanguage, LangUrl(draft.PrimaryLanguage)) };
            alternates.AddRange(availableLanguages.Where(l => l != draft.PrimaryLanguage).OrderBy(l => l)
                .Select(l => (l, LangUrl(l))));
            var description = metaPolicy == OgMetaPolicy.Full
                ? string.Join(" ", CedarPlainText.Paragraphs(cedarJson))
                : null;

            metaHtml = OgMetaBuilder.Build(new OgMetaInput(
                title, description, LangUrl(lang), image, imageW, imageH,
                channel?.Title ?? "Cedar Clerk", lang,
                alternates, LangUrl(draft.PrimaryLanguage),
                draft.BlogPublishedAt, draft.UpdatedAt, IsArticle: true), metaPolicy);

            // Item 4 — JSON-LD only where the meta already tells everything (Full); a gated page
            // keeps its silence in structured data exactly as it does in OG tags.
            if (metaPolicy == OgMetaPolicy.Full)
                metaHtml += ArticleJsonLd(title, draft.BlogPublishedAt, draft.UpdatedAt, lang,
                    image, channel?.Title ?? "Cedar Clerk", LangUrl(lang));
        }

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(title, html, lang, RenderHeader(channel, lang), metaHtml, mainClass: "site-main--post"));
    }

    // Wraps a resolved end-of-post signature (see PlanLimitations.ResolveSignature, Phase 8 Step 5)
    // as an anchor when it has a Href, plain encoded text otherwise. Shared with DraftEndpoints'
    // static HTML export — same rule, different wrapper tag (span on the live blog page, div there).
    internal static string SignatureHtml(ResolvedSignature? sig, string tag)
    {
        if (sig is null)
            return "";
        var text = System.Net.WebUtility.HtmlEncode(sig.Text);
        var inner = sig.Href is null ? text
            : $"<a href=\"{System.Net.WebUtility.HtmlEncode(sig.Href)}\" target=\"_blank\" rel=\"noopener\">{text}</a>";
        return $"<{tag} class=\"post-signature\">{inner}</{tag}>";
    }

    // Blog-only (see docs/tasks/ROADMAP.md Phase 8 Step 4 / ADR in docs/DECISIONS.md) — a subtitle line
    // under the title, distinct from post-meta-row's date/views/tags. Slot 3 is clamped away for
    // any tier below Pro, even if the column still holds a value from before a downgrade.
    private static string RenderHeaderSlotsLine(
        HeaderSlotType? slot1, HeaderSlotType? slot2, HeaderSlotType? slot3,
        string? authorDisplayName, string? profileUrl, string? profileLocation,
        PlanTiers currentPlan, DateTime? publishedAt, string cedarJson, int viewCount)
    {
        var configured = new[] { slot1, slot2, slot3 }.Take(PlanLimitations.MaxHeaderSlots(currentPlan));

        string text;
        try { text = string.Join(" ", TipTapTextNodes.ExtractTexts(cedarJson)).Trim(); }
        catch (Exception) { text = ""; }
        var wordCount = text.Length == 0 ? 0 : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var ctx = new HeaderSlotContext(authorDisplayName, profileUrl, profileLocation, publishedAt, text.Length, wordCount, viewCount);

        var parts = configured
            .Where(s => s is not null)
            .Select(s => HeaderSlotRenderer.Render(s!.Value, ctx))
            .Where(v => v is not null)
            .Select(v => v!.LinkUrl is { } url
                ? $"<a href=\"{System.Net.WebUtility.HtmlEncode(url)}\" target=\"_blank\" rel=\"noopener\">{System.Net.WebUtility.HtmlEncode(v.Text)}</a>"
                : System.Net.WebUtility.HtmlEncode(v.Text))
            .ToList();

        return parts.Count == 0 ? "" : $"<div class=\"post-header-slots\">{string.Join(" &bull; ", parts)}</div>";
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
        <link rel="alternate" type="application/rss+xml" title="Blog RSS feed" href="/rss.xml">
        {{META}}
        <script>
        (function () {
            /* Both settings, before the first paint: a theme applied after it flashes, and a text
               size applied after it reflows the article under the reader's eyes. */
            var el = document.documentElement;
            var theme = localStorage.getItem('cedar-blog-theme');
            if (theme) el.setAttribute('data-theme', theme);
            var size = localStorage.getItem('cedar-blog-text-size');
            if (size) el.setAttribute('data-read', size);
            var face = localStorage.getItem('cedar-blog-face');
            if (face) el.setAttribute('data-face', face);
        })();
        </script>
        <style>
        :root {
            color-scheme: light dark;
            {{LIGHT_TOKENS}}
        }
        /* T-101 — one palette for the whole product (ADR-071 principle 6), and since ADR-177 the bench
           materials travel with it. Both halves are generated from the app's own stylesheet
           (CedarClerk.Core.DesignTokens), which is what stops the blog from being a shade behind after
           every colour change — it already was: the contrast pass fixed the app and left this file on the
           old values. */
        @media (prefers-color-scheme: dark) {
            :root { {{DARK_TOKENS}} }
        }
        :root[data-theme="light"] { {{LIGHT_TOKENS}} }
        :root[data-theme="dark"] { {{DARK_TOKENS}} }

        /* ADR-181 — the reader's text size, and the whole of what it moves. It has to sit below the theme
           blocks: they restate --fs-read at the same specificity, so this wins on order rather than on
           weight. "m" declares nothing, because 17px is what the token already holds. */
        :root[data-read="s"] { --fs-read: 16px; }
        :root[data-read="l"] { --fs-read: 19px; }

        /* ADR-192 — the face toggle. Absent (the default) is Literata, same as --font-serif already
           declares on .post-sheet; the attribute only ever needs to say the one thing that differs. */
        :root[data-face="sans"] .post-sheet,
        :root[data-face="sans"] .post-card-excerpt { font-family: var(--font-sans); }

        /* ADR-178 — the faces this page names, served from stable URLs the bundler does not hash. */
        {{FONT_FACES}}

        * { box-sizing: border-box; }

        /* T-099 — the footer used to sit wherever the content ended: on a two-post index or a
           private post's gate that is the middle of the screen, with a wide empty band under it.
           A column that is at least the viewport tall, with the main area taking the slack. */
        html { height: 100%; }
        /* The plaster wall is the ground and the sheet is held off it (ADR-179 clause 1, ADR-174's rule
           for a page with no shell to own the viewport). The flat colour under the two gradients is
           --canvas rather than --bg on purpose: it is the darker of the wall's two stops, so the contrast
           census — which reads the last `background`/`background-color` in this rule — scores every ink on
           the page against the worse end of the ramp in both themes. */
        body {
            margin: 0;
            min-height: 100%;
            display: flex;
            flex-direction: column;
            /* A second lamp, low and to the right of the reader: the resin glow is mixed from the
               theme's own token, so the room warms in both palettes without a second value. */
            background-image: var(--lamp),
                radial-gradient(820px 560px at 86% 104%, color-mix(in srgb, var(--resin) 8%, transparent), transparent 62%),
                var(--surface-page);
            background-attachment: fixed;
            background-color: var(--canvas);
            /* The wall carries one ink and it is not --text (ADR-141): at night the wall darkens
               while paper stays cream, so a page-level --text is invisible on the ground and every
               paper surface below re-declares it for itself. */
            color: var(--wood-ink);
            font-family: var(--font-sans);
            font-size: 15px;
            line-height: 1.55;
        }
        .site-main { flex: 1 0 auto; }
        .site-footer { flex: none; }

        /* ADR-140's pair, copied rather than re-derived: no single colour clears 3:1 on paper and on the
           rail alike, and this page has both surfaces on it. Never blue, never a glow. */
        :focus-visible {
            outline: 2px solid var(--brass-edge);
            outline-offset: 2px;
            box-shadow: 0 0 0 5px var(--focus-halo);
        }

        /* T-039 — a post can take likes but no discussion, or the reverse. The block is only
           removed entirely when both are off; otherwise the half that is off simply is not there. */
        .annotation[data-no-reactions] .react-btn { display: none; }
        .annotation[data-no-comments] .comment-count-label,
        .annotation[data-no-comments] .comment-box { display: none; }
        /* With reactions gone the control row has nothing left to show but a comment count. */
        .annotation[data-no-reactions][data-no-comments] .annotation-controls { display: none; }

        a { color: var(--accent); text-decoration: none; }
        img, video { max-width: 100%; height: auto; }
        .spacer { flex: 1; }
        [hidden] { display: none !important; }

        /* Every number the page states is mono, and the same number in two places has to agree. */
        .num { font-family: var(--font-mono); font-variant-numeric: tabular-nums; }
        .gl { flex: none; vertical-align: -2px; }

        /* ── The rail: the one piece of wood in the blog's chrome (ADR-179 clause 2) ─────────────────
           Chrome may be dense — 30px boxes, 11-13px type (ADR-138) — and its lettering is painted cream
           like a national-park sign, never an ink-on-paper colour. */
        .site-header {
            position: sticky;
            top: 0;
            z-index: 10;
            background-color: var(--rail-lo);
            background-image: var(--tex-wood), var(--surface-rail);
            background-size: 420px, auto;
            border-bottom: 2px solid var(--rail-edge);
            box-shadow: var(--shadow-rail);
            color: var(--rail-ink);
        }
        .site-header-inner { max-width: 760px; margin: 0 auto; display: flex; align-items: center; gap: 10px; height: 56px; padding: 0 20px; }
        /* A brass plate with the channel's initial struck into it — hardware, and so 3px rather than a
           circle: the rulebook's roundest object is an 8px plaque. */
        .channel-avatar { width: 30px; height: 30px; border-radius: var(--radius-stamp); background-image: var(--grad-brass); border: 1px solid var(--brass-edge); color: var(--brass-ink); display: flex; align-items: center; justify-content: center; font-family: var(--font-display); font-size: 13px; font-weight: 700; flex: none; }
        /* With no channel the mark is the mark, carved into the board rather than mounted on a plate. */
        .channel-avatar.brand { background-image: none; border: none; color: var(--rail-ink); }
        .channel-photo { background-image: none; object-fit: cover; }
        .channel-id { min-width: 0; }
        .channel-name { font-family: var(--font-display); font-size: 13px; font-weight: 700; letter-spacing: .01em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; text-shadow: 0 1px 1px var(--rail-edge); }
        /* --rail-ink-soft composites to 4.37:1 on the board and is spent on separators; anything read
           takes the .8 cream (ADR-138). */
        .channel-meta { font-size: 11px; color: var(--rail-ink); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .tg-open-btn { display: flex; align-items: center; gap: 6px; min-height: 30px; border: var(--border-rail-btn); background: var(--rail-btn-face); border-radius: var(--radius-plaque); padding: 0 11px; font-size: 12px; font-weight: 600; color: var(--rail-ink); white-space: nowrap; flex: none; }
        .tg-open-btn:hover { background: var(--rail-btn-face-hover); }
        /* The feed's own colour, which is orange everywhere a feed is offered: the mark is what a
           reader recognises, so it is painted rather than set back into the wood. */
        .rss-btn { background: var(--resin); border-color: var(--brass-edge); color: var(--rail-edge); }
        .rss-btn:hover { background: var(--resin-hi); }
        /* ── The reading menu (ADR-181) ──────────────────────────────────────────────────────────
           One control for the two things a reader may change. ADR-175 settled its face: a tinted
           button on the rail needs wood under it to read as anything, and at the chrome box the
           honest answer is paper. */
        .reading-anchor { position: relative; flex: none; }
        .reading-btn { display: flex; align-items: center; justify-content: center; width: 30px; height: 30px; border: 1px solid var(--paper-edge); background: var(--sheet); box-shadow: var(--shadow-paper-sm); border-radius: var(--radius-plaque); color: var(--t2); cursor: pointer; padding: 0; font-family: var(--font-display); font-size: 13px; font-weight: 700; line-height: 1; }
        .reading-btn:hover, .reading-btn[aria-expanded="true"] { background: var(--alt); color: var(--text); }
        /* It hangs off the rail and lands on paper, so the rail's cream ink stops at the board. */
        .reading-menu { position: absolute; top: calc(100% + 8px); right: 0; z-index: 20; width: 248px; padding: 14px 16px 16px; background-color: var(--sheet); background-image: var(--tex-paper); border: 1px solid var(--paper-edge); border-radius: var(--radius-paper); box-shadow: var(--shadow-sheet); color: var(--text); text-align: left; }
        .reading-title { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--t2); margin: 0 0 12px; }
        .reading-group + .reading-group { margin-top: 14px; }
        .reading-label { font-size: 14px; font-weight: 600; color: var(--t2); margin: 0 0 6px; }
        /* A segmented control on paper: one plaque per state, the chosen one in pine. Not a toggle —
           the theme has three states and a toggle can only ever express two, which is why the old
           control could never hand the page back to the system setting. */
        /* Wraps, and the buttons size to their own labels: "Система" is longer than its third of the
           row and "Сістэмная" is longer still, so an equal split clips the very state it names. A
           long label takes its own line rather than being cut. */
        .seg { display: flex; flex-wrap: wrap; gap: 4px; }
        .seg button { flex: 1 1 auto; min-width: 0; min-height: 34px; padding: 0 10px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--paper-bright); color: var(--t2); font-family: var(--font-sans); font-size: 13px; font-weight: 600; white-space: nowrap; cursor: pointer; }
        .seg button:hover { background: var(--alt); color: var(--text); }
        .seg button[aria-pressed="true"] { border-color: var(--pine-deep); background: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); text-shadow: 0 1px 1px rgba(18, 26, 20, .45); }
        .seg-size button { font-family: var(--font-serif); }
        .seg-size button span { display: inline-block; line-height: 1; }
        .seg-face button { font-size: 12.5px; }
        /* On a phone the popover would hang off the right edge of a 390px viewport. */
        @media (max-width: 420px) {
            .reading-menu { right: -8px; width: calc(100vw - 32px); max-width: 260px; }
        }

        .site-main { max-width: 760px; margin: 0 auto; padding: 26px 20px 60px; width: 100%; }
        /* Feedback: the post reader read as mobile-width on a desktop screen. A little wider than
           the rest of the blog, not a second column width — 760px is still the index's measured
           line length (ADR-179), this is only the reader's own frame. */
        .site-main--post { max-width: 820px; }
        .empty { color: var(--wood-ink); }

        /* ── Leaves: tags and filters (ADR-179 clause 5, ADR-176's day/night pair) ───────────────────
           A leaf on paper is the design system's tag. Picked, it is green; unpicked, it lies on the dried
           stock with its own edge, because border alpha alone does not tell two leaves apart. */
        /* ── The index toolbar: one sort/filter button off a paper popover, the reading menu's own
           open/close mechanism ─────────────────────────────────────────────────────────────────── */
        /* Leaves on the reading edge, the order control on the far one, both on one baseline. */
        /* One height for everything on this bar — the two controls and the leaves between them.
           A 44px order button beside a 28px leaf on one line reads as two rows that failed to
           separate; 32px clears the 24px minimum target and is what the row is drawn at. */
        .index-bar { display: flex; align-items: flex-start; gap: 12px; margin: 0 0 20px; }
        .index-bar .tag-chip, .index-bar .lang-chip, .index-bar .index-sort-btn { min-height: 32px; }
        /* Two or three letters, one lit: the index reads in one language at a time, so this is a
           segmented pick and not a set of filters that combine the way the leaves beside it do. */
        .index-lang { display: inline-flex; flex: none; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--sheet); box-shadow: var(--shadow-paper-sm); overflow: hidden; }
        .lang-chip { display: inline-flex; align-items: center; min-height: 32px; padding: 0 11px; font-family: var(--font-readout); font-size: 12px; font-weight: 700; letter-spacing: .06em; color: var(--t2); }
        .lang-chip + .lang-chip { border-left: 1px solid var(--paper-edge); }
        .lang-chip:hover { background: var(--alt); color: var(--text); }
        .lang-chip.current { background-image: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); }
        .index-toolbar { display: flex; justify-content: flex-end; margin: 0; margin-left: auto; flex: none; }
        .sort-anchor { position: relative; flex: none; }
        .index-sort-btn { display: inline-flex; align-items: center; gap: 7px; min-height: 44px; padding: 0 14px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--sheet); box-shadow: var(--shadow-paper-sm); font-family: var(--font-sans); font-size: 13px; font-weight: 600; color: var(--text); white-space: nowrap; cursor: pointer; }
        .index-sort-btn:hover { background: var(--alt); }
        .sort-menu { position: absolute; top: calc(100% + 8px); right: 0; z-index: 20; width: 220px; padding: 14px 16px 16px; background-color: var(--sheet); background-image: var(--tex-paper); border: 1px solid var(--paper-edge); border-radius: var(--radius-paper); box-shadow: var(--shadow-sheet); color: var(--text); text-align: left; }
        .sort-menu .reading-label:not(:first-child) { margin-top: 14px; }
        .sort-menu-group { display: flex; flex-direction: column; gap: 2px; }
        .sort-menu-item { display: block; padding: 7px 9px; border-radius: var(--radius-field); font-family: var(--font-sans); font-size: 13px; font-weight: 600; color: var(--text); }
        .sort-menu-item:hover { background: var(--alt); }
        .sort-menu-item.current { background: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); }
        @media (max-width: 420px) { .sort-menu { right: -8px; width: calc(100vw - 32px); max-width: 260px; } }
        @media (max-width: 560px) { .index-bar { flex-direction: column; } .index-toolbar { margin-left: 0; } }

        /* ── "Show N more" — a plain link, the same paper-button family as the sort control ──────────── */
        .show-more-row { display: flex; justify-content: center; margin-top: 8px; }
        .show-more-btn { display: inline-flex; align-items: center; min-height: 44px; padding: 0 20px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--sheet); background-image: var(--tex-paper); box-shadow: var(--shadow-paper-sm); font-family: var(--font-sans); font-size: 13.5px; font-weight: 700; color: var(--text); }
        .show-more-btn:hover { background: var(--alt); }

        .tag-bar { display: flex; flex-wrap: wrap; gap: 6px; margin: 0; min-width: 0; }
        /* A leaf is read in passing, not aimed at — it keeps its own compact height rather than the
           44px paper touch-floor a standalone control needs. */
        .tag-chip, .post-tag-chip {
            display: inline-flex; align-items: center; min-height: 28px; padding: 4px 13px;
            border: 1px solid var(--leaf-dried-edge); border-radius: 2px 12px 2px 12px;
            background-color: var(--leaf-dried-bg); color: var(--leaf-ink);
            font-size: 13px; font-weight: 700; white-space: nowrap;
        }
        /* Unpicked keeps the green ink on the pale stock, which is the pair the table measures at
           4.5. --leaf-dried-ink is a step below it and means dried — no data, switched off — and a
           filter the reader is meant to read is neither. */
        .tag-chip:hover, .post-tag-chip:hover { border-color: var(--leaf-ink); }
        .tag-chip.selected, .post-tag-chip {
            background-color: var(--leaf-bg); background-image: linear-gradient(135deg, var(--leaf-bg), var(--leaf-bg-2));
            border-color: var(--leaf-ink); color: var(--leaf-ink); box-shadow: var(--shadow-paper-sm);
        }

        /* ── The index: paper pinned to the wall along a pencil rule ─────────────────────────────────── */
        .post-list { display: flex; flex-direction: column; gap: 20px; }
        .post-list.timeline { position: relative; padding-left: 26px; }
        /* The spine is drawn on the wall, which is the one ground the pencil follows into the dark:
           --rule-ink turns cream at night because the wall does. A rule on PAPER takes --border
           instead — cream on cream is not a line. */
        /* Starts at the first dot, not at the top of the list: a spine that begins level with the
           month rule reads as one line turning a corner into the other. */
        .post-list.timeline::before { content: ""; position: absolute; left: 4px; top: 32px; bottom: 6px; width: 1px; background: var(--rule-ink); }
        /* Kept inside the padded column rather than pulled out to the page edge: at -26px its rule
           ran straight through the spine and the two lines crossed in a corner that read as a
           mistake. The spine passes behind the gap between the rule and the plate instead. */
        .timeline-month-sep { display: flex; align-items: center; gap: 10px; margin: 6px 0 -4px; }
        .timeline-month-sep:first-child { margin-top: 0; }
        .timeline-month-sep .sep-line { flex: 1; height: 1px; background: var(--rule-ink-soft); }
        /* A rubber stamp: display face, wide tracking, its own ink for a border. The neutral tone carries
           a word, so it takes --t2 rather than the third tier (ADR-137 rule 5). */
        .timeline-month-sep .sep-label { flex: none; font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--wood-ink); background: none; border: 1.6px solid currentColor; border-radius: var(--radius-stamp); padding: 2px 9px; white-space: nowrap; opacity: .92; }
        .timeline-item { position: relative; }
        /* A brass pin, not a dot: what holds paper to a board in this system is hardware. */
        .timeline-dot { position: absolute; left: -26px; top: 26px; width: 11px; height: 11px; border-radius: 50%; background-image: var(--grad-brass); border: 1px solid var(--brass-edge); box-shadow: inset 0 1px 0 var(--brass-hi), 0 1px 2px rgba(30, 18, 6, .45); z-index: 1; }
        .post-card {
            display: block; position: relative;
            /* A breath of the lamp's resin over the stock — a few percent of the theme's own token,
               so the paper reads warmer at night too without a second recipe. */
            background-color: var(--sheet);
            background-image: linear-gradient(color-mix(in srgb, var(--resin) 4%, transparent), color-mix(in srgb, var(--resin) 4%, transparent)), var(--tex-paper);
            border: var(--border-paper); border-radius: calc(var(--radius-paper) + 3px);
            box-shadow: var(--shadow-paper); padding: 20px 24px 18px; color: var(--text);
            /* The lift is the whole hover language of the system — 2-3px, never a glow or a scale. The
               motion tokens are not served here, so the curve the design system states is written out. */
            transition: transform 150ms cubic-bezier(.3, 1.3, .5, 1);
        }
        /* Under the hand the edge warms toward brass — the paper answering the touch, not a glow. */
        .post-card:hover { transform: translateY(-3px); border-color: color-mix(in srgb, var(--brass) 45%, transparent); }
        .post-card-meta { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; margin: 0 0 6px; font-size: 12px; color: var(--t2); }
        .post-card-date { font-family: var(--font-mono); }
        .post-card-langs { font-family: var(--font-readout); font-size: 11px; font-weight: 700; letter-spacing: .04em; color: var(--brass-ink); background: var(--brass-soft); border: 1px solid var(--brass-lo); border-radius: var(--radius-stamp); padding: 1px 6px; }
        .post-card-tag { color: var(--t2); }
        .post-card-locked { display: inline-flex; color: var(--t2); }
        .post-card-title { font-family: var(--font-display); font-size: 20px; font-weight: 700; line-height: 1.24; margin: 0 0 6px; }
        /* The teaser is reading matter, so the reading controls reach it: it is the one thing on
           the index a reader actually reads, and a size control that moved nothing on the page it
           was opened from read as broken. Two steps under the body measure, so a card stays a card. */
        .post-card-excerpt { font-family: var(--font-serif); font-size: calc(var(--fs-read) - 2px); color: var(--t2); line-height: 1.6; margin: 0 0 10px; max-width: 70ch; }
        .post-card-stats { display: flex; align-items: center; gap: 5px; font-size: 12px; color: var(--t2); }
        .post-card-stats .num { margin-right: 9px; }

        .back-link { display: inline-flex; align-items: center; gap: 6px; font-size: 14px; font-weight: 600; color: var(--wood-ink); padding: 4px 0; margin: 0 0 14px; }
        .back-link:hover { color: var(--accent); }

        /* ── The reader (ADR-192) — the sheet on the wood board the header and footer already stand
           on; the same material, not a lighter plank invented for the occasion. */
        .post-reader {
            position: relative;
            padding: 22px;
            border: 1px solid var(--rail-edge);
            border-radius: 4px;
            background-color: var(--rail-lo);
            background-image: var(--tex-wood), var(--surface-rail);
            background-size: 420px, auto;
            box-shadow: var(--shadow-rail);
            margin: 0 0 20px;
        }
        .post-pin {
            position: absolute; top: -7px; width: 14px; height: 14px; border-radius: 50%; z-index: 3;
            background-image: var(--grad-brass); border: 1px solid var(--brass-edge);
            box-shadow: inset 0 1px 0 var(--brass-hi), 0 1px 3px rgba(20, 12, 4, .5);
        }
        .post-pin.left { left: 20%; }
        .post-pin.right { left: 80%; margin-left: -14px; }
        @media (max-width: 480px) { .post-reader { padding: 12px; } }

        /* ── The sheet (ADR-179 clause 4) — writer.html's paper at the reading numbers ──────────────── */
        .post-sheet {
            position: relative;
            background-color: var(--paper-bright); background-image: var(--tex-paper);
            border: 1px solid var(--paper-edge); border-radius: calc(var(--radius-paper) + 3px);
            box-shadow: var(--shadow-sheet); padding: 34px 44px 30px; color: var(--text);
            font-family: var(--font-serif); font-size: var(--fs-read); line-height: var(--lh-read);
        }
        /* A link inside the running text keeps a quiet warm underline: reading matter says where it
           leads without breaking the line's colour until the reader asks. */
        .post-sheet p a, .post-sheet li a, .post-card-excerpt a {
            text-decoration: underline; text-underline-offset: 3px; text-decoration-thickness: 1px;
            text-decoration-color: color-mix(in srgb, var(--accent) 40%, transparent);
        }
        .post-sheet p a:hover, .post-sheet li a:hover { text-decoration-color: var(--accent); }
        /* I7 — tiled over the post, not behind it. pointer-events:none so it can't take a click,
           and user-select:none so dragging across the page doesn't select the watermark. The
           tile itself (an SVG data URI) comes from WatermarkRenderer as an inline style. */
        .watermark-overlay { position: absolute; inset: 0; z-index: 2; pointer-events: none; user-select: none; border-radius: var(--radius-paper); background-repeat: repeat; }
        .post-sheet h1 { font-family: var(--font-display); font-size: 27px; font-weight: 700; line-height: 1.22; margin: 0 0 12px; text-align: center; }
        .post-sheet h2 { font-family: var(--font-display); font-size: 21px; font-weight: 700; line-height: 1.3; margin: 28px 0 8px; }
        .post-sheet h3 { font-family: var(--font-display); font-size: 18px; font-weight: 600; margin: 22px 0 6px; }
        .post-sheet p { margin: 0 0 16px; }
        .post-header-slots { font-family: var(--font-sans); font-size: 13px; color: var(--t2); margin: 0 0 18px; text-align: center; }
        .post-header-slots a { color: var(--accent); }
        .post-header-slots a:hover { text-decoration: underline; }
        /* The book divider, in brass: the one bit of hardware the reading column carries. */
        .post-title-divider { display: flex; align-items: center; justify-content: center; gap: 12px; margin: 0 0 26px; }
        .post-title-divider .tdl { height: 1px; width: 70px; background: var(--border); }
        .post-title-divider i { width: 6px; height: 6px; flex: none; display: block; background: var(--brass); border: 1px solid var(--brass-edge); transform: rotate(45deg); }
        .post-tags-row { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 16px; }
        .toc { background-color: var(--surface); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); padding: 14px 18px; margin: 0 0 20px; font-family: var(--font-sans); color: var(--text); }
        .toc-title { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--t2); margin: 0 0 8px; }
        .toc ul { list-style: none; margin: 0; padding: 0; font-size: 14px; line-height: 1.9; }
        .toc li a { color: var(--text); }
        .toc li a:hover { color: var(--accent); text-decoration: underline; }
        .toc .toc-lvl-2 { padding-left: 14px; }
        .toc .toc-lvl-3 { padding-left: 28px; }
        .toc .toc-lvl-4 { padding-left: 42px; }
        .toc .toc-lvl-5 { padding-left: 56px; }
        .toc .toc-lvl-6 { padding-left: 70px; }
        .not-translated-notice { font-family: var(--font-sans); background: var(--asoft); border-left: 3px solid var(--abord); border-radius: var(--radius-paper); padding: 10px 14px; margin: 0 0 16px; font-size: 14px; color: var(--t2); }
        .series-line { font-family: var(--font-sans); font-size: 13px; color: var(--t2); margin: 0 0 10px; }
        .series-line a { color: var(--accent); font-weight: 600; }
        .series-line a:hover { text-decoration: underline; }
        .series-nav { display: flex; justify-content: space-between; gap: 12px; margin: 22px 0 0; padding-top: 16px; border-top: 1px solid var(--border); font-family: var(--font-sans); }
        .series-nav a { max-width: 48%; font-size: 14px; color: var(--text); }
        .series-nav a:hover { color: var(--accent); }
        .series-nav .nav-label { display: block; font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--t2); margin-bottom: 2px; }
        .series-nav .nav-next { text-align: right; margin-left: auto; }

        /* ── Blog-wide prev/next (ADR-192) — two small sheets under the reader, cut from the same
           paper as the index cards, for the post that has no series to navigate by instead. */
        .post-neighbours { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 16px; }
        .neighbour-card { display: block; background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: calc(var(--radius-paper) + 3px); box-shadow: var(--shadow-paper-sm); padding: 12px 16px 14px; color: var(--text); transition: transform 150ms cubic-bezier(.3, 1.3, .5, 1); }
        .neighbour-card:hover { transform: translateY(-2px); border-color: color-mix(in srgb, var(--brass) 45%, transparent); }
        .neighbour-dir { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--t2); margin-bottom: 4px; }
        .neighbour-title { font-family: var(--font-serif); font-size: 15px; line-height: 1.35; }
        .neighbour-date { font-family: var(--font-mono); font-size: 11px; color: var(--t2); margin-top: 6px; }
        @media (max-width: 560px) { .post-neighbours { grid-template-columns: 1fr; } }
        .series-head { margin: 0 0 22px; }
        .series-head h1 { font-family: var(--font-display); font-size: 27px; font-weight: 700; line-height: 1.22; margin: 0 0 6px; }
        .series-head .series-desc { color: var(--wood-ink); font-size: 15px; margin: 0 0 4px; }
        .series-head .series-count { color: var(--wood-ink); font-family: var(--font-mono); font-size: 12px; }
        .series-part-no { font-family: var(--font-mono); font-size: 11px; font-weight: 700; letter-spacing: .04em; color: var(--accent); }

        /* ── Showcase (ADR-134) — the same materials, a wider head ───────────────────────────────────── */
        .showcase-head { display: flex; gap: 20px; align-items: flex-start; margin: 0 0 24px; }
        .showcase-cover { width: 180px; border: 1px solid var(--paper-edge); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper); flex: none; }
        .showcase-head-text h1 { font-family: var(--font-display); font-size: 27px; font-weight: 700; line-height: 1.22; margin: 0 0 6px; }
        .showcase-desc { color: var(--wood-ink); font-size: 15px; line-height: 1.55; margin: 0 0 12px; }
        .showcase-links { display: flex; flex-wrap: wrap; gap: 8px; }
        /* A pine plaque: the one colour in the system that acts (ADR-169 — a pine button as a link is a
           pine button). */
        .showcase-link { display: inline-flex; align-items: center; min-height: 44px; padding: 0 18px; border: 1px solid var(--pine-deep); border-radius: var(--radius-plaque); background: var(--grad-pine); box-shadow: var(--shadow-pine-btn); color: var(--text-on-pine); font-size: 14px; font-weight: 700; text-shadow: 0 1px 1px rgba(18, 26, 20, .45); }
        .showcase-link:hover { filter: brightness(1.07); }
        .showcase-section { font-family: var(--font-display); font-size: 12px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--wood-ink); margin: 30px 0 12px; }
        .roadmap-list { display: flex; flex-direction: column; gap: 8px; }
        .roadmap-row { display: flex; align-items: center; gap: 12px; background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper-sm); padding: 12px 18px; color: var(--text); }
        /* Ink and wash are one pair (ADR-145): each tone takes the wash mixed from its own ink. */
        .roadmap-status { flex: none; font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; border: 1.6px solid currentColor; border-radius: var(--radius-stamp); padding: 2px 9px; opacity: .92; }
        .roadmap-status.now { color: var(--accent); background: var(--asoft); }
        .roadmap-status.next, .roadmap-status.later { color: var(--t2); background: none; }
        .roadmap-status.done { color: var(--ok); background: var(--ok-soft); }
        .roadmap-title { font-size: 15px; }
        .showcase-trailer { position: relative; padding-bottom: 56.25%; height: 0; margin: 0 0 18px; border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper); overflow: hidden; }
        .showcase-trailer iframe { position: absolute; inset: 0; width: 100%; height: 100%; border: 0; }
        .showcase-gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 10px; margin: 0 0 6px; }
        .showcase-shot { display: block; border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper-sm); overflow: hidden; line-height: 0; }
        .showcase-shot img { width: 100%; height: 100%; aspect-ratio: 16 / 9; object-fit: cover; }
        .download-list { display: flex; flex-direction: column; gap: 8px; }
        .download-row { display: flex; align-items: baseline; flex-wrap: wrap; gap: 12px; background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper-sm); padding: 12px 18px; color: var(--text); }
        .download-version { font-family: var(--font-mono); font-weight: 700; }
        .download-date { font-family: var(--font-mono); font-size: 12px; color: var(--t2); }
        .download-notes { font-size: 14px; color: var(--t2); }
        .follow-form { display: flex; flex-wrap: wrap; gap: 8px; margin: 0 0 8px; }
        .follow-input { flex: 1 1 220px; min-height: 44px; padding: 0 14px; background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); color: var(--text); font-family: var(--font-sans); font-size: 15px; }
        .follow-button { min-height: 44px; padding: 0 20px; border: 1px solid var(--pine-deep); border-radius: var(--radius-plaque); background: var(--grad-pine); box-shadow: var(--shadow-pine-btn); color: var(--text-on-pine); font-size: 14px; font-weight: 700; cursor: pointer; }
        .follow-button:hover { filter: brightness(1.07); }
        .follow-hint { font-size: 13px; color: var(--t2); margin: 0; }
        .follow-notice { font-size: 14px; color: var(--accent); margin: 8px 0 0; }
        .games-strip { display: flex; flex-wrap: wrap; gap: 10px; margin: 0 0 22px; }
        .games-card { display: flex; align-items: center; gap: 10px; background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper-sm); padding: 8px 14px 8px 8px; color: var(--text); }
        .games-card:hover { box-shadow: var(--shadow-paper); }
        .games-cover { width: 44px; height: 44px; border-radius: var(--radius-stamp); object-fit: cover; flex: none; }
        .games-name { font-family: var(--font-display); font-size: 15px; font-weight: 700; }
        .post-game { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--t2); margin: 0 0 10px; }
        .post-game a { color: var(--accent); }
        @media (max-width: 560px) { .showcase-head { flex-direction: column; } .showcase-cover { width: 100%; } }

        /* ── Search: a compact paper field on the index bar, grown to full size on its own page ──────── */
        .search-form { display: flex; gap: 6px; flex: none; }
        .search-input { min-height: 32px; width: 150px; padding: 0 12px; background-color: var(--sheet); border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); box-shadow: var(--shadow-paper-sm); color: var(--text); font-family: var(--font-sans); font-size: 13px; }
        .search-input::placeholder { color: var(--t3); opacity: 1; }
        .search-btn { display: inline-flex; align-items: center; gap: 6px; min-height: 32px; padding: 0 12px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--sheet); box-shadow: var(--shadow-paper-sm); font-family: var(--font-sans); font-size: 13px; font-weight: 600; color: var(--text); cursor: pointer; }
        .search-btn:hover { background: var(--alt); }
        .index-toolbar .search-form { margin-right: 8px; }
        .search-head { margin: 0 0 22px; }
        .search-head h1 { font-family: var(--font-display); font-size: 27px; font-weight: 700; color: var(--wood-ink); margin: 0 0 12px; }
        .search-head .search-form { flex-wrap: wrap; }
        .search-head .search-input { flex: 1 1 220px; min-height: 44px; font-size: 15px; }
        .search-head .search-btn { min-height: 44px; padding: 0 16px; }
        @media (max-width: 560px) { .index-toolbar .search-form { display: none; } }

        /* ── "Read next": tag-mates under the reader, cut from the neighbour cards' paper ────────────── */
        .related-posts { margin-top: 16px; }
        .related-title { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--rail-ink); text-shadow: 0 1px 1px var(--rail-edge); margin: 0 0 8px; }
        .related-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; }

        /* ── The subscribe box: the follow form's paper, offered blog-wide ───────────────────────────── */
        .subscribe-box { background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper-sm); padding: 16px 20px; margin: 22px 0 0; color: var(--text); }
        .subscribe-title { font-family: var(--font-display); font-size: 12px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--t2); margin: 0 0 10px; }
        .subscribe-box .follow-form { margin: 0 0 8px; }

        /* ── The press kit page — the showcase's materials, a wider column, a wood top bar ───────────── */
        .site-main--press { max-width: 1000px; }
        .press-bar { display: flex; align-items: center; gap: 10px; padding: 10px 16px; margin: 0 0 22px; border: 1px solid var(--rail-edge); border-radius: 4px; background-color: var(--rail-lo); background-image: var(--tex-wood), var(--surface-rail); background-size: 420px, auto; box-shadow: var(--shadow-rail); color: var(--rail-ink); }
        .press-bar-name { font-family: var(--font-display); font-size: 14px; font-weight: 700; text-shadow: 0 1px 1px var(--rail-edge); }
        .press-bar-label { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; opacity: .85; white-space: nowrap; }
        .press-pack-chip { display: inline-flex; align-items: center; min-height: 32px; padding: 0 14px; background: var(--resin); border: 1px solid var(--brass-edge); border-radius: var(--radius-plaque); color: var(--rail-edge); font-size: 13px; font-weight: 700; white-space: nowrap; }
        .press-pack-chip:hover { background: var(--resin-hi); }
        .press-h1 { font-family: var(--font-display); font-size: 40px; font-weight: 700; line-height: 1.15; color: var(--wood-ink); margin: 0 0 8px; }
        .press-tagline { font-size: 15px; color: var(--wood-ink); margin: 0 0 12px; }
        .press-chips { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 20px; }
        .press-chip { display: inline-flex; align-items: center; min-height: 28px; padding: 0 12px; background-color: var(--sheet); border: 1px solid var(--paper-edge); border-radius: 999px; font-size: 13px; font-weight: 600; color: var(--text); }
        .press-grid { display: grid; grid-template-columns: 280px 1fr; gap: 24px; align-items: start; }
        .press-card { background-color: var(--paper-bright); background-image: var(--tex-paper); border: 1px solid var(--paper-edge); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper-sm); padding: 16px 18px; color: var(--text); }
        .press-card-title { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--t2); margin: 0 0 10px; }
        .press-row { margin: 0 0 10px; }
        .press-row-label { font-family: var(--font-display); font-size: 11px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; color: var(--t2); }
        .press-row-value { font-size: 14px; line-height: 1.5; overflow-wrap: anywhere; }
        .press-card-divider { border-top: 1px dashed var(--border-strong); margin: 12px 0; }
        .press-main { min-width: 0; }
        .press-main .showcase-section:first-child { margin-top: 0; }
        .press-about { font-family: var(--font-serif); font-size: 15px; line-height: 1.65; color: var(--wood-ink); }
        .press-about p { margin: 0 0 12px; }
        .press-shots { grid-template-columns: repeat(3, 1fr); }
        .press-download-all { font-family: var(--font-sans); font-size: 12px; font-weight: 600; letter-spacing: 0; text-transform: none; color: var(--accent); margin-left: 8px; }
        .press-devlog-line { font-size: 14px; color: var(--wood-ink); margin: 0; }
        @media (max-width: 700px) { .press-grid { grid-template-columns: 1fr; } .press-shots { grid-template-columns: repeat(2, 1fr); } .press-h1 { font-size: 30px; } }

        /* ── The draft preview's banner — the not-translated notice's paper, saying what this is ─────── */
        .preview-banner { font-family: var(--font-sans); background: var(--asoft); border-left: 3px solid var(--abord); border-radius: var(--radius-paper); padding: 10px 14px; margin: 0 0 16px; font-size: 14px; color: var(--t2); }

        /* ── Floating nav — paper plaques, never discs ───────────────────────────────────────────────── */
        .floating-nav { position: fixed; right: 20px; bottom: 46px; display: flex; flex-direction: column; gap: 8px; z-index: 50; opacity: 0; pointer-events: none; transition: opacity 150ms ease; }
        .floating-nav.visible { opacity: 1; pointer-events: auto; }
        .floating-nav-btn { width: 40px; height: 40px; border-radius: var(--radius-plaque); background: var(--sheet); border: 1px solid var(--paper-edge); box-shadow: var(--shadow-paper); display: flex; align-items: center; justify-content: center; color: var(--t2); cursor: pointer; padding: 0; }
        .floating-nav-btn:hover { background: var(--alt); color: var(--text); }

        .post-meta-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin: 0 0 12px; font-family: var(--font-sans); font-size: 12px; color: var(--t2); }
        .post-card-views { display: inline-flex; align-items: center; gap: 5px; }
        .lang-switch-track { display: flex; gap: 4px; margin-left: auto; }
        .lang-switch-btn { display: inline-flex; align-items: center; min-height: 30px; padding: 0 11px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--sheet); font-family: var(--font-mono); font-size: 12px; font-weight: 600; color: var(--t2); }
        .lang-switch-btn:hover { background: var(--alt); color: var(--text); }
        .lang-switch-btn.current { border-color: var(--pine-deep); background: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); }
        .post-footer-row { display: flex; align-items: center; gap: 10px; border-top: 1px solid var(--border); padding: 16px 0 0; margin-top: 18px; font-family: var(--font-sans); }
        /* The one place the margin note's hand appears: an author's sign-off is written, not typeset. */
        .post-signature { font-family: var(--font-serif); font-size: 14px; color: var(--t2); white-space: pre-line; }
        .telegram-link { font-size: 13px; font-weight: 600; color: var(--accent); }
        /* ADR-192 — a plain paper button, the same family the index's "show more" and sort control
           use, so a copy action does not read as a bigger deal than a link. */
        .copy-link-btn { display: inline-flex; align-items: center; gap: 6px; min-height: 30px; padding: 0 12px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--sheet); box-shadow: var(--shadow-paper-sm); font-family: var(--font-sans); font-size: 12.5px; font-weight: 600; color: var(--text); cursor: pointer; }
        .copy-link-btn:hover { background: var(--alt); }
        .copy-link-btn.copied { border-color: var(--pine-deep); background: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); }

        /* ── Body vocabulary the renderer emits ─────────────────────────────────────────────────────── */
        .spoiler { background: var(--t3); color: transparent; border-radius: var(--radius-stamp); padding: 0 5px; cursor: pointer; transition: background 200ms; }
        .spoiler:hover, .spoiler:focus { background: var(--alt); color: inherit; }
        .post-sheet code { font-family: var(--font-mono); font-size: .85em; background: var(--surface); border: 1px solid var(--paper-edge); border-radius: var(--radius-stamp); padding: 0 5px; }
        /* Text never sits on wood grain (rulebook 2), so a code block is the deeper paper rather than the
           dark plate it used to be — the design system's own log lines are on paper for the same reason. */
        .post-sheet pre { background-color: var(--surface); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-field); box-shadow: var(--shadow-field-inset); padding: 14px 16px; overflow-x: auto; }
        .post-sheet pre code { background: none; border: none; padding: 0; font-size: 13px; line-height: 1.6; }
        .post-sheet blockquote { border-left: 3px solid var(--brass); padding: 2px 0 2px 16px; color: var(--t2); margin: 0 0 18px; }
        .post-sheet hr { border: none; border-top: 1px solid var(--border); margin: 26px 0; }
        .post-sheet ul, .post-sheet ol { padding-left: 22px; margin: 0 0 18px; }
        .post-sheet figure { margin: 0 0 18px; }
        .post-sheet figcaption { text-align: center; font-family: var(--font-sans); font-size: 13px; color: var(--t2); margin-top: 6px; }
        .audio-title { font-family: var(--font-sans); font-size: 14px; font-weight: 600; color: var(--text); margin: 10px 0 4px; }
        .post-sheet table { width: 100%; border-collapse: collapse; font-family: var(--font-sans); font-size: 14px; margin: 0 0 18px; overflow-x: auto; display: block; }
        .post-sheet th, .post-sheet td { border: 1px solid var(--paper-edge); padding: 7px 11px; text-align: left; vertical-align: top; }
        .post-sheet th { background: var(--surface); font-weight: 700; }
        .post-sheet tr:nth-child(even) td { background: var(--alt); }
        .math-tex { margin: 16px 0; overflow-x: auto; }
        div.math-tex { text-align: center; }
        .collage { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 6px; }
        .collage img { width: 100%; height: 160px; object-fit: cover; border-radius: var(--radius-paper); }
        .carousel { position: relative; margin: 18px 0; }
        .carousel-viewport img { width: 100%; display: block; border-radius: var(--radius-paper); }
        .carousel-prev, .carousel-next { position: absolute; top: 50%; transform: translateY(-50%); background: var(--sheet); color: var(--text); border: 1px solid var(--paper-edge); box-shadow: var(--shadow-paper); width: 34px; height: 34px; border-radius: var(--radius-plaque); cursor: pointer; font-size: 18px; line-height: 1; padding: 0; }
        .carousel-prev { left: 8px; }
        .carousel-next { right: 8px; }
        .carousel-dots { display: flex; justify-content: center; gap: 6px; margin-top: 8px; }
        .carousel-dot { width: 8px; height: 8px; border-radius: 50%; border: 1px solid var(--paper-edge); background: var(--surface); cursor: pointer; padding: 0; }
        .carousel-dot.active { background-image: var(--grad-brass); border-color: var(--brass-edge); }

        /* Image viewer. The zoom cursor is put on by the script, not by a CSS selector, so that
           "this image opens" and "this image is clickable" can never disagree — and so a reader
           with JavaScript off is not invited to click something that will not happen. */
        .post-sheet img.zoomable { cursor: zoom-in; }
        .lightbox { position: fixed; inset: 0; z-index: 100; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px; padding: 3vh 3vw; background: rgba(14, 9, 3, .94); opacity: 0; transition: opacity 120ms ease; }
        .lightbox.open { opacity: 1; }
        /* 100% of a flex item that is already inside the padded box: the image fills what is left
           after the caption row, which is why the caption never pushes it off-screen. */
        .lightbox img { max-width: 100%; max-height: 100%; object-fit: contain; border-radius: var(--radius-paper); cursor: zoom-out; }
        .lightbox-cap { flex: none; max-width: 900px; text-align: center; font-family: var(--font-sans); font-size: 14px; line-height: 1.5; color: rgba(242, 232, 206, .78); }
        .lightbox-count { font-family: var(--font-mono); font-variant-numeric: tabular-nums; color: rgba(242, 232, 206, .5); }
        .lightbox-btn { position: absolute; display: flex; align-items: center; justify-content: center; border: 1px solid var(--brass-edge); border-radius: var(--radius-plaque); background-image: var(--grad-brass); color: var(--brass-ink); cursor: pointer; font-family: inherit; line-height: 1; padding: 0; }
        .lightbox-btn:hover { filter: brightness(1.08); }
        .lightbox-close { top: 16px; right: 16px; width: 40px; height: 40px; font-size: 24px; }
        .lightbox-prev, .lightbox-next { top: 50%; transform: translateY(-50%); width: 46px; height: 46px; font-size: 28px; }
        .lightbox-prev { left: 16px; }
        .lightbox-next { right: 16px; }
        /* On a phone the arrows would sit on top of the picture itself; there the swipe-sized
           targets move to the bottom corners, where a thumb already is. */
        @media (max-width: 600px) {
            .lightbox-prev, .lightbox-next { top: auto; bottom: 16px; transform: none; }
        }
        .youtube-embed { position: relative; width: 100%; aspect-ratio: 16 / 9; margin: 0 0 18px; border-radius: var(--radius-paper); overflow: hidden; }
        .youtube-embed iframe { position: absolute; inset: 0; width: 100%; height: 100%; border: none; }
        .footnotes { font-family: var(--font-sans); font-size: 13px; color: var(--t2); border-top: 1px solid var(--border); padding: 12px 0 0; margin: 0 0 4px; }
        .footnotes sup, .post-sheet sup { color: var(--accent); font-weight: 700; }

        .poll-block { background-color: var(--surface); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); padding: 16px 18px; margin: 18px 0; font-family: var(--font-sans); color: var(--text); }
        .poll-question { font-family: var(--font-display); font-weight: 700; font-size: 16px; margin-bottom: 12px; }
        .poll-options { display: flex; flex-direction: column; gap: 6px; }
        .poll-option { position: relative; display: flex; align-items: center; gap: 8px; min-height: 44px; border: 1px solid var(--paper-edge); background: var(--paper-bright); border-radius: var(--radius-field); padding: 8px 12px; cursor: pointer; font-family: inherit; font-size: 14px; color: var(--text); text-align: left; overflow: hidden; }
        .poll-option:hover { border-color: var(--border-strong); }
        .poll-option.voted { border-color: var(--abord); }
        .poll-option-label { position: relative; z-index: 1; flex: 1; }
        .poll-option-bar { position: absolute; inset: 0; z-index: 0; }
        .poll-option-fill { display: block; height: 100%; width: 0%; background: var(--asoft); transition: width 300ms ease; }
        .poll-option-pct { position: relative; z-index: 1; font-family: var(--font-mono); font-size: 12px; color: var(--t2); font-variant-numeric: tabular-nums; }
        .poll-total { font-family: var(--font-mono); font-size: 12px; color: var(--t2); margin-top: 10px; }

        /* ── Feedback: it stays under the post it belongs to (ADR-046 clause 4) ─────────────────────── */
        .annotation { border-left: 3px solid var(--abord); background: var(--asoft); padding: 10px 14px; margin: 18px 0; border-radius: var(--radius-paper); font-family: var(--font-sans); color: var(--text); }
        .article-annotation { border-left: none; background: none; padding: 0; margin: 20px 0 0; }
        .annotation-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 8px; }
        .article-annotation > .annotation-controls { margin-bottom: 14px; }
        .react-btn { display: flex; align-items: center; gap: 7px; min-height: 44px; border: 1px solid var(--paper-edge); background: var(--sheet); box-shadow: var(--shadow-paper-sm); border-radius: var(--radius-plaque); padding: 0 16px; font-size: 14px; font-weight: 600; cursor: pointer; color: var(--t2); font-family: inherit; }
        .react-btn:hover { background: var(--surface); color: var(--text); }
        .react-btn.active { border-color: var(--abord); background: var(--asoft); color: var(--accent); }
        .react-btn .count { font-family: var(--font-mono); font-weight: 700; font-variant-numeric: tabular-nums; }
        .comment-count-label { display: inline-flex; align-items: center; gap: 6px; font-size: 14px; color: var(--wood-ink); }
        .comment-count-label .comment-count { font-family: var(--font-mono); font-variant-numeric: tabular-nums; }
        .comment-box { background-color: var(--sheet); background-image: var(--tex-paper); border: var(--border-paper); border-radius: var(--radius-paper); box-shadow: var(--shadow-paper); padding: 20px 24px; font-family: var(--font-sans); color: var(--text); }
        .comment-box-label { font-family: var(--font-display); font-size: 11px; letter-spacing: .13em; text-transform: uppercase; font-weight: 700; color: var(--t2); margin: 0 0 12px; }
        .comment-published-line { font-family: var(--font-mono); font-size: 12px; color: var(--t2); margin: -6px 0 12px; }
        .comment-list { display: flex; flex-direction: column; gap: 4px; margin: 0 0 14px; }
        .comment-item { display: flex; gap: 10px; padding: 8px 10px; border-radius: var(--radius-paper); transition: background 250ms; }
        .comment-item.glow { background: var(--asoft); }
        .comment-item.owner { background: var(--asoft); border: 1px solid var(--abord); }
        .comment-item.owner .comment-meta::after { content: '★'; color: var(--brass-ink); font-size: 11px; }
        .comment-item.comment-reply { margin-left: 30px; padding-top: 6px; padding-bottom: 6px; }
        .comment-item.comment-reply .comment-avatar { width: 22px; height: 22px; font-size: 10px; }
        .comment-item.comment-reply .comment-meta { font-size: 13px; }
        .comment-item.comment-reply .comment-text { font-size: 14px; }
        .comment-avatar { width: 28px; height: 28px; border-radius: var(--radius-stamp); color: #fff; display: flex; align-items: center; justify-content: center; font-family: var(--font-display); font-size: 12px; font-weight: 700; flex: none; }
        .comment-meta { display: flex; align-items: baseline; gap: 7px; font-size: 14px; font-weight: 700; }
        .comment-meta time { font-family: var(--font-readout); font-size: 11px; font-weight: 400; color: var(--t2); }
        .comment-anchor { font-family: var(--font-readout); font-size: 11px; color: var(--accent); background: var(--asoft); border: 1px solid var(--abord); border-radius: var(--radius-stamp); padding: 1px 7px; display: inline-block; margin: 3px 0 1px; }
        .comment-text { font-size: 15px; line-height: 1.55; }
        .reply-btn { align-self: flex-start; margin-top: 4px; background: none; border: none; color: var(--t2); font-size: 13px; font-family: inherit; cursor: pointer; padding: 0; }
        .reply-btn:hover { color: var(--accent); text-decoration: underline; }
        /* IB5: a `display` rule beats the [hidden] attribute's default `display: none`, so both
           the reply indicator and the load-more button below stayed on screen no matter what the
           script set — the reply target looked impossible to clear, and "show more" was offered
           when there was no more. One global rule rather than a per-class fix, so the next
           element scripted through `hidden` doesn't reintroduce it. */
        .comment-reply-indicator { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--t2); margin: 0 0 8px; }
        .comment-reply-indicator .reply-target-name { font-weight: 700; color: var(--text); }
        .comment-reply-indicator .cancel-reply { background: var(--sheet); border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); padding: 2px 10px; font-size: 12px; color: var(--t2); cursor: pointer; font-family: inherit; }
        .comment-load-more { display: block; margin: 0 0 10px; background: var(--sheet); border: 1px solid var(--paper-edge); box-shadow: var(--shadow-paper-sm); border-radius: var(--radius-plaque); padding: 6px 12px; cursor: pointer; color: var(--t2); font: inherit; font-size: 13px; }
        .comment-load-more:hover { background: var(--surface); color: var(--text); }

        /* Idea #11 - a glossary term in the body, and the card that explains it. The term is
           focusable so the tooltip is reachable by keyboard and by tap, not only by hover. */
        .glossary-term { border-bottom: 1px dashed var(--abord); cursor: help; }
        .glossary-term:hover, .glossary-term:focus { background: var(--asoft); outline: none; }
        .glossary-pop {
            position: absolute; z-index: 50; max-width: 300px; padding: 12px 14px;
            background-color: var(--sheet); background-image: var(--tex-paper);
            border: var(--border-paper); border-radius: var(--radius-paper);
            box-shadow: var(--shadow-paper); font-family: var(--font-sans); font-size: 14px; line-height: 1.5; color: var(--text);
        }
        .glossary-pop-term { font-family: var(--font-display); font-weight: 700; margin: 0 0 4px; }
        .glossary-pop-desc { margin: 0; color: var(--t2); }
        .glossary-pop img { display: block; width: 100%; border-radius: var(--radius-paper); margin: 0 0 8px; }

        /* ── Fields and the pine button: the gate (B3) and the comment form ─────────────────────────── */
        .reg-gate { display: flex; justify-content: center; padding: 8px 0 40px; }
        .reg-card { background-color: var(--sheet); background-image: var(--tex-paper); border: 1px solid var(--paper-edge); border-radius: var(--radius-paper); box-shadow: var(--shadow-sheet); padding: 28px 30px; max-width: 460px; width: 100%; color: var(--text); }
        .reg-title { font-family: var(--font-display); font-size: 24px; font-weight: 700; line-height: 1.22; margin: 0 0 10px; }
        .reg-lock { display: flex; align-items: center; gap: 6px; font-family: var(--font-display); font-size: 11px; letter-spacing: .13em; text-transform: uppercase; font-weight: 700; color: var(--t2); margin-bottom: 12px; }
        .reg-blurb { font-size: 14px; color: var(--t2); margin: 0 0 6px; }
        .reg-intro { font-family: var(--font-serif); font-size: 15px; line-height: 1.6; margin: 0 0 16px; }
        .reg-langs { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 14px; }
        .reg-lang { display: inline-flex; align-items: center; min-height: 30px; padding: 0 11px; border: 1px solid var(--paper-edge); border-radius: var(--radius-plaque); background: var(--paper-bright); font-family: var(--font-mono); font-size: 12px; font-weight: 600; color: var(--t2); }
        .reg-lang:hover { background: var(--alt); color: var(--text); }
        .reg-lang.active { border-color: var(--pine-deep); background: var(--grad-pine); color: var(--text-on-pine); box-shadow: var(--shadow-pine-btn); }
        .reg-form { display: flex; flex-direction: column; gap: 12px; margin-top: 14px; }
        .reg-question { display: flex; flex-direction: column; gap: 5px; }
        .reg-question-label { font-size: 14px; font-weight: 600; }
        .reg-multi { display: flex; flex-direction: column; gap: 6px; }
        .reg-multi-option { display: flex; align-items: center; gap: 8px; min-height: 44px; font-size: 15px; cursor: pointer; }
        .reg-textarea { resize: vertical; min-height: 88px; line-height: 1.5; }
        .reg-static { display: flex; flex-direction: column; gap: 8px; }
        .reg-static-image { width: 100%; height: auto; border-radius: var(--radius-paper); display: block; }
        .reg-static-text { font-size: 15px; line-height: 1.6; margin: 0; white-space: pre-wrap; }
        .reg-consent { display: flex; flex-direction: column; gap: 6px; }
        .reg-consent-text { font-size: 14px; line-height: 1.5; margin: 0; white-space: pre-wrap; }
        .reg-consent-check { display: flex; align-items: center; gap: 8px; min-height: 44px; font-size: 15px; font-weight: 600; cursor: pointer; }
        .reg-error { color: var(--danger); font-size: 14px; margin: 4px 0 0; }

        /* One field rule for both forms: paper stock, a boundary the reader is meant to find
           (--border-strong is the affordance token), a milled inset, and a brass ring on focus. */
        .reg-input, .comment-form input[type="text"], .comment-form textarea {
            width: 100%; border: 1px solid var(--border-strong); background: var(--paper-bright); color: var(--text);
            border-radius: var(--radius-field); box-shadow: var(--shadow-field-inset);
            padding: 10px 12px; min-height: 44px; font-size: 15px; font-family: inherit; outline: none;
        }
        .reg-input::placeholder, .comment-form textarea::placeholder, .comment-form input::placeholder { color: var(--t3); opacity: 1; }

        /* IB5: was three stacked full-width rows (name, textarea, a full-width Send slab). The
           comment box is a secondary element on the page, so it now leads with the textarea and
           puts the optional name next to a normal-sized Send button on one row. */
        .comment-form { display: flex; flex-direction: column; gap: 8px; }
        .comment-form textarea { min-height: 62px; resize: vertical; }
        .comment-form-row { display: flex; gap: 8px; }
        .comment-form-row .comment-author { flex: 1; min-width: 0; }
        /* The pine plaque — the primary action of the one form a reader ever fills in. */
        .reg-submit, .comment-form button {
            flex: none; border: 1px solid var(--pine-deep); background: var(--grad-pine);
            color: var(--text-on-pine); border-radius: var(--radius-plaque); box-shadow: var(--shadow-pine-btn);
            min-height: 44px; padding: 0 20px; font-size: 15px; font-weight: 700; cursor: pointer;
            font-family: var(--font-sans); text-shadow: 0 1px 1px rgba(18, 26, 20, .45);
        }
        .reg-submit { margin-top: 4px; }
        .reg-submit:hover, .comment-form button:hover { filter: brightness(1.07); }
        .reg-submit:active, .comment-form button:active { transform: translateY(2px); }
        .reg-submit:disabled { opacity: .6; cursor: default; transform: none; }

        /* ── The bench's bottom edge (ADR-179 clause 2, repainted by ADR-184): wood, not brass ──────── */
        .site-footer {
            border-top: 1px solid var(--rail-edge);
            box-shadow: inset 0 1px 0 rgba(255, 240, 210, .1);
            background-color: var(--rail-lo);
            background-image: var(--tex-wood), var(--surface-rail);
            background-size: 420px, auto;
            color: var(--rail-ink);
            text-shadow: 0 1px 1px var(--rail-edge);
        }
        /* Three groups on one line — brand, links, badge — and the badge is the one that must not be
           centred: a hosted SVG of fixed size in the middle of a footer reads as an advert placed there,
           while the same badge at the edge reads as a credit. */
        .site-footer-inner { max-width: 760px; margin: 0 auto; display: flex; align-items: center;
            justify-content: space-between; gap: 8px 20px; flex-wrap: wrap; min-height: 30px; padding: 6px 20px;
            font-family: var(--font-readout); font-size: 11px; }
        .footer-brand { display: flex; align-items: center; gap: 8px; }
        .footer-brand a { color: var(--rail-ink); font-weight: 700; }
        .footer-links { display: flex; align-items: center; gap: 14px; }
        .footer-links a { color: var(--rail-ink); }
        .footer-links a:hover, .footer-brand a:hover { text-decoration: underline; }
        /* The badge ships as a white plate, which on the wood is the brightest thing on the bar —
           brighter than the credit beside it. Dimming it to the weight of the text around it keeps it a credit
           rather than a banner; it comes back to full on hover. */
        .footer-badge { flex: none; display: block; opacity: 0.72; transition: opacity 120ms; }
        .footer-badge:hover { opacity: 1; }
        .footer-badge img { max-width: 100%; height: auto; display: block; }

        /* Below this the three groups stack, and the row that was pushed to the edges would look
           ragged left — so a stacked rule centres instead. */
        @media (max-width: 700px) {
            .site-footer-inner { flex-direction: column; justify-content: center; text-align: center; gap: 10px; padding: 12px 20px; }
            .footer-brand { justify-content: center; }
        }

        @media (max-width: 480px) {
            .post-sheet { padding: 24px 18px 22px; }
            .post-sheet h1 { font-size: 23px; }
            .comment-box { padding: 16px; }
            .tg-open-btn span.tg-open-label { display: none; }
        }

        /* The design system's motion is a settle, and the OS setting turns it off entirely. */
        @media (prefers-reduced-motion: reduce) {
            *, *::before, *::after { animation-duration: 1ms !important; transition-duration: 1ms !important; scroll-behavior: auto !important; }
        }
        </style>
        {{MATH_ASSETS}}
        </head>
        <body>
        {{HEADER}}
        <main class="site-main{{MAIN_CLASS}}">
        {{BODY}}
        </main>
        <div class="site-footer"><div class="site-footer-inner">
        <div class="footer-brand">
        <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><polygon points="12,2 19,11 5,11" fill="var(--pine-mark)"></polygon><polygon points="12,7 21,18 3,18" fill="var(--pine-mark)" opacity="0.75"></polygon><rect x="10.6" y="18" width="2.8" height="4" rx="1" fill="var(--pine-mark)" opacity="0.9"></rect></svg>
        <span>Made with <a href="https://cedarclerk.app">Cedar Clerk</a> — write here, publish there. Moo.</span>
        </div>
        <!--Terms and Privacy live on the app host, not here: one copy of a legal page, and the blog
        is a second host serving the same product. Status is the uptime page (T-148) — a link a reader
        can reach precisely when the blog itself cannot answer, which is why it is not self-hosted.-->
        <nav class="footer-links">
        <a href="https://cedarclerk.app/terms">Terms</a>
        <a href="https://cedarclerk.app/privacy">Privacy</a>
        <!--UptimeRobot's own domain, not status.mooexe.dev: a custom domain is a paid feature there
        (13.08.2026). Swap the href when the plan changes; the DNS record already points at them.-->
        <a href="https://stats.uptimerobot.com/jKcnizZ9vU" target="_blank" rel="noopener">Status</a>
        <!--T-360 — the moderation channel on every public page: mail, because the reader needs no
        account here and the address is already public on Terms/Privacy. The page URL is appended
        by the script below, since this template renders once for every page.-->
        <a href="mailto:cedarworks@mooexe.dev?subject=Content%20report" data-report>Report</a>
        </nav>
        <!--Marty's DigitalOcean referral badge (11.08.2026). `loading=lazy` and explicit dimensions
        so a slow CDN cannot shift the page as it arrives, and rel=noopener because it leaves the
        site.-->
        <a class="footer-badge" href="https://www.digitalocean.com/?refcode=925f882ca720&amp;utm_campaign=Referral_Invite&amp;utm_medium=Referral_Program&amp;utm_source=badge" target="_blank" rel="noopener">
        <img src="https://web-platforms.sfo2.cdn.digitaloceanspaces.com/WWW/Badge%202.svg" alt="DigitalOcean Referral Badge" width="160" height="34" loading="lazy">
        </a>
        </div></div>
        <script>
        /* T-360 - the report link carries the page it was pressed on. */
        (function () {
            var report = document.querySelector('a[data-report]');
            if (report) report.href += '&body=' + encodeURIComponent(location.href);
        })();
        /* Idea #11 - one popup element reused by every term, positioned under whichever term is
           active. Hover for a pointer, focus/tap for everything else; Escape and any outside
           click dismiss it. The description arrives as a data attribute already escaped by the
           renderer, so it is inserted as TEXT here - textContent, never innerHTML - which keeps
           owner-authored text from becoming markup on a public page. */
        (function () {
            var terms = document.querySelectorAll('.glossary-term');
            if (!terms.length) return;

            var pop = document.createElement('div');
            pop.className = 'glossary-pop';
            pop.hidden = true;
            var img = document.createElement('img');
            img.hidden = true;
            var title = document.createElement('p');
            title.className = 'glossary-pop-term';
            var desc = document.createElement('p');
            desc.className = 'glossary-pop-desc';
            pop.appendChild(img);
            pop.appendChild(title);
            pop.appendChild(desc);
            document.body.appendChild(pop);

            var current = null;

            function hide() {
                pop.hidden = true;
                current = null;
            }

            function show(el) {
                current = el;
                title.textContent = el.getAttribute('data-term') || '';
                desc.textContent = el.getAttribute('data-desc') || '';
                var src = el.getAttribute('data-img');
                if (src) { img.src = src; img.hidden = false; } else { img.removeAttribute('src'); img.hidden = true; }

                pop.hidden = false;
                var r = el.getBoundingClientRect();
                var top = r.bottom + window.scrollY + 6;
                var left = r.left + window.scrollX;
                /* Keep it on screen on a narrow phone, where a term near the right edge would
                   otherwise push the popup off the viewport and cause a horizontal scroll. */
                var maxLeft = window.scrollX + document.documentElement.clientWidth - pop.offsetWidth - 8;
                pop.style.top = top + 'px';
                pop.style.left = Math.max(window.scrollX + 8, Math.min(left, maxLeft)) + 'px';
            }

            terms.forEach(function (el) {
                el.addEventListener('mouseenter', function () { show(el); });
                el.addEventListener('mouseleave', function () { if (current === el) hide(); });
                el.addEventListener('focus', function () { show(el); });
                el.addEventListener('blur', function () { if (current === el) hide(); });
                el.addEventListener('click', function (e) {
                    e.stopPropagation();
                    if (current === el) hide(); else show(el);
                });
            });

            document.addEventListener('click', hide);
            document.addEventListener('keydown', function (e) { if (e.key === 'Escape') hide(); });
            window.addEventListener('resize', hide);
        })();

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

        /* Click a picture in a post and it opens over the page instead of in a new tab (Marty,
           11.08.2026). One overlay is built once and reused, and the set of images is collected
           from the post body — so a collage or a carousel becomes a gallery with arrows rather
           than eight separate one-image popups.

           An image inside a link is skipped. The renderer cannot produce one today (link is a mark
           and marks only apply to text nodes), so this is a guard for a future it does not have
           yet — but the rule is worth stating: where a click already means "go there", it wins. */
        (function () {
            var sheet = document.querySelector('.post-sheet');
            if (!sheet) return;

            var images = Array.prototype.filter.call(sheet.querySelectorAll('img'), function (img) {
                return !img.closest('a') && !img.classList.contains('reg-static-image');
            });
            if (!images.length) return;

            var box = document.createElement('div');
            box.className = 'lightbox';
            box.hidden = true;
            box.setAttribute('role', 'dialog');
            box.setAttribute('aria-modal', 'true');

            var full = document.createElement('img');
            var cap = document.createElement('div');
            cap.className = 'lightbox-cap';
            var close = button('lightbox-close', String.fromCharCode(215), 'Close');
            var prev = button('lightbox-prev', String.fromCharCode(8249), 'Previous image');
            var next = button('lightbox-next', String.fromCharCode(8250), 'Next image');

            box.appendChild(full);
            box.appendChild(cap);
            box.appendChild(close);
            if (images.length > 1) { box.appendChild(prev); box.appendChild(next); }
            document.body.appendChild(box);

            function button(cls, glyph, label) {
                var b = document.createElement('button');
                b.type = 'button';
                b.className = 'lightbox-btn ' + cls;
                b.textContent = glyph;
                b.setAttribute('aria-label', label);
                return b;
            }

            var at = 0;
            var restoreFocus = null;

            function show(n) {
                at = (n + images.length) % images.length;
                var img = images[at];
                full.src = img.currentSrc || img.src;
                full.alt = img.alt || '';

                /* The caption a reader already sees under the picture, carried over so the
                   enlarged view is not less informative than the small one. Written as text,
                   never as markup: it is owner-authored content on a public page. */
                var figure = img.closest('figure');
                var figcap = figure ? figure.querySelector('figcaption') : null;
                cap.textContent = figcap ? figcap.textContent : '';
                if (images.length > 1) {
                    var counter = document.createElement('span');
                    counter.className = 'lightbox-count';
                    /* A middle dot, not spaces: HTML collapses runs of whitespace, so a padded
                       separator would have rendered as "caption 1 / 4" with nothing between. */
                    counter.textContent = (cap.textContent ? ' · ' : '') + (at + 1) + ' / ' + images.length;
                    cap.appendChild(counter);
                }
            }

            function open(n) {
                restoreFocus = document.activeElement;
                show(n);
                box.hidden = false;
                /* The page behind must not scroll while this is open — a scroll wheel over a
                   full-screen picture that moves the article underneath is disorienting, and the
                   position is lost by the time it is closed. */
                document.body.style.overflow = 'hidden';
                requestAnimationFrame(function () { box.classList.add('open'); });
                close.focus();
            }

            function hide() {
                box.classList.remove('open');
                box.hidden = true;
                full.removeAttribute('src');
                document.body.style.overflow = '';
                if (restoreFocus && restoreFocus.focus) restoreFocus.focus();
            }

            images.forEach(function (img, idx) {
                img.classList.add('zoomable');
                img.addEventListener('click', function () { open(idx); });
            });

            /* Anything that is not an arrow closes it, the picture included: "click it again to
               get out" is the gesture people try first, and a viewer that ignores it feels stuck. */
            box.addEventListener('click', function (e) {
                if (e.target === prev) { show(at - 1); return; }
                if (e.target === next) { show(at + 1); return; }
                hide();
            });

            document.addEventListener('keydown', function (e) {
                if (box.hidden) return;
                if (e.key === 'Escape') { hide(); return; }
                if (images.length < 2) return;
                if (e.key === 'ArrowLeft') { show(at - 1); e.preventDefault(); }
                if (e.key === 'ArrowRight') { show(at + 1); e.preventDefault(); }
            });
        })();

        /* ADR-181 — the reading menu. Two segmented controls over the same mechanism: a value goes
           on <html> as an attribute and into localStorage under its key, and the empty value means
           "no attribute", which is how the theme reaches its third state — back to the system. */
        (function () {
            var btn = document.getElementById('readingBtn');
            var menu = document.getElementById('readingMenu');
            if (!btn || !menu) return;

            var SETTINGS = {
                theme: { attr: 'data-theme', key: 'cedar-blog-theme' },
                read: { attr: 'data-read', key: 'cedar-blog-text-size' },
                face: { attr: 'data-face', key: 'cedar-blog-face' }
            };
            var el = document.documentElement;

            function apply(name, value) {
                var s = SETTINGS[name];
                if (value) {
                    el.setAttribute(s.attr, value);
                    localStorage.setItem(s.key, value);
                } else {
                    el.removeAttribute(s.attr);
                    localStorage.removeItem(s.key);
                }
                mark(name);
            }

            /* The size control has no "no attribute" state to show — m is the default and declares
               nothing — so an absent value reads as m there and as the system on the theme row. */
            function mark(name) {
                var s = SETTINGS[name];
                var current = el.getAttribute(s.attr) || (name === 'read' ? 'm' : '');
                var group = menu.querySelector('[data-seg="' + name + '"]');
                if (!group) return;
                Array.prototype.forEach.call(group.querySelectorAll('button'), function (b) {
                    b.setAttribute('aria-pressed', b.getAttribute('data-value') === current ? 'true' : 'false');
                });
            }

            Object.keys(SETTINGS).forEach(function (name) {
                mark(name);
                var group = menu.querySelector('[data-seg="' + name + '"]');
                if (!group) return;
                group.addEventListener('click', function (e) {
                    var b = e.target.closest ? e.target.closest('button[data-value]') : null;
                    if (b) apply(name, b.getAttribute('data-value'));
                });
            });

            function open(state) {
                menu.hidden = !state;
                btn.setAttribute('aria-expanded', state ? 'true' : 'false');
            }

            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                open(menu.hidden);
            });
            /* A click inside must not close it: the two rows are meant to be tried against the page. */
            menu.addEventListener('click', function (e) { e.stopPropagation(); });
            document.addEventListener('click', function () { open(false); });
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && !menu.hidden) { open(false); btn.focus(); }
            });
        })();

        /* ADR-193 — the index sort/filter dropdown, the same open/close mechanism as the reading
           menu above, kept separate rather than shared: two anchors, two buttons, and the two never
           appear on the same page (one is index-only, the other on every page). */
        (function () {
            var btn = document.getElementById('sortBtn');
            var menu = document.getElementById('sortMenu');
            if (!btn || !menu) return;

            function open(state) {
                menu.hidden = !state;
                btn.setAttribute('aria-expanded', state ? 'true' : 'false');
            }

            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                open(menu.hidden);
            });
            menu.addEventListener('click', function (e) { e.stopPropagation(); });
            document.addEventListener('click', function () { open(false); });
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && !menu.hidden) { open(false); btn.focus(); }
            });
        })();

        /* ADR-192 — copy the URL the reader is actually looking at (whatever ?lang= it carries),
           exactly as the address bar shows it, rather than a cleaned-up variant of it. */
        (function () {
            var btn = document.querySelector('.copy-link-btn');
            if (!btn || !navigator.clipboard) return;
            var label = btn.querySelector('span');
            var original = label ? label.textContent : '';
            var timer = null;
            btn.addEventListener('click', function () {
                navigator.clipboard.writeText(location.href).then(function () {
                    btn.classList.add('copied');
                    if (label) label.textContent = btn.getAttribute('data-copied-label') || original;
                    if (timer) clearTimeout(timer);
                    timer = setTimeout(function () {
                        btn.classList.remove('copied');
                        if (label) label.textContent = original;
                    }, 1800);
                });
            });
        })();

        (function () {
            var nav = document.querySelector('.floating-nav');
            if (!nav) return;
            var topBtn = nav.querySelector('.back-to-top-btn');
            function onScroll() { nav.classList.toggle('visible', window.scrollY > 400); }
            window.addEventListener('scroll', onScroll, { passive: true });
            onScroll();
            if (topBtn) topBtn.addEventListener('click', function () { window.scrollTo({ top: 0, behavior: 'smooth' }); });
        })();

        (function () {
            var form = document.querySelector('.reg-form');
            if (!form) return;
            var slug = location.pathname.replace(/^\/|\/$/g, '');
            var errEl = form.querySelector('.reg-error');
            // The shell is one static template for both languages, so client-side copy reads the
            // page's own lang attribute rather than being interpolated per render.
            var nameRuleText = document.documentElement.lang === 'en'
                ? 'Name must be at least 2 letters and may only contain letters, spaces and hyphens.'
                : 'Имя должно быть не короче 2 букв и содержать только буквы, пробелы и дефисы.';

            form.addEventListener('submit', function (e) {
                e.preventDefault();
                var submitBtn = form.querySelector('.reg-submit');
                var payload = { answers: {} };
                form.querySelectorAll('[data-field]').forEach(function (el) {
                    var key = el.getAttribute('data-field');
                    payload[key === 'social' ? 'socialLink' : key] = el.value.trim();
                });
                // N6 — mirrors RegistrationFieldValidator (Core); the server re-checks it, this
                // only saves a round-trip. \p{L} needs the u flag to match Cyrillic.
                if (payload.name && !(payload.name.length >= 2 && /^[\p{L}\s-]+$/u.test(payload.name) && /\p{L}/u.test(payload.name))) {
                    errEl.textContent = nameRuleText;
                    errEl.hidden = false;
                    return;
                }
                form.querySelectorAll('[data-question]').forEach(function (el) {
                    payload.answers[el.getAttribute('data-question')] = el.value.trim();
                });
                // Multi-choice answers go over as a JSON array inside the same string map — see
                // MultiAnswer in CedarClerk.Core.
                form.querySelectorAll('[data-question-multi]').forEach(function (group) {
                    var picked = [];
                    group.querySelectorAll('input[type=checkbox]').forEach(function (box) {
                        if (box.checked) picked.push(box.value);
                    });
                    payload.answers[group.getAttribute('data-question-multi')] = picked.length ? JSON.stringify(picked) : '';
                });
                // Consent checkbox — .checked, not .value: a checkbox's value attribute is static
                // and reports the same thing whether it's ticked or not, so the generic
                // [data-question] handler above (which reads .value) would always say "answered"
                // even when the reader never ticked it.
                form.querySelectorAll('[data-question-consent]').forEach(function (el) {
                    payload.answers[el.getAttribute('data-question-consent')] = el.checked ? 'yes' : '';
                });

                errEl.hidden = true;
                submitBtn.disabled = true;
                /* Carry the page's language so the server validates against the form this
                   visitor actually saw - a required question that only exists in one language
                   must not be enforced against a reader of another. */
                var gateLang = new URLSearchParams(location.search).get('lang');
                fetch('/api/posts/' + encodeURIComponent(slug) + '/register' + (gateLang ? '?lang=' + encodeURIComponent(gateLang) : ''), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                })
                    .then(function (r) {
                        if (!r.ok) return r.json().then(function (e) { throw new Error(e.error || 'Something went wrong'); });
                        return r.json();
                    })
                    // The cookie comes back on this response, so a reload would already land on
                    // the post — but T-064 wants the URL to carry the access too, so this browser
                    // is not the only one that has it. The reader can now send themselves the link.
                    .then(function (data) {
                        if (data && data.accessUrl) location.href = data.accessUrl;
                        else location.reload();
                    })
                    .catch(function (err) {
                        errEl.textContent = err.message;
                        errEl.hidden = false;
                        submitBtn.disabled = false;
                    });
            });
        })();

        (function () {
            var annEls = document.querySelectorAll('.annotation');
            if (!annEls.length) return;
            var slug = location.pathname.replace(/^\/|\/$/g, '');
            if (!slug) return;

            var PAGE_SIZE = 20;
            var AVATAR_COLORS = ['#7A5A3A', '#375D74', '#3E7A4E', '#8A4A6B', '#5B6E46'];
            var REPLY_LABEL = document.documentElement.lang === 'en' ? 'Reply' : 'Ответить';
            function avatarColor(name) {
                var hash = 0;
                for (var i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) >>> 0;
                return AVATAR_COLORS[hash % AVATAR_COLORS.length];
            }

            // One level of nesting only (Phase 8 Step 7, see the ADR following ADR-035,
            // docs/DECISIONS.md) — splits the flat comment list into top-level comments (paginated
            // by PAGE_SIZE, same as before) and a parentId->replies map (always shown in full
            // alongside their visible parent, never paginated separately).
            function regroup(comments) {
                var topLevel = [];
                var repliesByParent = {};
                comments.forEach(function (c) {
                    if (c.parentCommentId) {
                        (repliesByParent[c.parentCommentId] = repliesByParent[c.parentCommentId] || []).push(c);
                    } else {
                        topLevel.push(c);
                    }
                });
                return { topLevel: topLevel, repliesByParent: repliesByParent };
            }

            function buildCommentItem(c, ownerName, isReply, onReply) {
                var name = c.authorName || 'Anonymous';
                var item = document.createElement('div');
                item.className = isReply ? 'comment-item comment-reply' : 'comment-item';
                if (ownerName && name.toLowerCase() === ownerName.toLowerCase()) item.classList.add('owner');
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
                var cDate = new Date(c.createdAt);
                timeEl.textContent = cDate.toLocaleDateString() + ' ' + cDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
                meta.appendChild(nameEl);
                meta.appendChild(timeEl);
                var text = document.createElement('div');
                text.className = 'comment-text';
                text.textContent = c.text;
                body.appendChild(meta);
                body.appendChild(text);
                if (!isReply && onReply) {
                    var replyBtn = document.createElement('button');
                    replyBtn.type = 'button';
                    replyBtn.className = 'reply-btn';
                    replyBtn.textContent = REPLY_LABEL;
                    replyBtn.addEventListener('click', function () { onReply(c.id, name); });
                    body.appendChild(replyBtn);
                }
                item.appendChild(avatar);
                item.appendChild(body);
                return item;
            }

            function renderCommentsPage(listEl, moreBtn, topLevel, repliesByParent, shownCount, ownerName, onReply) {
                listEl.innerHTML = '';
                topLevel.slice(0, shownCount).forEach(function (c) {
                    listEl.appendChild(buildCommentItem(c, ownerName, false, onReply));
                    (repliesByParent[c.id] || []).forEach(function (r) {
                        listEl.appendChild(buildCommentItem(r, ownerName, true, null));
                    });
                });
                if (moreBtn) moreBtn.hidden = shownCount >= topLevel.length;
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

                var commentBox = el.querySelector('.comment-box');
                var ownerName = commentBox ? commentBox.getAttribute('data-owner-name') : null;
                var commentList = el.querySelector('.comment-list');
                var moreBtn = el.querySelector('.comment-load-more');
                var commentCountEl = el.querySelector('.comment-count');
                var comments = (info.comments || []).slice();
                var shown = Math.min(PAGE_SIZE, comments.length);

                var replyIndicator = el.querySelector('.comment-reply-indicator');
                var replyTargetEl = el.querySelector('.reply-target-name');
                var cancelReplyBtn = el.querySelector('.cancel-reply');
                var form = el.querySelector('.comment-form');
                var parentIdInput = form ? form.querySelector('.comment-parent-id') : null;

                function startReply(id, name) {
                    if (!parentIdInput) return;
                    parentIdInput.value = id;
                    if (replyTargetEl) replyTargetEl.textContent = name;
                    if (replyIndicator) replyIndicator.hidden = false;
                    var textInput = form.querySelector('textarea.comment-text');
                    if (textInput) textInput.focus();
                }
                function cancelReply() {
                    if (parentIdInput) parentIdInput.value = '';
                    if (replyIndicator) replyIndicator.hidden = true;
                }
                if (cancelReplyBtn) cancelReplyBtn.addEventListener('click', cancelReply);

                function render() {
                    var grouped = regroup(comments);
                    if (commentCountEl) commentCountEl.textContent = comments.length;
                    renderCommentsPage(commentList, moreBtn, grouped.topLevel, grouped.repliesByParent, shown, ownerName, startReply);
                }
                render();

                if (moreBtn) {
                    moreBtn.addEventListener('click', function () {
                        shown = Math.min(shown + PAGE_SIZE, regroup(comments).topLevel.length);
                        render();
                    });
                }

                if (form) {
                    form.addEventListener('submit', function (e) {
                        e.preventDefault();
                        var authorInput = form.querySelector('.comment-author');
                        var textInput = form.querySelector('textarea.comment-text');
                        var text = textInput.value.trim();
                        if (!text) return;
                        var parentCommentId = parentIdInput && parentIdInput.value ? parentIdInput.value : null;
                        fetch('/api/posts/' + encodeURIComponent(slug) + '/comments', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ annotationId: annotationId || null, authorName: authorInput.value.trim(), text: text, parentCommentId: parentCommentId })
                        })
                            .then(function (r) { if (!r.ok) return r.json().then(function (e) { throw new Error(e.error || 'failed'); }); return r.json(); })
                            .then(function (c) {
                                comments.unshift(c);
                                if (!c.parentCommentId) shown = Math.min(shown + 1, regroup(comments).topLevel.length);
                                render();
                                textInput.value = '';
                                authorInput.value = '';
                                cancelReply();
                            })
                            .catch(function (err) { alert(err.message || 'Failed to post comment'); });
                    });
                }
            }

            // NF5 — polls piggyback on the same bootstrap fetch as reactions/comments; every real
            // post page has at least the whole-article annotation block, so this fetch always runs.
            function hydratePoll(el, info) {
                var counts = (info && info.counts) || {};
                var myVote = info && info.myVote;
                var total = Object.keys(counts).reduce(function (sum, k) { return sum + counts[k]; }, 0);
                var totalEl = el.querySelector('.poll-total');

                function paint() {
                    el.querySelectorAll('.poll-option').forEach(function (btn) {
                        var opt = btn.getAttribute('data-option');
                        var pct = total > 0 ? Math.round(((counts[opt] || 0) / total) * 100) : 0;
                        var fill = btn.querySelector('.poll-option-fill');
                        var pctEl = btn.querySelector('.poll-option-pct');
                        var voted = !!myVote;
                        if (fill) fill.style.width = voted ? pct + '%' : '0%';
                        if (pctEl) pctEl.textContent = voted ? pct + '%' : '';
                        btn.classList.toggle('voted', myVote === opt);
                    });
                    if (totalEl) totalEl.textContent = total > 0 ? (total === 1 ? '1 vote' : total + ' votes') : '';
                }
                paint();

                el.querySelectorAll('.poll-option').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        var option = btn.getAttribute('data-option');
                        fetch('/api/posts/' + encodeURIComponent(slug) + '/poll', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ pollId: el.getAttribute('data-poll-id'), option: option })
                        })
                            .then(function (r) { return r.json(); })
                            .then(function (res) {
                                counts = res.counts || {};
                                myVote = res.myVote;
                                total = Object.keys(counts).reduce(function (sum, k) { return sum + counts[k]; }, 0);
                                paint();
                            })
                            .catch(function () {});
                    });
                });
            }

            fetch('/api/posts/' + encodeURIComponent(slug) + '/annotations')
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    annEls.forEach(function (el) {
                        var id = el.getAttribute('data-annotation-id') || '';
                        var info = id ? (data.annotations[id] || { counts: {}, myVote: null, comments: [] }) : data.article;
                        hydrate(el, id, info);
                    });
                    document.querySelectorAll('.poll-block').forEach(function (el) {
                        var pollId = el.getAttribute('data-poll-id') || '';
                        hydratePoll(el, (data.polls && data.polls[pollId]) || { counts: {}, myVote: null });
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

    // metaHtml defaults empty so the pages that must reveal nothing to crawlers — 404 and the
    // registration gate — emit nothing by construction rather than by remembering to (ADR-124).
    private static string PageShell(string title, string bodyHtml, string lang, string headerHtml, string metaHtml = "", string mainClass = "")
    {
        var mathAssets = bodyHtml.Contains("math-tex") ? MathAssets : "";
        return ShellTemplate
            // T-101 — the palette comes from the app's stylesheet through the generated
            // DesignTokens, so a colour changed there reaches the blog without anyone copying it.
            .Replace("{{LIGHT_TOKENS}}", DesignTokens.Declarations(DesignTokens.Light, DesignTokens.MaterialsLight))
            .Replace("{{DARK_TOKENS}}", DesignTokens.Declarations(DesignTokens.Dark, DesignTokens.MaterialsDark))
            .Replace("{{FONT_FACES}}", DesignTokens.FontFaces)
            .Replace("{{LANG}}", lang)
            .Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{META}}", metaHtml)
            .Replace("{{MATH_ASSETS}}", mathAssets)
            .Replace("{{HEADER}}", headerHtml)
            .Replace("{{MAIN_CLASS}}", mainClass.Length == 0 ? "" : " " + mainClass)
            .Replace("{{BODY}}", bodyHtml);
    }
}
