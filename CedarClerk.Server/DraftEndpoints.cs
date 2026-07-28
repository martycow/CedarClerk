using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Ai;
using CedarClerk.Server.Email;
using CedarClerk.Server.Translation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftEndpoints
{
    public record SaveDraftRequest(string Title, string CedarJson);
    public record SaveTranslationRequest(string Title, string CedarJson);
    public record UpdateTagsRequest(string Tags);
    public record RenameTagRequest(string From, string To);
    public record UpdateFolderRequest(Guid? FolderId);
    public record UpdatePrivateRequest(bool IsPrivate);
    public record UpdateTemplateRequest(bool IsTemplate);
    public record UpdateListedRequest(bool IsListedWhilePrivate);
    public record UpdateWatermarkRequest(string? WatermarkText);
    public record UpdateSlugRequest(string? Slug);
    public record UpdateArticleTitleRequest(string? ArticleTitle);
    public record AddInviteRequest(string Email);
    // FI4.1 — Language names which language slot the form belongs to; absent or primary writes
    // the post's own RegistrationFormJson, anything else goes into the translations object.
    public record UpdateRegistrationFormRequest(string? FormJson, string? Language = null);

    private const int InviteEmailMaxLength = 254;

    // Matches the editor's own tag input (maxlength=30) - the tag-management endpoints below are
    // a second way in, and a longer tag would render as one that can't be typed.
    private const int TagMaxLength = 30;

    private const long CedarZipMaxBytes = 50 * 1024 * 1024;
    private const int CedarMaxAssetCount = 50;

    // A bulk Notion-shaped export routinely carries far more images than a personal .cedar
    // draft — a single large page can easily have 100+ (verified against a real 216-image
    // export, 17.07.2026) — so markdown import gets its own, more generous caps rather than
    // sharing the .cedar ones. See ADR-026, docs/DECISIONS.md.
    private const long MarkdownZipMaxBytes = 200 * 1024 * 1024;
    private const int MarkdownMaxImageCount = 300;

    private static readonly Dictionary<string, string> ImportImageExtensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };

    private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
    };

    private static List<string> SplitTagList(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static void MapDraftEndpoints(this WebApplication app)
    {
        var groupBuilder = app.MapGroup("/api/drafts").RequireAuthorization();
        
        groupBuilder.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var drafts = await db.Drafts.Where(d => d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new
                {
                    d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                    d.IsArchived, d.LastTelegramMessageId, d.LastTelegramUsername, d.FolderId, d.IsPrivate, d.IsTemplate, d.ViewCount,
                    Translations = db.DraftTranslations.Where(t => t.DraftId == d.Id)
                        .Select(t => new { t.Language, t.UpdatedAt }).ToList(),
                })
                .ToListAsync();

            // Most recent Pending-or-Failed schedule per draft — a /drafts screen "Scheduled"/
            // "Failed" badge only means something for a real, persisted ScheduledPost row (see
            // ADR-035: an immediate export failure isn't persisted anywhere, unlike this one).
            var draftIds = drafts.Select(d => d.Id).ToList();
            var scheduledRows = await db.ScheduledPosts
                .Where(s => draftIds.Contains(s.DraftId) && (s.Status == "Pending" || s.Status == "Failed"))
                .ToListAsync();
            var scheduled = scheduledRows
                .GroupBy(s => s.DraftId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ScheduledAtUtc).First());

            // Activity column (B23): totals plus what accumulated since the previous session.
            // Reactions are counted across both kinds — the screen answers "did anything happen
            // here", the like/dislike split stays on the blog post page.
            var reactionCounts = await db.Reactions
                .Where(r => draftIds.Contains(r.DraftId))
                .GroupBy(r => r.DraftId)
                .Select(g => new { DraftId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DraftId, x => x.Count);

            var seenRows = await db.DraftStatSeens.Where(x => x.OwnerId == uid).ToListAsync();
            var seen = seenRows.ToDictionary(x => x.DraftId);
            var now = DateTime.UtcNow;
            var deltas = new Dictionary<Guid, (int Views, int Reactions)>();

            foreach (var d in drafts)
            {
                var views = d.ViewCount;
                var reactions = reactionCounts.GetValueOrDefault(d.Id);

                if (!seen.TryGetValue(d.Id, out var row))
                {
                    // First time this draft is listed — no earlier session to compare against.
                    db.DraftStatSeens.Add(new DraftStatSeen
                    {
                        OwnerId = uid, DraftId = d.Id,
                        BaselineViewCount = views, BaselineReactionCount = reactions,
                        LastViewCount = views, LastReactionCount = reactions, SeenAt = now,
                    });
                    deltas[d.Id] = (0, 0);
                    continue;
                }

                if (now - row.SeenAt > Consts.DraftActivity.SessionGap)
                {
                    row.BaselineViewCount = row.LastViewCount;
                    row.BaselineReactionCount = row.LastReactionCount;
                }
                row.LastViewCount = views;
                row.LastReactionCount = reactions;
                row.SeenAt = now;

                // Max(0, …): a deleted reaction can put the total below the baseline.
                deltas[d.Id] = (Math.Max(0, views - row.BaselineViewCount), Math.Max(0, reactions - row.BaselineReactionCount));
            }

            await db.SaveChangesAsync();

            return drafts.Select(d => new
            {
                d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                d.IsArchived, d.LastTelegramMessageId, d.LastTelegramUsername, d.FolderId, d.IsPrivate, d.IsTemplate,
                d.ViewCount,
                ReactionCount = reactionCounts.GetValueOrDefault(d.Id),
                NewViewCount = deltas[d.Id].Views,
                NewReactionCount = deltas[d.Id].Reactions,
                Languages = d.Translations.Select(t => t.Language).ToList(),
                StaleLanguages = d.Translations.Where(t => t.UpdatedAt < d.UpdatedAt).Select(t => t.Language).ToList(),
                Scheduled = scheduled.TryGetValue(d.Id, out var s)
                    ? new { s.ScheduledAtUtc, s.ChatId, s.Status, s.Error }
                    : null,
            });
        });

        groupBuilder.MapPost("/{id:guid}/archive", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.IsArchived = true;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsArchived });
        });

        groupBuilder.MapPost("/{id:guid}/unarchive", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.IsArchived = false;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsArchived });
        });
        
        groupBuilder.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();


            var translations = await db.DraftTranslations.Where(t => t.DraftId == id)
                .Select(t => new { t.Language, t.Title, t.UpdatedAt })
                .ToListAsync();
            return Results.Ok(new
            {
                draft.Id, draft.Title, draft.CedarJson, draft.CreatedAt, draft.UpdatedAt, draft.BlogSlug,
                draft.IsBlogPublished, draft.BlogPublishedAt, draft.Tags, draft.FolderId, draft.IsPrivate,
                draft.WatermarkText, draft.ArticleTitle, draft.IsListedWhilePrivate,
                draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson,
                // FI4.1 — which languages a reader would actually be greeted in.
                FormLanguages = RegistrationFormSet.LanguagesWithForm(draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson),
                Translations = translations,
            });
        });

        // Backs the export modal's "Files" list — every media asset referenced by this draft
        // (RU and, if it exists, its EN translation), with size and Telegram-compression status,
        // so Marty can see what's actually embedded before publishing large photos.
        groupBuilder.MapGet("/{id:guid}/assets", async (Guid id, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var names = CedarPackage.FindReferencedMediaPaths(draft.CedarJson).ToList();
            var translationJson = await db.DraftTranslations.Where(t => t.DraftId == id).Select(t => t.CedarJson).FirstOrDefaultAsync();
            if (translationJson is not null)
                names = names.Union(CedarPackage.FindReferencedMediaPaths(translationJson)).ToList();

            if (names.Count == 0)
                return Results.Ok(Array.Empty<object>());

            var assets = await db.Assets.Where(a => a.OwnerId == uid && names.Contains(a.LocalPath)).ToListAsync();
            return Results.Ok(assets.Select(a => new
            {
                a.Id,
                a.FileName,
                a.LocalPath,
                a.ContentType,
                a.SizeBytes,
                HasTelegramDerivative = a.TelegramLocalPath is not null,
                TelegramSizeBytes = a.TelegramLocalPath is not null && File.Exists(Path.Combine(media.Dir, a.TelegramLocalPath))
                    ? new FileInfo(Path.Combine(media.Dir, a.TelegramLocalPath)).Length
                    : (long?)null,
            }));
        });

        groupBuilder.MapPut("/{id:guid}/tags", async (Guid id, UpdateTagsRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.Tags = string.Join(",", req.Tags.Split(',')
                .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct());
            
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Tags });
        });
        
        // Backs the tag "cloud" picker (ADR-035) — usage counts across every one of the owner's
        // drafts. No separate Tag table exists; Draft.Tags is a flat comma-separated column, so
        // this aggregates in-memory rather than adding relational tag storage for a picker list.
        // Idea #3 - managing the tag *set* itself, not one draft's tags. Tags are a flat string
        // column rather than an entity (deliberately, see ADR-038's neighbour), so renaming means
        // rewriting every draft that carries it. Both operations propagate to the blog for free:
        // the blog reads Draft.Tags directly, it has no copy of its own.
        groupBuilder.MapPut("/tags", async (RenameTagRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var from = req.From?.Trim().ToLowerInvariant();
            var to = req.To?.Trim().ToLowerInvariant().Replace(",", "");
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return Results.BadRequest(new { error = "Both the old and the new tag are required" });
            if (to.Length > TagMaxLength)
                return Results.BadRequest(new { error = $"Tag is too long ({TagMaxLength} characters maximum)" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var drafts = await db.Drafts.Where(d => d.OwnerId == uid && d.Tags != "").ToListAsync();
            var touched = 0;
            foreach (var draft in drafts)
            {
                var tags = SplitTagList(draft.Tags);
                if (!tags.Contains(from)) continue;
                // Distinct: renaming "foo" to a tag the draft already has must merge, not
                // duplicate. Order is otherwise preserved, so nothing visibly reshuffles.
                var renamed = tags.Select(t => t == from ? to : t).Distinct().ToList();
                draft.Tags = string.Join(",", renamed);
                touched++;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { renamed = touched });
        });

        groupBuilder.MapDelete("/tags/{tag}", async (string tag, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var target = tag.Trim().ToLowerInvariant();
            if (target.Length == 0) return Results.BadRequest(new { error = "Tag is required" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var drafts = await db.Drafts.Where(d => d.OwnerId == uid && d.Tags != "").ToListAsync();
            var touched = 0;
            foreach (var draft in drafts)
            {
                var tags = SplitTagList(draft.Tags);
                if (!tags.Contains(target)) continue;
                draft.Tags = string.Join(",", tags.Where(t => t != target));
                touched++;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { removed = touched });
        });

        groupBuilder.MapGet("/tags", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var allTags = await db.Drafts.Where(d => d.OwnerId == uid && d.Tags != "")
                .Select(d => d.Tags)
                .ToListAsync();

            var counts = allTags
                .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(t => t)
                .Select(g => new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            return Results.Ok(counts);
        });

        // See the ADR following ADR-038, docs/DECISIONS.md — one folder per draft, folderId null
        // unassigns. FolderId itself is a plain scalar (no FK constraint), so ownership of the
        // target folder must be checked here explicitly.
        groupBuilder.MapPut("/{id:guid}/folder", async (Guid id, UpdateFolderRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            if (req.FolderId is { } folderId)
            {
                var ownsFolder = await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == uid);
                if (!ownsFolder) return Results.NotFound();
            }

            draft.FolderId = req.FolderId;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.FolderId });
        });

        // Private posts (see the ADR following ADR-040, docs/DECISIONS.md) — invite by email,
        // gated on the public blog side via BlogEndpoints.HasPrivateAccess.
        groupBuilder.MapPost("/{id:guid}/private", async (Guid id, UpdatePrivateRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.IsPrivate = req.IsPrivate;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsPrivate });
        });

        // NF1 — post templates. Same shape as /private above: one endpoint, a bool body.
        groupBuilder.MapPost("/{id:guid}/template", async (Guid id, UpdateTemplateRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.IsTemplate = req.IsTemplate;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsTemplate });
        });

        // Watermark text tiled over the blog page of a private post (I7). Its own endpoint rather
        // than a field on /private, matching how /tags, /folder and /registration-form each own
        // one concern. Blank clears it.
        // FI3.4 — a published post's URL is the one piece of it that outlives the draft, so it's
        // worth being able to choose. Only reachable once published: before that there is no URL
        // to name, and PublishAsync generates one from the title.
        groupBuilder.MapPost("/{id:guid}/slug", async (Guid id, UpdateSlugRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            if (draft.BlogSlug is null)
                return Results.BadRequest(new { error = "Publish this draft to the blog first" });

            // Run the author's text through the same slugifier the automatic path uses, so a URL
            // typed by hand can't be something the blog router won't match.
            var slug = SlugGenerator.Slugify(req.Slug ?? "");
            if (slug.Length == 0)
                return Results.BadRequest(new { error = "That URL has no usable characters" });

            // Blog lookup is by slug across all owners, so uniqueness has to be global — not
            // per-owner like most things here.
            if (await db.Drafts.AnyAsync(d => d.Id != id && d.BlogSlug == slug))
                return Results.BadRequest(new { error = "That URL is already taken" });

            draft.BlogSlug = slug;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.BlogSlug });
        });

        groupBuilder.MapPost("/{id:guid}/watermark", async (Guid id, UpdateWatermarkRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var text = req.WatermarkText?.Trim();
            if (text is { Length: > Consts.Watermark.MaxLength })
                return Results.BadRequest(new { error = "Watermark text is too long" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.WatermarkText = string.IsNullOrEmpty(text) ? null : text;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.WatermarkText });
        });

        // Registration form (B3) — shown to uninvited visitors of a private post. Length-checked
        // only; the client owns the JSON shape, same treatment as the preference blobs in
        // AuthEndpoints. Null clears the form (back to the plain 404 for uninvited visitors).
        // Idea #4 - the headline the reader sees, when it should differ from the draft's name.
        // Blank clears it, which restores "the name is the title".
        // Semi-public private posts: listed and searchable on the blog, still gated behind the
        // registration form. Its own endpoint rather than a field on /private, matching how
        // /tags, /folder and /slug each own one concern.
        groupBuilder.MapPost("/{id:guid}/listed", async (Guid id, UpdateListedRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.IsListedWhilePrivate = req.IsListedWhilePrivate;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.IsListedWhilePrivate });
        });

        groupBuilder.MapPost("/{id:guid}/article-title", async (Guid id, UpdateArticleTitleRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var title = req.ArticleTitle?.Trim();
            if (title is { Length: > Consts.ArticleTitle.MaxLength })
                return Results.BadRequest(new { error = $"Title is too long ({Consts.ArticleTitle.MaxLength} characters maximum)" });

            draft.ArticleTitle = string.IsNullOrWhiteSpace(title) ? null : title;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.ArticleTitle });
        });

        groupBuilder.MapPost("/{id:guid}/registration-form", async (Guid id, UpdateRegistrationFormRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (req.FormJson is { Length: > Consts.RegistrationForm.FormJsonMaxChars })
                return Results.BadRequest(new { error = "Registration form is too large" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var lang = req.Language is not null && Languages.IsTranslationLanguage(req.Language)
                ? req.Language
                : Languages.Primary;
            if (lang == Languages.Primary)
                draft.RegistrationFormJson = string.IsNullOrWhiteSpace(req.FormJson) ? null : req.FormJson;
            else
                draft.RegistrationFormTranslationsJson =
                    RegistrationFormSet.SetTranslation(draft.RegistrationFormTranslationsJson, lang, req.FormJson);

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                draft.RegistrationFormJson,
                draft.RegistrationFormTranslationsJson,
                FormLanguages = RegistrationFormSet.LanguagesWithForm(draft.RegistrationFormJson, draft.RegistrationFormTranslationsJson),
            });
        });

        groupBuilder.MapGet("/{id:guid}/registrations", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var rows = await db.PostRegistrations.Where(r => r.DraftId == id)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.Id, r.Name, r.Nickname, r.Email, r.SocialLink, r.AnswersJson, r.CreatedAt })
                .ToListAsync();
            return Results.Ok(rows);
        });

        groupBuilder.MapGet("/{id:guid}/invites", async (Guid id, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var invites = await db.PostInvites.Where(pi => pi.DraftId == id).OrderBy(pi => pi.CreatedAt).ToListAsync();
            return Results.Ok(invites.Select(pi => new { pi.Id, pi.Email, pi.CreatedAt, Url = BuildInviteUrl(cfg, draft, pi.Token) }));
        });

        // Always creates the invite + returns a copyable link even if the email fails to send
        // (no email provider configured, Resend error, etc.) — the link is the source of truth,
        // the email is a convenience on top of it.
        groupBuilder.MapPost("/{id:guid}/invites", async (Guid id, AddInviteRequest req, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, ResendEmailProvider email) =>
        {
            var emailAddr = req.Email.Trim();
            if (emailAddr.Length == 0 || emailAddr.Length > InviteEmailMaxLength || !emailAddr.Contains('@'))
                return Results.Json(new { error = "Enter a valid email address" }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            if (draft.BlogSlug is null)
                return Results.Json(new { error = "Publish this draft to the blog first" }, statusCode: StatusCodes.Status400BadRequest);

            var invite = new PostInvite { DraftId = id, Email = emailAddr, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
            db.PostInvites.Add(invite);
            await db.SaveChangesAsync();

            var url = BuildInviteUrl(cfg, draft, invite.Token);
            var emailSent = await email.SendAsync(emailAddr, $"You're invited to read \"{draft.Title}\"",
                $"<p>You've been invited to a private post: <a href=\"{url}\">{System.Net.WebUtility.HtmlEncode(draft.Title)}</a></p>");

            return Results.Ok(new { invite.Id, invite.Email, invite.CreatedAt, Url = url, EmailSent = emailSent });
        });

        groupBuilder.MapDelete("/{id:guid}/invites/{inviteId:guid}", async (Guid id, Guid inviteId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var invite = await db.PostInvites.FirstOrDefaultAsync(pi => pi.Id == inviteId && pi.DraftId == id);
            if (invite is null) return Results.NotFound();

            db.PostInvites.Remove(invite);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        groupBuilder.MapPost("/{id:guid}/invites/{inviteId:guid}/resend", async (Guid id, Guid inviteId, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, ResendEmailProvider email) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var invite = await db.PostInvites.FirstOrDefaultAsync(pi => pi.Id == inviteId && pi.DraftId == id);
            if (invite is null) return Results.NotFound();

            var url = BuildInviteUrl(cfg, draft, invite.Token);
            var emailSent = await email.SendAsync(invite.Email, $"You're invited to read \"{draft.Title}\"",
                $"<p>You've been invited to a private post: <a href=\"{url}\">{System.Net.WebUtility.HtmlEncode(draft.Title)}</a></p>");

            return Results.Ok(new { EmailSent = emailSent });
        });

        groupBuilder.MapGet("/{id:guid}/translations/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            return translation is null
                ? Results.NotFound()
                : Results.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt, translation.SourceSnapshotJson });
        });
        
        groupBuilder.MapPut("/{id:guid}/translations/{lang}", async (Guid id, string lang, SaveTranslationRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (!Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            if (translation is null)
            {
                translation = new DraftTranslation { DraftId = id, Language = lang };
                db.DraftTranslations.Add(translation);
            }
            translation.Title = req.Title;
            translation.CedarJson = req.CedarJson;
            translation.UpdatedAt = DateTime.UtcNow;
            translation.SourceSnapshotJson = draft.CedarJson;
            await db.SaveChangesAsync();
            return Results.Ok(new { translation.Language, translation.UpdatedAt, translation.SourceSnapshotJson });
        });
        
        groupBuilder.MapPost("/{id:guid}/translations/{lang}/auto", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (!Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
            if (draft is null) return Results.NotFound();

            // AI features are Pro Plus; each call counts against the per-day AI quota
            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = "Auto-translate is a Pro Plus feature. Upgrade to use it." }, statusCode: StatusCodes.Status403Forbidden);
            
            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = $"Daily AI limit ({PlanLimitations.AiDailyLimit} calls) reached — resets at midnight UTC." }, statusCode: StatusCodes.Status429TooManyRequests);

            ITranslationProvider? provider;
            try
            {
                provider = TranslationProviderFactory.Create(cfg, httpFactory);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is null)
                return Results.Json(new { error = "Auto-translate is not configured" }, statusCode: StatusCodes.Status501NotImplemented);

            TranslationResult result;
            try
            {
                result = await provider.TranslateAsync(draft.Title, draft.CedarJson, lang, ct);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
            
            try
            {
                using var docCheck = JsonDocument.Parse(result.CedarJson);
                var root = docCheck.RootElement;
                if (root.ValueKind != JsonValueKind.Object || 
                    !root.TryGetProperty("type", out var typeProp) || 
                    typeProp.GetString() != "doc")
                {
                    return Results.Json(new { error = "Translator returned an invalid document — try again" },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "Translator returned invalid JSON — try again" }, statusCode: StatusCodes.Status502BadGateway);
            }

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
            if (translation is null)
            {
                translation = new DraftTranslation { DraftId = id, Language = lang };
                db.DraftTranslations.Add(translation);
            }
            translation.Title = result.Title;
            translation.CedarJson = result.CedarJson;
            translation.UpdatedAt = DateTime.UtcNow;
            translation.SourceSnapshotJson = draft.CedarJson;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt, translation.SourceSnapshotJson });
        });

        groupBuilder.MapPost("/{id:guid}/ai-edit/{lang}/{kind}", async (Guid id, string lang, string kind, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (lang != Languages.Primary && !Languages.IsTranslationLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported language: {lang}" });

            AiEditKind editKind;
            switch (kind)
            {
                case "fix-errors": editKind = AiEditKind.FixErrors; break;
                case "schizo": editKind = AiEditKind.Schizo; break;
                default: return Results.BadRequest(new { error = $"Unknown AI edit kind: {kind}" });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
            if (draft is null) return Results.NotFound();

            // AI features are Pro Plus; each call counts against the per-day AI quota
            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = "AI editing is a Pro Plus feature. Upgrade to use it." }, statusCode: StatusCodes.Status403Forbidden);

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = $"Daily AI limit ({PlanLimitations.AiDailyLimit} calls) reached — resets at midnight UTC." }, statusCode: StatusCodes.Status429TooManyRequests);

            DraftTranslation? translation = null;
            string sourceTitle, sourceCedarJson;
            if (lang == Languages.Primary)
            {
                sourceTitle = draft.Title;
                sourceCedarJson = draft.CedarJson;
            }
            else
            {
                translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
                if (translation is null) return Results.NotFound(new { error = $"No {lang} version to edit yet" });
                sourceTitle = translation.Title;
                sourceCedarJson = translation.CedarJson;
            }

            IAiEditProvider? provider;
            try
            {
                provider = AiEditProviderFactory.Create(cfg, httpFactory);
            }
            catch (AiEditException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is null)
                return Results.Json(new { error = "AI editing is not configured" }, statusCode: StatusCodes.Status501NotImplemented);

            AiEditResult result;
            try
            {
                result = await provider.EditAsync(sourceTitle, sourceCedarJson, editKind, ct);
            }
            catch (AiEditException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }

            try
            {
                using var docCheck = JsonDocument.Parse(result.CedarJson);
                var root = docCheck.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "doc")
                {
                    return Results.Json(new { error = "AI returned an invalid document — try again" },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "AI returned invalid JSON — try again" }, statusCode: StatusCodes.Status502BadGateway);
            }

            if (translation is null)
            {
                draft.Title = result.Title;
                draft.CedarJson = result.CedarJson;
                draft.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                translation.Title = result.Title;
                translation.CedarJson = result.CedarJson;
                translation.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { title = result.Title, cedarJson = result.CedarJson, updatedAt = DateTime.UtcNow });
        });

        groupBuilder.MapDelete("/{id:guid}/translations/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var deleted = await db.DraftTranslations
                .Where(t => t.DraftId == id && t.Language == lang)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
        
        groupBuilder.MapPost("/", async (SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = new Draft { Title = req.Title, CedarJson = req.CedarJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();
            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        });
        
        groupBuilder.MapPut("/{id:guid}", async (Guid id, SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            draft.Title = req.Title;
            draft.CedarJson = req.CedarJson;
            draft.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.UpdatedAt });
        });
        
        groupBuilder.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.Drafts
                .Where(x => x.Id == id && x.OwnerId == uid)
                .ExecuteDeleteAsync();
            if (deleted > 0)
                await db.DraftStatSeens.Where(x => x.DraftId == id && x.OwnerId == uid).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
        
        groupBuilder.MapGet("/{id:guid}/cedar", async (Guid id, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var mediaNames = CedarPackage.FindReferencedMediaPaths(draft.CedarJson);
            var assets = new List<CedarAsset>();
            foreach (var name in mediaNames)
            {
                var path = Path.Combine(media.Dir, name);
                if (!File.Exists(path)) continue; // asset was removed since; export what we still have
                assets.Add(new CedarAsset(name, await File.ReadAllBytesAsync(path)));
            }

            using var ms = new MemoryStream();
            CedarPackage.Write(ms, draft.CedarJson, new CedarPackageMeta(draft.Title, draft.CreatedAt), assets);

            var fileName = SanitizeFileName(draft.Title) + ".cedar";
            return Results.File(ms.ToArray(), "application/zip", fileName);
        });

        // A standalone, self-contained HTML page — the article body via the same
        // CedarToBlogHtmlRenderer the live blog uses, plus a minimal title/date header and the
        // author's signature. Deliberately doesn't reuse BlogEndpoints.PageShell/ShellTemplate:
        // those wire up comment/reaction fetch() calls against a live slug and a theme-toggle
        // button, none of which make sense for a file opened locally with no server behind it.
        groupBuilder.MapGet("/{id:guid}/export-html", async (Guid id, string? lang, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var language = lang is not null && Languages.IsTranslationLanguage(lang) ? lang : Languages.Primary;
            // Idea #4 - the exported page is a reader-facing artefact, so it carries the article
            // title. A translation already has its own title and overwrites this below.
            var title = draft.ArticleTitle ?? draft.Title;
            var cedarJson = draft.CedarJson;
            if (language != Languages.Primary)
            {
                var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == language);
                if (translation is null)
                    return Results.BadRequest(new { error = $"No {language.ToUpperInvariant()} version of this draft" });
                title = translation.Title;
                cedarJson = translation.CedarJson;
            }

            var blogHost = cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;
            var body = CedarToBlogHtmlRenderer.Render(cedarJson, $"https://{blogHost}", language);
            var owner = await db.Users.Where(u => u.Id == uid)
                .Select(u => new { u.PostSignature, u.PostSignatureUrl, u.PostSignatureTranslationsJson, u.PlanTier, u.PlanExpiresAt })
                .FirstAsync();
            var ownerPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
            var localizedSignature = LocalizedTextMap.Pick(owner.PostSignature, owner.PostSignatureTranslationsJson, language);
            var signature = PlanLimitations.ResolveSignature(ownerPlan, localizedSignature, owner.PostSignatureUrl);
            var publishedAt = draft.BlogPublishedAt ?? draft.CreatedAt;

            var html = StaticExportHtml(title, body, language, signature, publishedAt, cedarJson);
            var fileName = SanitizeFileName(title) + ".html";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "text/html", fileName);
        });

        // FI2.10 — the whole post as a standalone website in one archive: a page per language
        // plus the media they reference, so it opens from disk with no server and no network.
        // The per-language .html download it replaces produced a page whose images all pointed
        // at blog.mooexe.dev, which is a saved page only for as long as the blog is up.
        groupBuilder.MapGet("/{id:guid}/export-zip", async (Guid id, ClaimsPrincipal user, CedarDbContext db, MediaPaths media, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var translations = await db.DraftTranslations.Where(t => t.DraftId == id).ToListAsync();
            var owner = await db.Users.Where(u => u.Id == uid)
                .Select(u => new { u.PostSignature, u.PostSignatureUrl, u.PostSignatureTranslationsJson, u.PlanTier, u.PlanExpiresAt })
                .FirstAsync();
            var ownerPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
            var publishedAt = draft.BlogPublishedAt ?? draft.CreatedAt;

            var versions = new List<(string Lang, string Title, string CedarJson)>
            {
                (Languages.Primary, draft.ArticleTitle ?? draft.Title, draft.CedarJson),
            };
            versions.AddRange(translations.Select(t => (t.Language, t.Title, t.CedarJson)));

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var written = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (lang, title, cedarJson) in versions)
                {
                    // "." rather than the blog host: ResolveUrl prefixes it onto the leading
                    // slash of /media/..., which turns every asset into ./media/... — relative
                    // to the page, which is exactly the layout inside the archive.
                    var body = CedarToBlogHtmlRenderer.Render(cedarJson, ".", lang);
                    // FI5 — each language's page in the archive gets that language's own signature,
                    // not the primary one on repeat (this loop used to resolve the signature once,
                    // outside the loop, before per-language signatures existed).
                    var localizedSignature = LocalizedTextMap.Pick(owner.PostSignature, owner.PostSignatureTranslationsJson, lang);
                    var signature = PlanLimitations.ResolveSignature(ownerPlan, localizedSignature, owner.PostSignatureUrl);
                    var html = StaticExportHtml(title, body, lang, signature, publishedAt, cedarJson);
                    var pageName = lang == Languages.Primary ? "index.html" : $"index.{lang}.html";
                    var pageEntry = zip.CreateEntry(pageName, CompressionLevel.Optimal);
                    await using (var pageStream = pageEntry.Open())
                        await pageStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(html));

                    foreach (var name in CedarPackage.FindReferencedMediaPaths(cedarJson))
                    {
                        if (!written.Add(name)) continue; // shared between language versions
                        var path = Path.Combine(media.Dir, name);
                        if (!File.Exists(path)) continue; // removed since; export what still exists
                        var assetEntry = zip.CreateEntry("media/" + name, CompressionLevel.Optimal);
                        await using var assetStream = assetEntry.Open();
                        await using var source = File.OpenRead(path);
                        await source.CopyToAsync(assetStream);
                    }
                }
            }

            return Results.File(ms.ToArray(), "application/zip", SanitizeFileName(draft.Title) + ".zip");
        });

        groupBuilder.MapPost("/import", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            if (file.Length == 0 || file.Length > CedarZipMaxBytes)
                return Results.BadRequest(new { error = $"File is too large ({CedarZipMaxBytes / (1024 * 1024)}MB maximum)" });

            CedarPackageContents pkg;
            await using (var stream = file.OpenReadStream())
            {
                try
                {
                    pkg = CedarPackage.Read(stream);
                }
                catch (CedarPackageException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }

            if (pkg.Assets.Count > CedarMaxAssetCount)
                return Results.BadRequest(new { error = $"Too many assets in package ({CedarMaxAssetCount} maximum)" });

            using (var docCheck = JsonDocument.Parse(pkg.DocumentJson))
            {
                var root = docCheck.RootElement;
                var looksLikeTiptapDoc = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "doc"
                    && root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array;
                if (!looksLikeTiptapDoc)
                    return Results.BadRequest(new { error = "Invalid document structure." });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);
            var incomingBytes = pkg.Assets.Sum(kv => (long)kv.Value.Length);
            if (!PlanLimitations.HasStorageRoom(tier, usedBytes, incomingBytes))
                return Results.Json(new { error = $"Storage limit of your plan ({PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)}MB) exceeded. Upgrade for more." }, statusCode: StatusCodes.Status403Forbidden);

            var pathRewrites = new Dictionary<string, string>();

            foreach (var (originalName, bytes) in pkg.Assets)
            {
                var contentType = ImageContentSniffer.DetectContentType(bytes);
                if (contentType is null || !ImportImageExtensions.TryGetValue(contentType, out var ext))
                    return Results.BadRequest(new { error = $"Unsupported or invalid asset: {originalName}" });
                if (bytes.Length > Consts.FileSizes.ImageMaxBytes)
                    return Results.BadRequest(new { error = $"Asset too large: {originalName}" });

                var newName = $"asset_{Guid.NewGuid()}{ext}";
                await File.WriteAllBytesAsync(Path.Combine(media.Dir, newName), bytes);

                db.Assets.Add(new Asset
                {
                    FileName = originalName,
                    ContentType = contentType,
                    SizeBytes = bytes.Length,
                    LocalPath = newName,
                    OwnerId = uid,
                });

                pathRewrites[originalName] = newName;
            }

            var rewrittenJson = CedarPackage.RewriteMediaPaths(pkg.DocumentJson, pathRewrites);
            var draft = new Draft { Title = pkg.Title, CedarJson = rewrittenJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();

            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        }).DisableAntiforgery();

        groupBuilder.MapPost("/import-markdown", async (IFormFile file, ClaimsPrincipal user, CedarDbContext db, MediaPaths media) =>
        {
            if (file.Length == 0 || file.Length > MarkdownZipMaxBytes)
                return Results.BadRequest(new { error = $"File is too large ({MarkdownZipMaxBytes / (1024 * 1024)}MB maximum)" });

            // ZipArchive needs a seekable stream; IFormFile's underlying stream may not be.
            using var uploadCopy = new MemoryStream();
            await using (var uploadStream = file.OpenReadStream())
                await uploadStream.CopyToAsync(uploadCopy);
            uploadCopy.Position = 0;

            ZipArchive archive;
            try
            {
                archive = new ZipArchive(uploadCopy, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "The file is not a valid .zip archive." });
            }

            using (archive)
            {
                var mdEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                if (mdEntry is null)
                    return Results.BadRequest(new { error = "No .md file found inside the zip." });

                string markdownText;
                using (var mdStream = mdEntry.Open())
                using (var reader = new StreamReader(mdStream))
                    markdownText = await reader.ReadToEndAsync();

                var imageEntries = archive.Entries
                    .Where(e => e != mdEntry
                        && !e.FullName.EndsWith('/')
                        && ImageFileExtensions.Contains(Path.GetExtension(e.FullName))
                        && !e.FullName.Contains("..")
                        && !Path.IsPathRooted(e.FullName))
                    .ToList();

                if (imageEntries.Count > MarkdownMaxImageCount)
                    return Results.BadRequest(new { error = $"Too many images in the zip ({MarkdownMaxImageCount} maximum)" });

                var docJson = MarkdownToCedarConverter.Convert(markdownText, out var titleFromHeading);
                var referencedNames = CedarPackage.FindReferencedMediaPaths(docJson);

                // Matched by basename only — Notion's exact subfolder layout isn't preserved. If the
                // same filename appears under more than one subfolder (rare, but possible in a large
                // multi-page export), the first match wins rather than throwing on a duplicate key.
                var byBasename = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                var byBasenameCi = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in imageEntries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    byBasename.TryAdd(name, entry);
                    byBasenameCi.TryAdd(name, entry);
                }

                var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
                var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);

                var unmatched = new List<string>();
                var pending = new List<(string OriginalName, byte[] Bytes, string ContentType, string Ext)>();
                long incomingBytes = 0;

                foreach (var refName in referencedNames)
                {
                    if (!byBasename.TryGetValue(refName, out var entry) && !byBasenameCi.TryGetValue(refName, out entry))
                    {
                        unmatched.Add(refName);
                        continue;
                    }

                    byte[] bytes;
                    using (var entryStream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        await entryStream.CopyToAsync(ms);
                        bytes = ms.ToArray();
                    }

                    var contentType = ImageContentSniffer.DetectContentType(bytes);
                    if (contentType is null || !ImportImageExtensions.TryGetValue(contentType, out var ext) || bytes.Length > Consts.FileSizes.ImageMaxBytes)
                    {
                        unmatched.Add(refName);
                        continue;
                    }

                    incomingBytes += bytes.Length;
                    pending.Add((refName, bytes, contentType, ext));
                }

                if (!PlanLimitations.HasStorageRoom(tier, usedBytes, incomingBytes))
                    return Results.Json(new { error = $"Storage limit of your plan ({PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)}MB) exceeded. Upgrade for more." }, statusCode: StatusCodes.Status403Forbidden);

                var pathRewrites = new Dictionary<string, string>();
                foreach (var (originalName, bytes, contentType, ext) in pending)
                {
                    var newName = $"asset_{Guid.NewGuid()}{ext}";
                    await File.WriteAllBytesAsync(Path.Combine(media.Dir, newName), bytes);

                    db.Assets.Add(new Asset
                    {
                        FileName = originalName,
                        ContentType = contentType,
                        SizeBytes = bytes.Length,
                        LocalPath = newName,
                        OwnerId = uid,
                    });

                    pathRewrites[originalName] = newName;
                }

                var rewrittenJson = CedarPackage.RewriteMediaPaths(docJson, pathRewrites);
                var title = titleFromHeading ?? Path.GetFileNameWithoutExtension(mdEntry.Name);
                var draft = new Draft { Title = title, CedarJson = rewrittenJson, OwnerId = uid };
                db.Drafts.Add(draft);
                await db.SaveChangesAsync();

                return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id, unmatchedImages = unmatched });
            }
        }).DisableAntiforgery()
          .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MarkdownZipMaxBytes });
    }

    private static string BuildInviteUrl(IConfiguration cfg, Draft draft, string token) =>
        $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}?invite={token}";

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "draft" : sanitized;
    }

    // Trimmed CSS subset of BlogEndpoints.ShellTemplate — just enough to render every block type
    // CedarToBlogHtmlRenderer can emit (headings, lists, tables, blockquote, code, collage,
    // carousel, spoiler, math, footnotes, TOC) plus a minimal title/date/signature header.
    // Duplicated rather than shared (docs/DESIGN.md already notes CSS is duplicated per-component
    // in this codebase, not centralized) because this needs to be fully self-contained in one
    // file with no external <link>/fetch of any kind.
    private static string StaticExportHtml(string title, string bodyHtml, string lang, ResolvedSignature? signature, DateTime publishedAt, string cedarJson)
    {
        var mathAssets = bodyHtml.Contains("math-tex")
            ? """<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css"><script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js" onload="document.querySelectorAll('.math-tex').forEach(function (el) { try { katex.render(el.textContent, el, { displayMode: el.dataset.display === 'true', throwOnError: false }); } catch (e) {} });"></script>"""
            : "";
        var signatureBlock = BlogEndpoints.SignatureHtml(signature, "div");
        // Same rule as BlogEndpoints.RenderPostAsync: skip the separate <h1> if the document's
        // own first block is already a heading, to avoid showing the title twice.
        var titleHeading = HeadingOutline.StartsWithHeading(cedarJson)
            ? ""
            : $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>";

        return $$"""
            <!doctype html>
            <html lang="{{lang}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
            <style>
            :root {
                color-scheme: light dark;
                --bg: #ECE9E2; --sheet: #FCFBF8; --alt: #EFECE4; --border: #DBD5C8;
                --text: #26231D; --t2: #6B655A; --t3: #9F988A; --accent: #5B6E46;
                --font-sans: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                --font-mono: ui-monospace, Menlo, Consolas, monospace;
                --asoft: color-mix(in srgb, var(--accent) 13%, var(--sheet));
                --abord: color-mix(in srgb, var(--accent) 38%, var(--border));
            }
            @media (prefers-color-scheme: dark) {
                :root {
                    --bg: #171511; --sheet: #211E18; --alt: #2F2C23; --border: #3C382D;
                    --text: #EAE6DB; --t2: #A69F8F; --t3: #776F5F;
                    --accent: color-mix(in srgb, #5B6E46 55%, #E8F0E8 45%);
                }
            }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text); font-family: var(--font-sans); line-height: 1.6; }
            a { color: var(--accent); }
            img, video { max-width: 100%; height: auto; }
            .page { max-width: 720px; margin: 0 auto; padding: 40px 20px 60px; }
            .post-sheet { background: var(--sheet); border-radius: 12px; box-shadow: 0 1px 3px rgba(40,35,25,.10); padding: 32px 40px 28px; }
            .post-sheet h1 { font-size: 27px; font-weight: 700; letter-spacing: -.015em; line-height: 1.22; margin: 0 0 6px; text-align: center; }
            .post-meta { font-size: 12px; color: var(--t3); text-align: center; margin: 0 0 22px; }
            .post-sheet h2 { font-size: 20px; font-weight: 600; letter-spacing: -.01em; margin: 24px 0 8px; }
            .post-sheet p { font-size: 16px; line-height: 1.65; margin: 0 0 14px; }
            .toc { background: var(--asoft); border: 1px solid var(--abord); border-radius: 10px; padding: 14px 18px; margin: 0 0 18px; }
            .toc-title { font-size: 11px; font-weight: 700; letter-spacing: .05em; text-transform: uppercase; color: var(--accent); margin: 0 0 8px; }
            .toc ul { list-style: none; margin: 0; padding: 0; font-size: 14px; line-height: 1.8; }
            .toc li a { color: var(--text); }
            .toc .toc-lvl-2 { padding-left: 14px; } .toc .toc-lvl-3 { padding-left: 28px; }
            .toc .toc-lvl-4 { padding-left: 42px; } .toc .toc-lvl-5 { padding-left: 56px; } .toc .toc-lvl-6 { padding-left: 70px; }
            .spoiler { background: var(--t3); color: transparent; border-radius: 4px; padding: 0 5px; cursor: pointer; }
            .spoiler:hover, .spoiler:focus { background: var(--alt); color: inherit; }
            .post-sheet code { font-family: var(--font-mono); font-size: .85em; background: var(--alt); border-radius: 4px; padding: 1px 6px; }
            .post-sheet pre { background: #22201A; color: #C9C08C; border-radius: 8px; padding: 12px 14px; overflow-x: auto; }
            .post-sheet pre code { background: none; padding: 0; font-size: 13.5px; line-height: 1.55; }
            .post-sheet blockquote { border-left: 3px solid var(--abord); padding: 2px 0 2px 14px; color: var(--t2); margin: 0 0 16px; }
            .post-sheet hr { border: none; border-top: 1px solid var(--border); margin: 24px 0; }
            .post-sheet ul, .post-sheet ol { font-size: 16px; line-height: 1.7; padding-left: 20px; margin: 0 0 16px; }
            .post-sheet figure { margin: 0 0 16px; }
            .post-sheet figcaption { text-align: center; font-size: 13px; color: var(--t2); margin-top: 6px; }
            .post-sheet table { width: 100%; border-collapse: collapse; font-size: 14.5px; margin: 0 0 16px; overflow-x: auto; display: block; }
            .post-sheet th, .post-sheet td { border: 1px solid var(--border); padding: 7px 11px; text-align: left; vertical-align: top; }
            .post-sheet th { background: var(--alt); font-weight: 600; }
            .math-tex { margin: 16px 0; overflow-x: auto; }
            div.math-tex { text-align: center; }
            .collage { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 6px; }
            .collage img { width: 100%; height: 160px; object-fit: cover; border-radius: 6px; }
            .carousel { position: relative; margin: 16px 0; }
            .carousel-viewport img { width: 100%; display: block; border-radius: 6px; }
            .carousel-prev, .carousel-next { position: absolute; top: 50%; transform: translateY(-50%); background: rgba(0,0,0,0.5); color: #fff; border: none; width: 32px; height: 32px; border-radius: 50%; cursor: pointer; font-size: 18px; }
            .carousel-prev { left: 8px; } .carousel-next { right: 8px; }
            .carousel-dots { display: flex; justify-content: center; gap: 6px; margin-top: 8px; }
            .carousel-dot { width: 8px; height: 8px; border-radius: 50%; border: none; background: rgba(128,128,128,0.4); cursor: pointer; padding: 0; }
            .carousel-dot.active { background: var(--accent); }
            .footnotes { font-size: 12.5px; color: var(--t2); border-top: 1px solid var(--border); padding: 10px 0 0; margin: 0 0 4px; }
            .footnotes sup, .post-sheet sup { color: var(--accent); font-weight: 600; }
            .post-signature { font-size: 13.5px; font-style: italic; color: var(--t2); white-space: pre-line; border-top: 1px solid var(--border); padding-top: 14px; margin-top: 18px; }
            .made-with { text-align: center; font-size: 11.5px; color: var(--t3); margin-top: 18px; }
            {{mathAssets}}
            </style>
            </head>
            <body>
            <div class="page">
            <div class="post-sheet">
            {{titleHeading}}
            <div class="post-meta">{{publishedAt.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}}</div>
            {{bodyHtml}}
            {{signatureBlock}}
            </div>
            <div class="made-with">Made with Cedar Clerk</div>
            </div>
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
            </script>
            </body>
            </html>
            """;
    }
}