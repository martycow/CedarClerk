using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace CedarClerk.Server;

public static class BlogEndpoints
{
    private const int CommentMaxLength = 2000;
    private const int AuthorNameMaxLength = 60;
    private const int ExcerptMaxLength = 140;
    private const int RssItemLimit = 30;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Hardcoded rather than CultureInfo("ru-RU") — the Pi's runtime install is bare (no SDK,
    // see .claude/rules/production-environment.md) and the rest of the codebase never reaches
    // for a non-invariant CultureInfo, so avoid depending on ICU data being present for this.
    private static readonly string[] RuMonthNames =
        ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];

    private record ReactRequest(string? AnnotationId, string Kind);
    private record CommentRequest(string? AnnotationId, string? AuthorName, string Text, Guid? ParentCommentId = null);
    private record RegistrationRequest(string? Name, string? Nickname, string? Email, string? SocialLink, Dictionary<string, string>? Answers);
    private record BlogChannelInfo(string Title, string? Username, int? MemberCount);
    private record MarkSeenRequest(DateTime? SeenAt);

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

            return Results.Ok(new
            {
                currentViews, deltaWeekViews,
                currentLikes, deltaWeekLikes,
                currentComments, deltaWeekComments,
                snapshots,
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
            else if (action == "register" && ctx.Request.Method == HttpMethods.Post)
                await PostRegistrationAsync(ctx, db, slug);
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

        if (segments is ["rss.xml"])
        {
            await RenderRssAsync(ctx, db);
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

    // Shared by every "look up a Draft by slug" call site (RenderPostAsync, GetAnnotationsAsync,
    // PostReactionAsync, PostCommentAsync) — see the ADR following ADR-040, docs/DECISIONS.md.
    // A private draft is only visible once the invite-grant cookie has been set (RenderPostAsync
    // is the only place that sets it, after validating a ?invite= token).
    private static bool HasPrivateAccess(HttpContext ctx, Draft draft) =>
        !draft.IsPrivate || ctx.Request.Cookies.ContainsKey(Consts.General.PrivateAccessCookiePrefix + draft.Id);

    private static async Task GetAnnotationsAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
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
        };

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, result, JsonOpts);
    }

    // Opt-in DM to the post owner via the bot on genuinely new engagement (see the ADR following
    // ADR-039, docs/DECISIONS.md) — not on a toggled-off reaction, not on "dislike". Never lets a
    // failed/unreachable DM affect the anonymous visitor's request; only logs.
    private static async Task NotifyOwnerAsync(HttpContext ctx, CedarDbContext db, string ownerId, string slug, string message)
    {
        var bot = ctx.RequestServices.GetRequiredService<TelegramBotService>();
        if (!bot.IsRunning) return;

        var owner = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => new { u.TelegramUserId, u.NotifyOnEngagement }).FirstOrDefaultAsync();
        if (owner is not { NotifyOnEngagement: true, TelegramUserId: { } chatId }) return;

        try
        {
            var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var url = $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{slug}";
            await bot.Client.SendMessage(chatId, $"{message}\n{url}");
        }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILogger<TelegramBotService>>()
                .LogWarning(ex, "Engagement notification DM failed for owner {OwnerId}", ownerId);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static async Task PostReactionAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null || !HasPrivateAccess(ctx, draft))
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

        var isNewLike = existing is null && req.Kind == "like";
        if (existing is null)
            db.Reactions.Add(new Reaction { DraftId = draft.Id, AnnotationId = annotationId, Kind = req.Kind, VisitorHash = visitor });
        else if (existing.Kind == req.Kind)
            db.Reactions.Remove(existing);
        else
            existing.Kind = req.Kind;
        await db.SaveChangesAsync();

        if (isNewLike)
            await NotifyOwnerAsync(ctx, db, draft.OwnerId, slug, $"👍 Someone liked your post \"{draft.Title}\"");

        var group = await db.Reactions.Where(r => r.DraftId == draft.Id && r.AnnotationId == annotationId).ToListAsync();
        var counts = group.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
        var myVote = group.FirstOrDefault(r => r.VisitorHash == visitor)?.Kind;

        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new { counts, myVote }, JsonOpts);
    }

    // Registration-form submission on a private post (B3). Grants access immediately by setting
    // the same cookie a valid invite token sets — the form collects an audience, it isn't a
    // verification step (nothing confirms the email). See the ADR following ADR-041.
    private static async Task PostRegistrationAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        // Only private posts that actually have a form configured accept submissions.
        // Validated against the form the visitor was actually shown (FI4.1) — a required question
        // that only exists in one language must not be enforced against a reader of another.
        var submitLang = ctx.Request.Query["lang"].FirstOrDefault() is { } sq && Languages.IsTranslationLanguage(sq)
            ? sq
            : Languages.Primary;
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

        db.PostRegistrations.Add(new PostRegistration
        {
            DraftId = draft.Id,
            Name = name,
            Nickname = nickname,
            Email = email,
            SocialLink = social,
            AnswersJson = answers,
            VisitorHash = visitor,
        });
        await db.SaveChangesAsync();

        // N11 — same opt-in plumbing as comment/like notifications (ADR-040): a failed or
        // unreachable DM only logs, it never turns a successful registration into an error.
        var who = name ?? nickname ?? email ?? "Someone";
        await NotifyOwnerAsync(ctx, db, draft.OwnerId, slug, $"📝 {who} filled in the form for \"{draft.Title}\"");

        ctx.Response.Cookies.Append(Consts.General.PrivateAccessCookiePrefix + draft.Id, "1", new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(90),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });

        ctx.Response.StatusCode = StatusCodes.Status201Created;
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new { ok = true }, JsonOpts);
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

    private static async Task PostCommentAsync(HttpContext ctx, CedarDbContext db, string slug)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.BlogSlug == slug && d.IsBlogPublished);
        if (draft is null || !HasPrivateAccess(ctx, draft))
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
            await JsonSerializer.SerializeAsync(ctx.Response.Body, new { error = "That name is reserved for the post's author." }, JsonOpts);
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

        var comment = new Comment { DraftId = draft.Id, AnnotationId = annotationId, AuthorName = authorName, Text = text, ParentCommentId = parentCommentId };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        await NotifyOwnerAsync(ctx, db, draft.OwnerId, slug, $"💬 New comment on \"{draft.Title}\": {Truncate(text, 100)}");

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
        // Private posts never appear in the public list (see the ADR following ADR-040,
        // docs/DECISIONS.md) — listing one would leak its existence even though the single-post
        // page itself 404s for anyone not invited.
        var posts = await db.Drafts.Where(d => d.IsBlogPublished && !d.IsPrivate)
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
            sb.Append("<div class=\"post-list timeline\">");
            string? lastMonthKey = null;
            foreach (var p in filtered)
            {
                var monthKey = p.BlogPublishedAt?.ToString("yyyy-MM") ?? "";
                if (monthKey != lastMonthKey)
                {
                    lastMonthKey = monthKey;
                    if (p.BlogPublishedAt is { } monthDate)
                    {
                        var monthLabel = $"{RuMonthNames[monthDate.Month - 1]} {monthDate.Year}";
                        sb.Append("<div class=\"timeline-month-sep\"><span class=\"sep-line\"></span><span class=\"sep-label\">")
                          .Append(monthLabel).Append("</span><span class=\"sep-line\"></span></div>");
                    }
                }

                var tags = SplitTags(p.Tags);
                var likes = likeCounts.GetValueOrDefault(p.Id);
                var comments = commentCounts.GetValueOrDefault(p.Id);
                var excerpt = Excerpt(p.CedarJson);

                sb.Append("<div class=\"timeline-item\"><span class=\"timeline-dot\"></span>");
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
                sb.Append("</a></div>");
            }
            sb.Append("</div>");
        }

        var channel = await GetBlogChannelInfoAsync(db);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell("Blog", sb.ToString(), Languages.Primary, RenderHeader(channel)));
    }

    private static async Task RenderRssAsync(HttpContext ctx, CedarDbContext db)
    {
        var posts = await db.Drafts.Where(d => d.IsBlogPublished && !d.IsPrivate)
            .OrderByDescending(d => d.BlogPublishedAt)
            .Take(RssItemLimit)
            .Select(d => new { d.Title, d.BlogSlug, d.BlogPublishedAt, d.CedarJson })
            .ToListAsync();

        var channel = await GetBlogChannelInfoAsync(db);
        var siteTitle = System.Net.WebUtility.HtmlEncode(channel?.Title ?? "Cedar Clerk Blog");
        var siteUrl = $"https://{Consts.URLs.BlogHost}/";

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
            sb.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(p.Title)).Append("</title>");
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

        // Private posts (see the ADR following ADR-040, docs/DECISIONS.md): a ?invite= token
        // matching one of this draft's PostInvites grants a long-lived cookie; anything else gets
        // the exact same "Post not found" as a nonexistent slug, so a private post's existence
        // isn't distinguishable from a 404 to anyone not invited.
        if (!HasPrivateAccess(ctx, draft))
        {
            var inviteToken = ctx.Request.Query["invite"].FirstOrDefault();
            var validInvite = inviteToken is not null
                && await db.PostInvites.AnyAsync(pi => pi.DraftId == draft.Id && pi.Token == inviteToken);

            if (!validInvite)
            {
                // With a registration form configured the post is "locked", not "hidden" (B3) —
                // a deliberate departure from the indistinguishable-from-404 behaviour above,
                // which still applies when no form is set. See the ADR following ADR-041.
                // FI4.1 — the gate answers in the language the reader asked for: their own form
                // if the owner wrote one for it, the primary-language form otherwise. It used to
                // hardcode the primary language, so an EN reader of a private post was greeted
                // in Russian even when an EN form existed.
                var gateLang = ctx.Request.Query["lang"].FirstOrDefault() is { } q && Languages.IsTranslationLanguage(q)
                    ? q
                    : Languages.Primary;
                if (RegistrationFormSet.Pick(draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson, gateLang) is { } form)
                {
                    ctx.Response.StatusCode = StatusCodes.Status200OK;
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.WriteAsync(PageShell(draft.Title,
                        CedarToBlogHtmlRenderer.RegistrationFormHtml(form, draft.Title, gateLang),
                        gateLang, RenderHeader(channel)));
                    return;
                }

                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(PageShell("Not found", "<p class=\"empty\">Post not found.</p>", Languages.Primary, RenderHeader(channel)));
                return;
            }

            ctx.Response.Cookies.Append(Consts.General.PrivateAccessCookiePrefix + draft.Id, "1", new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(90),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });
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
        }

        var availableLanguages = await db.DraftTranslations.Where(t => t.DraftId == draft.Id)
            .Select(t => t.Language)
            .ToListAsync();

        var requestedLang = ctx.Request.Query["lang"].FirstOrDefault();
        var lang = Languages.Primary;
        var title = draft.Title;
        var cedarJson = draft.CedarJson;
        var notTranslatedNotice = "";
        if (requestedLang is not null && requestedLang != Languages.Primary && Languages.IsTranslationLanguage(requestedLang))
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
        var tagsRow = tags.Count == 0 ? "" :
            "<div class=\"post-tags-row\">" + string.Join("", tags.Select(t =>
                $"<a class=\"post-tag-chip\" href=\"{TagFilterUrl([t])}\">#{System.Net.WebUtility.HtmlEncode(t)}</a>")) + "</div>";

        var owner = await db.Users.Where(u => u.Id == draft.OwnerId)
            .Select(u => new
            {
                u.PostSignature, u.PostSignatureUrl, u.AuthorDisplayName, u.ProfileUrl, u.ProfileLocation,
                u.HeaderSlot1Type, u.HeaderSlot2Type, u.HeaderSlot3Type, u.PlanTier, u.PlanExpiresAt,
                u.TelegramLinkText,
            })
            .FirstAsync();
        var ownerPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
        var signatureBlock = SignatureHtml(PlanLimitations.ResolveSignature(ownerPlan, owner.PostSignature, owner.PostSignatureUrl), "span");

        var headerSlotsLine = RenderHeaderSlotsLine(owner.HeaderSlot1Type, owner.HeaderSlot2Type, owner.HeaderSlot3Type,
            owner.AuthorDisplayName, owner.ProfileUrl, owner.ProfileLocation,
            ownerPlan, draft.BlogPublishedAt, cedarJson);

        var body = CedarToBlogHtmlRenderer.Render(cedarJson, $"https://{Consts.URLs.BlogHost}", lang);
        var dateLine = draft.BlogPublishedAt is { } published
            ? $"<span class=\"post-card-date\">{published.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}</span>"
            : "";
        // I15 — author's own wording when set; escaped, unlike the built-in defaults which carry
        // their own arrow entity.
        var viewInTelegramLabel = string.IsNullOrWhiteSpace(owner.TelegramLinkText)
            ? (lang == Languages.English ? Consts.CrossLinks.DefaultTelegramLinkTextEn : Consts.CrossLinks.DefaultTelegramLinkTextRu)
            : System.Net.WebUtility.HtmlEncode(owner.TelegramLinkText.Trim());
        var telegramLink = draft is { LastTelegramUsername: not null, LastTelegramMessageId: not null }
            ? $"<a class=\"telegram-link\" href=\"https://t.me/{draft.LastTelegramUsername}/{draft.LastTelegramMessageId}\" target=\"_blank\" rel=\"noopener\">{viewInTelegramLabel}</a>"
            : "";
        var viewsLine = $"<span class=\"post-card-views\">&#128065; {viewCount}</span>";

        var metaRow = $"<div class=\"post-meta-row\">{dateLine}{viewsLine}{langSwitch}</div>";
        var footerRow = (signatureBlock.Length > 0 || telegramLink.Length > 0)
            ? $"<div class=\"post-footer-row\">{signatureBlock}<div class=\"spacer\"></div>{telegramLink}</div>"
            : "";

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

        var postSheet = $"""
            <div class="post-sheet">
            {metaRow}
            {tagsRow}
            {notTranslatedNotice}
            {titleBlock}
            {body}
            {footerRow}
            {watermark}
            </div>
            """;

        var articleBlock = "<div class=\"annotation article-annotation\" data-annotation-id=\"\">"
            + CedarToBlogHtmlRenderer.AnnotationControlsHtml(lang, owner.AuthorDisplayName, draft.BlogPublishedAt) + "</div>";

        var backLinkLabel = lang == Languages.English ? "All posts" : "Все посты";
        var backToTopLabel = lang == Languages.English ? "Back to top" : "Наверх";
        var floatingNav = $"""
            <div class="floating-nav">
            <a class="floating-nav-btn" href="/" title="{backLinkLabel}">&#9776;</a>
            <button type="button" class="floating-nav-btn back-to-top-btn" title="{backToTopLabel}">&#8593;</button>
            </div>
            """;
        var html = $"""
            <a class="back-link" href="/">&larr; {backLinkLabel}</a>
            {postSheet}
            {articleBlock}
            {floatingNav}
            """;

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(title, html, lang, RenderHeader(channel)));
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

    // Blog-only (see docs/ROADMAP.md Phase 8 Step 4 / ADR in docs/DECISIONS.md) — a subtitle line
    // under the title, distinct from post-meta-row's date/views/tags. Slot 3 is clamped away for
    // any tier below Pro, even if the column still holds a value from before a downgrade.
    private static string RenderHeaderSlotsLine(
        HeaderSlotType? slot1, HeaderSlotType? slot2, HeaderSlotType? slot3,
        string? authorDisplayName, string? profileUrl, string? profileLocation,
        PlanTiers currentPlan, DateTime? publishedAt, string cedarJson)
    {
        var configured = new[] { slot1, slot2, slot3 }.Take(PlanLimitations.MaxHeaderSlots(currentPlan));

        string text;
        try { text = string.Join(" ", TipTapTextNodes.ExtractTexts(cedarJson)).Trim(); }
        catch (Exception) { text = ""; }
        var wordCount = text.Length == 0 ? 0 : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var ctx = new HeaderSlotContext(authorDisplayName, profileUrl, profileLocation, publishedAt, text.Length, wordCount);

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
        .post-list.timeline { position: relative; padding-left: 24px; }
        .post-list.timeline::before { content: ""; position: absolute; left: 4px; top: 6px; bottom: 6px; width: 2px; background: var(--border); }
        .timeline-month-sep { display: flex; align-items: center; gap: 10px; margin: 4px 0 -2px -24px; }
        .timeline-month-sep:first-child { margin-top: 0; }
        .timeline-month-sep .sep-line { flex: 1; height: 1px; background: var(--border); }
        .timeline-month-sep .sep-label { flex: none; font-size: 11px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--t2); background: var(--asoft); border: 1px solid var(--abord); border-radius: 999px; padding: 3px 12px; white-space: nowrap; }
        .timeline-item { position: relative; }
        .timeline-dot { position: absolute; left: -24px; top: 24px; width: 10px; height: 10px; border-radius: 50%; background: var(--accent); border: 2px solid var(--bg); box-shadow: 0 0 0 1px var(--abord); z-index: 1; }
        .post-card { display: block; background: var(--sheet); border-radius: 12px; box-shadow: var(--shadow); padding: 20px 24px; border: 1px solid transparent; color: var(--text); }
        .post-card:hover { border-color: var(--abord); }
        .post-card-meta { display: flex; align-items: center; gap: 8px; margin: 0 0 6px; font-size: 11.5px; color: var(--t3); }
        .post-card-langs { font-size: 10px; font-weight: 600; letter-spacing: .04em; color: var(--accent); background: var(--asoft); border-radius: 4px; padding: 2px 6px; }
        .post-card-title { font-size: 19px; font-weight: 700; letter-spacing: -.01em; line-height: 1.3; margin: 0 0 6px; }
        .post-card-excerpt { font-size: 14px; color: var(--t2); line-height: 1.55; margin: 0 0 10px; }
        .post-card-stats { font-size: 12px; color: var(--t3); }

        .back-link { display: inline-flex; align-items: center; gap: 5px; font-size: 13px; font-weight: 500; padding: 4px 0; margin: 0 0 12px; }
        .back-link:hover { text-decoration: underline; }
        .post-sheet { position: relative; background: var(--sheet); border-radius: 12px; box-shadow: var(--shadow); padding: 32px 40px 28px; }
        /* I7 — tiled over the post, not behind it. pointer-events:none so it can't take a click,
           and user-select:none so dragging across the page doesn't select the watermark. The
           tile itself (an SVG data URI) comes from WatermarkRenderer as an inline style. */
        .watermark-overlay { position: absolute; inset: 0; z-index: 2; pointer-events: none; user-select: none; border-radius: 12px; background-repeat: repeat; }
        .post-sheet h1 { font-size: 27px; font-weight: 700; letter-spacing: -.015em; line-height: 1.22; margin: 0 0 12px; text-align: center; }
        .post-header-slots { font-size: 13px; color: var(--t3); margin: 0 0 18px; text-align: center; }
        .post-header-slots a { color: var(--accent); text-decoration: none; }
        .post-header-slots a:hover { text-decoration: underline; }
        .post-title-divider { display: flex; align-items: center; justify-content: center; gap: 12px; margin: 0 0 28px; }
        .post-title-divider .tdl { height: 1px; width: 70px; background: var(--border); }
        .post-title-divider i { width: 6px; height: 6px; flex: none; display: block; background: var(--accent); opacity: .55; transform: rotate(45deg); }
        .post-tags-row { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 14px; padding: 10px 12px; background: var(--alt); border-radius: 9px; }
        .post-tag-chip { display: inline-block; font-size: 12px; font-weight: 500; color: var(--accent); background: var(--asoft); border: 1px solid var(--abord); border-radius: 999px; padding: 3px 12px; }
        .post-tag-chip:hover { filter: brightness(1.05); }
        .post-sheet h2 { font-size: 20px; font-weight: 600; letter-spacing: -.01em; margin: 24px 0 8px; }
        .post-sheet p { font-size: 16px; line-height: 1.65; margin: 0 0 14px; }
        .toc { background: var(--asoft); border: 1px solid var(--abord); border-radius: 10px; padding: 14px 18px; margin: 0 0 18px; }
        .toc-title { font-size: 11px; font-weight: 700; letter-spacing: .05em; text-transform: uppercase; color: var(--accent); margin: 0 0 8px; }
        .toc ul { list-style: none; margin: 0; padding: 0; font-size: 14px; line-height: 1.8; }
        .toc li a { color: var(--text); }
        .toc li a:hover { color: var(--accent); text-decoration: underline; }
        .toc .toc-lvl-2 { padding-left: 14px; }
        .toc .toc-lvl-3 { padding-left: 28px; }
        .toc .toc-lvl-4 { padding-left: 42px; }
        .toc .toc-lvl-5 { padding-left: 56px; }
        .toc .toc-lvl-6 { padding-left: 70px; }
        .not-translated-notice { background: var(--asoft); border: 1px solid var(--abord); border-radius: 10px; padding: 10px 14px; margin: 0 0 14px; font-size: 13px; color: var(--t2); font-style: italic; }
        .floating-nav { position: fixed; right: 20px; bottom: 20px; display: flex; flex-direction: column; gap: 8px; z-index: 50; opacity: 0; pointer-events: none; transition: opacity .15s ease; }
        .floating-nav.visible { opacity: 1; pointer-events: auto; }
        .floating-nav-btn { width: 38px; height: 38px; border-radius: 50%; background: var(--sheet); border: 1px solid var(--border); box-shadow: var(--shadow); display: flex; align-items: center; justify-content: center; color: var(--text); text-decoration: none; cursor: pointer; font-size: 16px; }
        .floating-nav-btn:hover { background: var(--alt); }
        .post-meta-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin: 0 0 10px; font-size: 12px; color: var(--t3); }
        .lang-switch-track { display: flex; gap: 2px; background: var(--alt); border-radius: 7px; padding: 2px; }
        .lang-switch-btn { border: none; background: none; border-radius: 5px; padding: 3px 11px; font-size: 11.5px; font-weight: 600; color: var(--t2); }
        .lang-switch-btn.current { background: var(--sheet); box-shadow: var(--shadow); color: var(--text); }
        .post-footer-row { display: flex; align-items: center; gap: 10px; border-top: 1px solid var(--border); padding: 14px 0 0; margin-top: 14px; }
        .post-signature { font-size: 13.5px; font-style: italic; color: var(--t2); white-space: pre-line; }
        .telegram-link { font-size: 12.5px; font-weight: 500; color: var(--accent); }

        .spoiler { background: var(--t3); color: transparent; border-radius: 4px; padding: 0 5px; cursor: pointer; transition: background .2s; }
        .spoiler:hover, .spoiler:focus { background: var(--alt); color: inherit; }
        .post-sheet code { font-family: var(--font-mono); font-size: .85em; background: var(--alt); border-radius: 4px; padding: 1px 6px; }
        .post-sheet pre { background: #22201A; color: #C9C08C; border-radius: 8px; padding: 12px 14px; overflow-x: auto; }
        .post-sheet pre code { background: none; padding: 0; font-size: 13.5px; line-height: 1.55; }
        .post-sheet blockquote { border-left: 3px solid var(--abord); padding: 2px 0 2px 14px; color: var(--t2); margin: 0 0 16px; }
        .post-sheet hr { border: none; border-top: 1px solid var(--border); margin: 24px 0; }
        .post-sheet ul, .post-sheet ol { font-size: 16px; line-height: 1.7; padding-left: 20px; margin: 0 0 16px; }
        .post-sheet figure { margin: 0 0 16px; }
        .post-sheet figcaption { text-align: center; font-size: 13px; color: var(--t2); margin-top: 6px; }
        .audio-title { font-size: 13.5px; font-weight: 600; color: var(--text); margin: 10px 0 4px; }
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
        .youtube-embed { position: relative; width: 100%; aspect-ratio: 16 / 9; margin: 0 0 16px; border-radius: 6px; overflow: hidden; }
        .youtube-embed iframe { position: absolute; inset: 0; width: 100%; height: 100%; border: none; }
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
        .comment-published-line { font-size: 11.5px; color: var(--t3); margin: -8px 0 12px; }
        .comment-list { display: flex; flex-direction: column; gap: 4px; margin: 0 0 14px; }
        .comment-item { display: flex; gap: 10px; padding: 8px 10px; border-radius: 9px; transition: background .25s; }
        .comment-item.glow { background: var(--asoft); }
        .comment-item.owner { background: var(--asoft); border: 1px solid var(--abord); }
        .comment-item.owner .comment-meta::after { content: '★'; color: var(--accent); font-size: 11px; }
        .comment-item.comment-reply { margin-left: 30px; padding-top: 6px; padding-bottom: 6px; }
        .comment-item.comment-reply .comment-avatar { width: 22px; height: 22px; font-size: 9.5px; }
        .comment-item.comment-reply .comment-meta { font-size: 12px; }
        .comment-item.comment-reply .comment-text { font-size: 13px; }
        .comment-avatar { width: 28px; height: 28px; border-radius: 50%; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 700; flex: none; }
        .comment-meta { display: flex; align-items: baseline; gap: 7px; font-size: 13px; font-weight: 600; }
        .comment-meta time { font-size: 11px; font-weight: 400; color: var(--t3); }
        .comment-anchor { font-size: 11px; color: var(--accent); background: var(--asoft); border-radius: 5px; padding: 2px 7px; display: inline-block; margin: 3px 0 1px; }
        .comment-text { font-size: 14px; line-height: 1.5; }
        .reply-btn { align-self: flex-start; margin-top: 4px; background: none; border: none; color: var(--t3); font-size: 12px; font-family: inherit; cursor: pointer; padding: 0; }
        .reply-btn:hover { color: var(--accent); text-decoration: underline; }
        /* IB5: a `display` rule beats the [hidden] attribute's default `display: none`, so both
           the reply indicator and the load-more button below stayed on screen no matter what the
           script set — the reply target looked impossible to clear, and "show more" was offered
           when there was no more. One global rule rather than a per-class fix, so the next
           element scripted through `hidden` doesn't reintroduce it. */
        [hidden] { display: none !important; }
        .comment-reply-indicator { display: flex; align-items: center; gap: 6px; font-size: 12.5px; color: var(--t3); margin: 0 0 8px; }
        .comment-reply-indicator .reply-target-name { font-weight: 600; color: var(--text); }
        .comment-reply-indicator .cancel-reply { background: none; border: 1px solid var(--border); border-radius: 999px; padding: 1px 9px; font-size: 11.5px; color: var(--t2); cursor: pointer; font-family: inherit; }
        .comment-load-more { display: block; margin: 0 0 10px; background: none; border: 1px solid var(--border); border-radius: 6px; padding: 4px 10px; cursor: pointer; color: var(--text); font: inherit; font-size: 12.5px; }
        /* Registration gate for private posts (B3) — replaces the article body entirely. */
        .reg-gate { display: flex; justify-content: center; padding: 8px 0 40px; }
        .reg-card { background: var(--sheet); border-radius: 12px; box-shadow: var(--shadow); padding: 28px 30px; max-width: 460px; width: 100%; }
        .reg-title { font-size: 22px; margin: 0 0 10px; }
        .reg-lock { font-size: 12px; letter-spacing: .05em; text-transform: uppercase; font-weight: 600; color: var(--t3); margin-bottom: 10px; }
        .reg-blurb { font-size: 14px; color: var(--t2); margin: 0 0 6px; }
        .reg-intro { font-size: 14px; line-height: 1.5; margin: 0 0 16px; }
        .reg-form { display: flex; flex-direction: column; gap: 10px; margin-top: 14px; }
        .reg-input { border: 1px solid var(--border); background: var(--sheet); color: var(--text); border-radius: 8px; padding: 10px 12px; font-size: 14px; font-family: inherit; outline: none; width: 100%; box-sizing: border-box; }
        .reg-input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--asoft); }
        .reg-question { display: flex; flex-direction: column; gap: 5px; }
        .reg-question-label { font-size: 13px; font-weight: 500; }
        .reg-multi { display: flex; flex-direction: column; gap: 6px; }
        .reg-multi-option { display: flex; align-items: center; gap: 8px; font-size: 14px; cursor: pointer; }
        .reg-submit { border: none; background: var(--accent); color: #F4F2EA; border-radius: 8px; padding: 11px 18px; font-size: 14px; font-weight: 500; cursor: pointer; font-family: inherit; margin-top: 4px; }
        .reg-submit:hover { filter: brightness(1.08); }
        .reg-submit:disabled { opacity: .6; cursor: default; }
        .reg-error { color: var(--danger); font-size: 13px; margin: 4px 0 0; }

        /* IB5: was three stacked full-width rows (name, textarea, a full-width Send slab). The
           comment box is a secondary element on the page, so it now leads with the textarea and
           puts the optional name next to a normal-sized Send button on one row. */
        .comment-form { display: flex; flex-direction: column; gap: 8px; }
        .comment-form input, .comment-form textarea { border: 1px solid var(--border); background: var(--sheet); color: var(--text); border-radius: 8px; padding: 9px 12px; font-size: 13.5px; font-family: inherit; outline: none; }
        .comment-form textarea { min-height: 62px; resize: vertical; }
        .comment-form input:focus, .comment-form textarea:focus { border-color: var(--accent); box-shadow: 0 0 0 3px var(--asoft); }
        .comment-form-row { display: flex; gap: 8px; }
        .comment-form-row .comment-author { flex: 1; min-width: 0; }
        .comment-form button { flex: none; border: none; background: var(--accent); color: #F4F2EA; border-radius: 8px; padding: 9px 18px; font-size: 13.5px; font-weight: 500; cursor: pointer; font-family: inherit; }
        .comment-form button:hover { filter: brightness(1.08); }

        .site-footer { border-top: 1px solid var(--border); background: var(--surface); }
        .site-footer-inner { max-width: 760px; margin: 0 auto; display: flex; align-items: center; justify-content: center; gap: 8px; padding: 16px 20px; font-size: 12px; color: var(--t3); }

        @media (max-width: 480px) {
            .post-sheet { padding: 22px 16px 20px; }
            .comment-box { padding: 16px; }
            .tg-open-btn span.tg-open-label { display: none; }
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

                errEl.hidden = true;
                submitBtn.disabled = true;
                fetch('/api/posts/' + encodeURIComponent(slug) + '/register', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                })
                    .then(function (r) {
                        if (!r.ok) return r.json().then(function (e) { throw new Error(e.error || 'Something went wrong'); });
                        return r.json();
                    })
                    // The access cookie comes back on this response — reloading lands on the post.
                    .then(function () { location.reload(); })
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
