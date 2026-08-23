using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Ai;
using CedarClerk.Server.Email;
using CedarClerk.Server.Translation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// ADR-058 — mirrors the MediaPaths pattern (AssetEndpoints.cs). Resolves to CEDAR_DATA_DIR/
// import-tmp, where a zip too large for Cloudflare's edge is scp'd for a local-only import.
public record ImportTmpPaths(string Dir);

public static class DraftEndpoints
{
    // ExpectedUpdatedAt/ConfirmShrink are the two save guards (T-018.1/T-018.3) and both are
    // optional: a client that sends neither behaves exactly as before, which keeps the Posts
    // manager's rename-PUT and the import paths working unchanged.
    public record SaveDraftRequest(string Title, string CedarJson, DateTime? ExpectedUpdatedAt = null, bool ConfirmShrink = false);
    public record SaveTranslationRequest(string Title, string CedarJson, DateTime? ExpectedUpdatedAt = null, bool ConfirmShrink = false);
    public record ChangePrimaryLanguageRequest(string Language);
    public record UpdateTagsRequest(string Tags);
    public record RenameTagRequest(string From, string To);
    public record UpdateFolderRequest(Guid? FolderId);
    public record UpdateSeriesRequest(Guid? SeriesId);
    public record UpdateParentRequest(Guid? ParentId, Guid? BeforeId = null);
    public record UpdatePrivateRequest(bool IsPrivate);
    public record UpdateTemplateRequest(bool IsTemplate);
    public record UpdateListedRequest(bool IsListedWhilePrivate);
    public record UpdateDisableCopyRequest(bool DisableCopy);
    public record UpdateWatermarkRequest(string? WatermarkText);
    public record UpdateSlugRequest(string? Slug);
    public record UpdateArticleTitleRequest(string? ArticleTitle);
    public record UpdateEngagementRequest(bool DisableReactions, bool DisableComments);
    public record AddInviteRequest(string Email);
    // FI4.1 — Language names which language slot the form belongs to; absent or primary writes
    // the post's own RegistrationFormJson, anything else goes into the translations object.
    public record UpdateRegistrationFormRequest(string? FormJson, string? Language = null);
    // ADR-058 — the local-only import-bypass request: a filename resolved only against
    // ImportTmpPaths.Dir (never an arbitrary path) and the email of the account to own the draft.
    public record LocalImportMarkdownRequest(string ZipFileName, string OwnerEmail);

    // ADR-128 — diff-sync the derived wiki-links against the primary document's current text.
    // Only ids the owner actually has become rows (a pasted foreign id links nothing), self-links
    // are ignored, and translations never run this: they inherit the primary's links.
    private static async Task SyncDocumentLinksAsync(CedarDbContext db, string uid, Guid fromId, string cedarJson)
    {
        var linked = WikiLinkRefs.Collect(cedarJson).Where(to => to != fromId).ToList();
        var owned = linked.Count == 0
            ? []
            : await db.Drafts.Where(d => d.OwnerId == uid && linked.Contains(d.Id)).Select(d => d.Id).ToListAsync();
        var existing = await db.DocumentLinks.Where(l => l.FromDraftId == fromId).ToListAsync();
        db.DocumentLinks.RemoveRange(existing.Where(l => !owned.Contains(l.ToDraftId)));
        foreach (var to in owned.Where(to => existing.All(l => l.ToDraftId != to)))
            db.DocumentLinks.Add(new DocumentLink { OwnerId = uid, FromDraftId = fromId, ToDraftId = to });
    }

    // T-018.1/T-018.3, both from the 29.07.2026 wipe: the two reasons a save is refused rather
    // than applied. Both answer 409 with a `code` the editor switches on — "stale" wants a reload,
    // "shrink" wants an explicit confirmation. Returns null when the save may proceed.
    private static IResult? SaveGuardFailure(DateTime storedUpdatedAt, string storedCedarJson, string incomingCedarJson,
        DateTime? expectedUpdatedAt, bool confirmShrink)
    {
        // Sub-millisecond slack: the timestamp round-trips through JSON and SQLite, and a real
        // conflicting save is seconds away, not ticks.
        if (expectedUpdatedAt is not null && Math.Abs((storedUpdatedAt - expectedUpdatedAt.Value).TotalMilliseconds) > 1)
            return Results.Json(new { error = ErrorMessages.SaveConflict, code = "stale", currentUpdatedAt = storedUpdatedAt },
                statusCode: StatusCodes.Status409Conflict);

        if (confirmShrink) return null;
        var verdict = ShrinkGuard.Inspect(storedCedarJson, incomingCedarJson);
        return verdict.Suspicious
            ? Results.Json(new
                {
                    error = ErrorMessages.SaveShrinkNeedsConfirmation, code = "shrink",
                    storedTextLength = verdict.StoredTextLength, incomingTextLength = verdict.IncomingTextLength,
                }, statusCode: StatusCodes.Status409Conflict)
            : null;
    }

    // T-015 — the splice: only the blocks the plan marked as changed reach the provider, and the
    // untouched ones are copied out of the existing translation, manual corrections and all.
    private static async Task<TranslationResult> TranslateIncrementallyAsync(ITranslationProvider provider,
        IncrementalTranslationPlan.Plan plan, string sourceTitle, string existingTitle, string existingTranslationJson,
        string lang, CancellationToken ct)
    {
        // Nothing moved. The stored translation is already the answer, and spending one of the
        // twenty daily AI calls to confirm that is not worth it. The consequence — a source whose
        // *title alone* changed keeps the old translated title — is in ADR-068.
        if (plan.BlocksToTranslate.Count == 0)
            return new TranslationResult(existingTitle, existingTranslationJson);

        // A real TipTap document holding just those blocks, so this goes through the ordinary
        // provider path — which replaces text in place and leaves structure alone (ADR-059), and
        // therefore hands back the same blocks in the same order.
        var partial = await provider.TranslateAsync(sourceTitle, IncrementalTranslationPlan.PartialDocument(plan), lang, ct);
        return new TranslationResult(partial.Title, IncrementalTranslationPlan.Assemble(plan, existingTranslationJson, partial.CedarJson));
    }

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

        // 8.6 — the growth series for one post: views, likes and comments over time, from the
        // nightly DraftStatSnapshot rows. Nothing recorded these before, so an old post has no
        // history to show; the client says so rather than drawing a flat line that would read as
        // "no growth" instead of "not measured".
        groupBuilder.MapGet("/{id:guid}/stat-history", async (Guid id, ClaimsPrincipal user, CedarDbContext db, int days = 30) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid))
                return Results.NotFound();

            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 7, 180));
            var snapshots = await db.DraftStatSnapshots
                .Where(s => s.DraftId == id && s.TakenAt >= since)
                .OrderBy(s => s.TakenAt)
                .Select(s => new { s.ViewCount, s.LikeCount, s.DislikeCount, s.CommentCount, s.TakenAt })
                .ToListAsync();

            return Results.Ok(new { snapshots });
        });
        
        groupBuilder.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var drafts = await db.Drafts.Where(d => d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new
                {
                    d.Id, d.Title, d.PrimaryLanguage, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                    d.IsArchived, d.LastTelegramMessageId, d.LastTelegramUsername, d.FolderId, d.SeriesId, d.IsPrivate, d.IsTemplate,
                    d.ParentDraftId, d.SiblingOrder,
                    d.DisableCopy, d.DisableReactions, d.DisableComments, d.ViewCount,
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
                d.Id, d.Title, d.PrimaryLanguage, d.CreatedAt, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished, d.BlogPublishedAt, d.Tags,
                d.IsArchived, d.LastTelegramMessageId, d.LastTelegramUsername, d.FolderId, d.SeriesId, d.IsPrivate, d.IsTemplate,
                d.ParentDraftId, d.SiblingOrder,
                d.DisableCopy, d.DisableReactions, d.DisableComments, d.ViewCount,
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
                draft.Id, draft.Title, draft.PrimaryLanguage, draft.CedarJson, draft.CreatedAt, draft.UpdatedAt, draft.BlogSlug,
                draft.IsBlogPublished, draft.BlogPublishedAt, draft.Tags, draft.FolderId, draft.IsPrivate,
                draft.WatermarkText, draft.ArticleTitle, draft.IsListedWhilePrivate, draft.DisableCopy,
                draft.DisableReactions, draft.DisableComments,
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

            // Every translation, not the first one found — a photo living only in the German
            // version is still embedded media.
            var names = CedarPackage.FindReferencedMediaPathsSafe(draft.CedarJson).ToList();
            var translationJsons = await db.DraftTranslations.Where(t => t.DraftId == id).Select(t => t.CedarJson).ToListAsync();
            foreach (var tj in translationJsons)
                names = names.Union(CedarPackage.FindReferencedMediaPathsSafe(tj)).ToList();

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
                return Results.BadRequest(new { error = ErrorMessages.BothTagsRequired });
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
            if (target.Length == 0) return Results.BadRequest(new { error = ErrorMessages.TagRequired });

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

        // ADR-125 — series membership. Attach assigns SeriesOrder = max+1 among the members;
        // detach nulls both halves of the pair so no stale order survives a reattach elsewhere.
        groupBuilder.MapPut("/{id:guid}/series", async (Guid id, UpdateSeriesRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            if (req.SeriesId is { } seriesId)
            {
                var ownsSeries = await db.Series.AnyAsync(s => s.Id == seriesId && s.OwnerId == uid);
                if (!ownsSeries) return Results.NotFound();

                if (draft.SeriesId != seriesId)
                {
                    var maxOrder = await db.Drafts.Where(d => d.SeriesId == seriesId)
                        .MaxAsync(d => (int?)d.SeriesOrder) ?? 0;
                    draft.SeriesId = seriesId;
                    draft.SeriesOrder = maxOrder + 1;
                }
            }
            else
            {
                draft.SeriesId = null;
                draft.SeriesOrder = null;
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { draft.SeriesId, draft.SeriesOrder });
        });

        // ADR-128 — place a document in the tree: under `parentId` (null = root), before
        // `beforeId` among its new siblings (absent = at the end). The whole target sibling set
        // is renumbered 0..n, so orders never drift apart. Reorder-within-parent is the same call.
        groupBuilder.MapPut("/{id:guid}/parent", async (Guid id, UpdateParentRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            if (req.ParentId is { } parentId)
            {
                if (parentId == id)
                    return Results.BadRequest(new { error = ErrorMessages.TreeWouldCycle });
                var parents = await db.Drafts.Where(d => d.OwnerId == uid)
                    .Select(d => new { d.Id, d.ParentDraftId })
                    .ToDictionaryAsync(d => d.Id, d => d.ParentDraftId);
                if (!parents.ContainsKey(parentId)) return Results.NotFound();

                // Walk the new parent's ancestor chain: meeting `id` there means the target is our
                // own descendant (a cycle); the chain's length is the new depth. Iteration is
                // capped so pre-existing bad data can never spin this forever.
                var depth = 1;
                for (Guid? cursor = parentId; cursor is { } c; cursor = parents.GetValueOrDefault(c))
                {
                    if (c == id)
                        return Results.BadRequest(new { error = ErrorMessages.TreeWouldCycle });
                    if (++depth > Consts.Documents.MaxTreeDepth)
                        return Results.BadRequest(new { error = ErrorMessages.TreeTooDeep });
                }
            }

            var siblings = await db.Drafts
                .Where(d => d.OwnerId == uid && d.ParentDraftId == req.ParentId && d.Id != id)
                .OrderBy(d => d.SiblingOrder).ThenByDescending(d => d.UpdatedAt)
                .ToListAsync();
            var insertAt = siblings.Count;
            if (req.BeforeId is { } beforeId)
            {
                var idx = siblings.FindIndex(d => d.Id == beforeId);
                if (idx >= 0) insertAt = idx;
            }
            draft.ParentDraftId = req.ParentId;
            siblings.Insert(insertAt, draft);
            for (var i = 0; i < siblings.Count; i++) siblings[i].SiblingOrder = i;

            await db.SaveChangesAsync();
            return Results.Ok(new { draft.ParentDraftId, draft.SiblingOrder });
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
                return Results.BadRequest(new { error = ErrorMessages.PublishToBlogFirst });

            // Run the author's text through the same slugifier the automatic path uses, so a URL
            // typed by hand can't be something the blog router won't match.
            var slug = SlugGenerator.Slugify(req.Slug ?? "");
            if (slug.Length == 0)
                return Results.BadRequest(new { error = ErrorMessages.SlugHasNoUsableCharacters });

            // Blog lookup is by slug across all owners, so uniqueness has to be global — not
            // per-owner like most things here.
            if (await db.Drafts.AnyAsync(d => d.Id != id && d.BlogSlug == slug))
                return Results.BadRequest(new { error = ErrorMessages.SlugTaken });

            draft.BlogSlug = slug;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.BlogSlug });
        });

        groupBuilder.MapPost("/{id:guid}/watermark", async (Guid id, UpdateWatermarkRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var text = req.WatermarkText?.Trim();
            if (text is { Length: > Consts.Watermark.MaxLength })
                return Results.BadRequest(new { error = ErrorMessages.WatermarkTooLong });

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

        // Copy protection on the blog page of a private post — same one-concern-per-endpoint
        // shape as /listed and /watermark above.
        groupBuilder.MapPost("/{id:guid}/disable-copy", async (Guid id, UpdateDisableCopyRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.DisableCopy = req.DisableCopy;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.DisableCopy });
        });

        // T-039 — one endpoint for both flags: they are set from the same row of checkboxes and
        // sending them together keeps "off, then off again" from being two round trips.
        groupBuilder.MapPost("/{id:guid}/engagement", async (Guid id, UpdateEngagementRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            draft.DisableReactions = req.DisableReactions;
            draft.DisableComments = req.DisableComments;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.DisableReactions, draft.DisableComments });
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
                return Results.BadRequest(new { error = ErrorMessages.RegistrationFormTooLarge });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            // ADR-060 — a v2 multi-language blob carries every language itself: it goes into the
            // primary column whole, and the legacy per-language translations column is cleared so
            // no stale v1 slot can shadow it later if the blob is ever downgraded.
            if (RegistrationFormSet.IsMultiLanguage(req.FormJson))
            {
                draft.RegistrationFormJson = req.FormJson;
                draft.RegistrationFormTranslationsJson = null;
            }
            else
            {
                var lang = req.Language is not null && Languages.IsContentLanguage(req.Language)
                    ? req.Language
                    : draft.PrimaryLanguage;
                if (lang == draft.PrimaryLanguage)
                    draft.RegistrationFormJson = string.IsNullOrWhiteSpace(req.FormJson) ? null : req.FormJson;
                else
                    draft.RegistrationFormTranslationsJson =
                        RegistrationFormSet.SetTranslation(draft.RegistrationFormTranslationsJson, lang, req.FormJson);
            }

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

        // The owner's own test submissions would otherwise sit in the distribution charts forever.
        // A hard delete, and deliberately so: the row IS that reader's grant (ADR-084's AccessToken
        // lives on it), so deleting a submission also closes the door it opened — which is what
        // removing a test account should mean.
        groupBuilder.MapDelete("/{id:guid}/registrations/{regId:guid}", async (Guid id, Guid regId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var row = await db.PostRegistrations.FirstOrDefaultAsync(r => r.Id == regId && r.DraftId == id);
            if (row is null) return Results.NotFound();

            db.PostRegistrations.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
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
                return Results.Json(new { error = ErrorMessages.InvalidEmail }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            if (draft.BlogSlug is null)
                return Results.Json(new { error = ErrorMessages.PublishToBlogFirst }, statusCode: StatusCodes.Status400BadRequest);

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
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            if (translation is null) return Results.NotFound();

            // ADR-065 — the snapshot only means anything against the language it was translated
            // from. After a primary-language change it can describe a document in a language this
            // translation never saw, and the editor's diff gutter would report every block changed.
            // Null SourceLanguage is a pre-ADR-064 row, where the source was the primary by
            // definition. Withholding it falls back to the plain staleness indicator.
            var comparable = translation.SourceLanguage is null || translation.SourceLanguage == draft.PrimaryLanguage;
            return Results.Ok(new
            {
                translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt,
                SourceSnapshotJson = comparable ? translation.SourceSnapshotJson : null,
            });
        });
        
        groupBuilder.MapPut("/{id:guid}/translations/{lang}", async (Guid id, string lang, SaveTranslationRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (!Languages.IsContentLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            // ADR-065 — a row in the draft's own primary language would be an invisible second copy
            // of the canonical document, and later collides with the primary-language swap's insert.
            if (lang == draft.PrimaryLanguage)
                return Results.BadRequest(new { error = ErrorMessages.LanguageIsPrimary(lang) });

            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
            // ADR-065 — a byte-identical save must not bump UpdatedAt: staleness is a timestamp
            // comparison, so bumping it here is what marked every other language dirty after a
            // save that changed nothing (the long-standing IB3).
            if (translation is not null && translation.Title == req.Title && translation.CedarJson == req.CedarJson)
                return Results.Ok(new { translation.Language, translation.UpdatedAt, translation.SourceSnapshotJson });

            if (translation is not null &&
                SaveGuardFailure(translation.UpdatedAt, translation.CedarJson, req.CedarJson, req.ExpectedUpdatedAt, req.ConfirmShrink) is { } refusal)
                return refusal;

            if (translation is null)
            {
                translation = new DraftTranslation { DraftId = id, Language = lang };
                db.DraftTranslations.Add(translation);
            }
            translation.Title = req.Title;
            translation.CedarJson = req.CedarJson;
            translation.UpdatedAt = DateTime.UtcNow;
            translation.SourceSnapshotJson = draft.CedarJson;
            translation.SourceLanguage = draft.PrimaryLanguage;
            await DraftRevisionService.RecordAsync(db, id, lang, req.Title, req.CedarJson);
            await db.SaveChangesAsync();
            return Results.Ok(new { translation.Language, translation.UpdatedAt, translation.SourceSnapshotJson });
        });
        
        // ADR-058-follow-up — only the fast, DB-only checks stay synchronous here (so bad-language/
        // not-found/quota errors still return immediately, unchanged). The slow part (the actual
        // Anthropic call + persisting the result) moves into a background job — see AiJobService's
        // own comment for why. Returns 202 + a job id instead of the translation itself; the
        // frontend polls GET /api/ai-jobs/{jobId}.
        groupBuilder.MapPost("/{id:guid}/translations/{lang}/auto", async (
            Guid id, string lang, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg,
            IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory, AiJobService jobs) =>
        {
            if (!Languages.IsContentLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported translation language: {lang}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            // ADR-065 — translating the primary into itself would burn a quota call to produce a
            // shadow copy of the canonical document.
            if (lang == draft.PrimaryLanguage)
                return Results.BadRequest(new { error = ErrorMessages.LanguageIsPrimary(lang) });

            // AI features are Pro Plus; each call counts against the per-day AI quota
            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = ErrorMessages.AutoTranslateProPlus }, statusCode: StatusCodes.Status403Forbidden);

            // T-013 — the provider is resolved and asked about the language BEFORE the quota is
            // charged: "DeepL has no Georgian" is a configuration fact, and charging a daily call
            // to discover it is the same mistake ADR-065 fixed for AI edit.
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
                return Results.Json(new { error = ErrorMessages.AutoTranslateNotConfigured }, statusCode: StatusCodes.Status501NotImplemented);
            if (!provider.SupportsTargetLanguage(lang))
                return Results.Json(new { error = ErrorMessages.LanguageNotSupportedByProvider(lang, provider.Name) },
                    statusCode: StatusCodes.Status501NotImplemented);

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = ErrorMessages.AiDailyLimitReached(PlanLimitations.AiDailyLimit) }, statusCode: StatusCodes.Status429TooManyRequests);

            var sourceTitle = draft.Title;
            var sourceCedarJson = draft.CedarJson;
            var sourcePrimaryLanguage = draft.PrimaryLanguage;
            var jobId = jobs.Start(uid, async ct =>
            {
                // T-015 — what the translation looks like *now* decides whether this is a full
                // translation or a splice of the changed blocks. Read in its own scope: the
                // provider call below can legitimately run for minutes.
                string? existingTranslationJson;
                var existingTitle = sourceTitle;
                string? snapshotJson;
                using (var readScope = scopeFactory.CreateScope())
                {
                    var readDb = readScope.ServiceProvider.GetRequiredService<CedarDbContext>();
                    var existing = await readDb.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
                    existingTranslationJson = existing?.CedarJson;
                    if (existing is not null) existingTitle = existing.Title;
                    // ADR-065 — a snapshot taken from a different language describes a document
                    // this translation never saw, so it cannot be diffed against.
                    snapshotJson = existing is not null && (existing.SourceLanguage is null || existing.SourceLanguage == sourcePrimaryLanguage)
                        ? existing.SourceSnapshotJson : null;
                }

                var plan = existingTranslationJson is null ? null
                    : IncrementalTranslationPlan.Build(snapshotJson, sourceCedarJson, existingTranslationJson);

                TranslationResult result;
                try
                {
                    result = plan is null
                        ? await provider.TranslateAsync(sourceTitle, sourceCedarJson, lang, ct)
                        : await TranslateIncrementallyAsync(provider, plan, sourceTitle, existingTitle, existingTranslationJson!, lang, ct);
                }
                catch (TranslationException ex)
                {
                    return AiJobOutcome.Fail(ex.Message, StatusCodes.Status502BadGateway);
                }
                catch (ArgumentException ex)
                {
                    // Assemble() refusing the provider's answer: the splice is the only thing that
                    // can produce this, and a wrong-shaped document must never be stored.
                    return AiJobOutcome.Fail(ex.Message, StatusCodes.Status502BadGateway);
                }

                try
                {
                    using var docCheck = JsonDocument.Parse(result.CedarJson);
                    var root = docCheck.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("type", out var typeProp) ||
                        typeProp.GetString() != "doc")
                    {
                        return AiJobOutcome.Fail("Translator returned an invalid document — try again", StatusCodes.Status502BadGateway);
                    }
                }
                catch (JsonException)
                {
                    return AiJobOutcome.Fail("Translator returned invalid JSON — try again", StatusCodes.Status502BadGateway);
                }

                // Fresh scope: the request's own `db` is disposed once this HTTP request returns,
                // long before this background work finishes.
                using var scope = scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
                var scopedDraft = await scopedDb.Drafts.FirstOrDefaultAsync(d => d.Id == id, ct);
                if (scopedDraft is null) return AiJobOutcome.Fail("Draft was deleted", StatusCodes.Status404NotFound);
                var translation = await scopedDb.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
                if (translation is null)
                {
                    translation = new DraftTranslation { DraftId = id, Language = lang };
                    scopedDb.DraftTranslations.Add(translation);
                }
                translation.Title = result.Title;
                translation.CedarJson = result.CedarJson;
                translation.UpdatedAt = DateTime.UtcNow;
                translation.SourceSnapshotJson = sourceCedarJson;
                translation.SourceLanguage = scopedDraft.PrimaryLanguage;
                await DraftRevisionService.RecordAsync(scopedDb, id, lang, result.Title, result.CedarJson, ct: ct);
                await scopedDb.SaveChangesAsync(ct);

                return AiJobOutcome.Ok(new { translation.Language, translation.Title, translation.CedarJson, translation.UpdatedAt, translation.SourceSnapshotJson });
            }, Consts.Anthropic.AutoTranslateTimeout);

            return Results.Accepted(value: new { jobId });
        });

        // ADR-058-follow-up — same shape as auto-translate above: fast checks stay synchronous,
        // the AI call + persist move into a background job, response is 202 + a job id.
        groupBuilder.MapPost("/{id:guid}/ai-edit/{lang}/{kind}", async (
            Guid id, string lang, string kind, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg,
            IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory, AiJobService jobs) =>
        {
            if (!Languages.IsContentLanguage(lang))
                return Results.BadRequest(new { error = $"Unsupported language: {lang}" });

            AiEditKind editKind;
            switch (kind)
            {
                case "fix-errors": editKind = AiEditKind.FixErrors; break;
                case "schizo": editKind = AiEditKind.Schizo; break;
                default: return Results.BadRequest(new { error = $"Unknown AI edit kind: {kind}" });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            // AI features are Pro Plus; each call counts against the per-day AI quota
            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = ErrorMessages.AiEditProPlus }, statusCode: StatusCodes.Status403Forbidden);

            // ADR-065 — which slot holds this language is a per-draft question; comparing against a
            // literal "ru" made every AI edit on a non-Russian-primary draft a guaranteed 404.
            var isTranslation = lang != draft.PrimaryLanguage;
            string sourceTitle, sourceCedarJson;
            if (!isTranslation)
            {
                sourceTitle = draft.Title;
                sourceCedarJson = draft.CedarJson;
            }
            else
            {
                var existingTranslation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
                if (existingTranslation is null) return Results.NotFound(new { error = $"No {lang} version to edit yet" });
                sourceTitle = existingTranslation.Title;
                sourceCedarJson = existingTranslation.CedarJson;
            }

            // Charged only once there is something to edit — the quota used to be spent before the
            // 404 above, so a doomed request still cost the user one of the day's AI calls.
            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = ErrorMessages.AiDailyLimitReached(PlanLimitations.AiDailyLimit) }, statusCode: StatusCodes.Status429TooManyRequests);

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
                return Results.Json(new { error = ErrorMessages.AiEditNotConfigured }, statusCode: StatusCodes.Status501NotImplemented);

            var jobId = jobs.Start(uid, async ct =>
            {
                AiEditResult result;
                try
                {
                    result = await provider.EditAsync(sourceTitle, sourceCedarJson, editKind, ct);
                }
                catch (AiEditException ex)
                {
                    return AiJobOutcome.Fail(ex.Message, StatusCodes.Status502BadGateway);
                }

                try
                {
                    using var docCheck = JsonDocument.Parse(result.CedarJson);
                    var root = docCheck.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("type", out var typeProp) ||
                        typeProp.GetString() != "doc")
                    {
                        return AiJobOutcome.Fail("AI returned an invalid document — try again", StatusCodes.Status502BadGateway);
                    }
                }
                catch (JsonException)
                {
                    return AiJobOutcome.Fail("AI returned invalid JSON — try again", StatusCodes.Status502BadGateway);
                }

                using var scope = scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
                if (!isTranslation)
                {
                    var scopedDraft = await scopedDb.Drafts.FirstOrDefaultAsync(d => d.Id == id, ct);
                    if (scopedDraft is null) return AiJobOutcome.Fail("Draft was deleted", StatusCodes.Status404NotFound);
                    scopedDraft.Title = result.Title;
                    scopedDraft.CedarJson = result.CedarJson;
                    scopedDraft.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var scopedTranslation = await scopedDb.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang, ct);
                    if (scopedTranslation is null) return AiJobOutcome.Fail("Translation was deleted", StatusCodes.Status404NotFound);
                    scopedTranslation.Title = result.Title;
                    scopedTranslation.CedarJson = result.CedarJson;
                    scopedTranslation.UpdatedAt = DateTime.UtcNow;
                }
                // An AI edit rewrites the whole document without the author reading it first, which
                // is exactly the kind of change worth being able to look back at.
                await DraftRevisionService.RecordAsync(scopedDb, id, lang, result.Title, result.CedarJson, ct: ct);
                await scopedDb.SaveChangesAsync(ct);

                return AiJobOutcome.Ok(new { title = result.Title, cedarJson = result.CedarJson, updatedAt = DateTime.UtcNow });
            }, Consts.Anthropic.RequestTimeout);

            return Results.Accepted(value: new { jobId });
        });

        groupBuilder.MapDelete("/{id:guid}/translations/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var deleted = await db.DraftTranslations
                .Where(t => t.DraftId == id && t.Language == lang)
                .ExecuteDeleteAsync();
            // ADR-065 — a deleted language must not leave full copies of its text behind in the
            // revision history (and in every nightly backup generation of it).
            if (deleted > 0)
            {
                await db.DraftGlossaryExclusions
                    .Where(x => x.DraftId == id && x.OwnerId == uid && x.Language == lang).ExecuteDeleteAsync();
                await db.DraftRevisions.Where(r => r.DraftId == id && r.Language == lang).ExecuteDeleteAsync();
            }
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
        
        groupBuilder.MapPost("/", async (SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = new Draft { Title = req.Title, CedarJson = req.CedarJson, OwnerId = uid };
            db.Drafts.Add(draft);
            await DraftRevisionService.RecordAsync(db, draft.Id, draft.PrimaryLanguage, req.Title, req.CedarJson);
            await db.SaveChangesAsync();
            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id });
        });
        
        groupBuilder.MapPut("/{id:guid}", async (Guid id, SaveDraftRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            // ADR-065 — a save that changes nothing changes nothing: the autosave fires on every
            // pause in typing, including after a typed-then-undone edit or a rename that PUTs the
            // unchanged body back, and bumping UpdatedAt there is what marked every translation
            // stale without the primary text having moved (the long-standing IB3).
            if (draft.Title == req.Title && draft.CedarJson == req.CedarJson)
                return Results.Ok(new { draft.Id, draft.UpdatedAt });

            if (SaveGuardFailure(draft.UpdatedAt, draft.CedarJson, req.CedarJson, req.ExpectedUpdatedAt, req.ConfirmShrink) is { } refusal)
                return refusal;

            draft.Title = req.Title;
            draft.CedarJson = req.CedarJson;
            draft.UpdatedAt = DateTime.UtcNow;
            await DraftRevisionService.RecordAsync(db, id, draft.PrimaryLanguage, req.Title, req.CedarJson);
            await SyncDocumentLinksAsync(db, uid, id, req.CedarJson);
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.UpdatedAt });
        });

        // ADR-128 — who links here. Backlinks are DocumentLink rows walked from the target side.
        groupBuilder.MapGet("/{id:guid}/backlinks", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid)) return Results.NotFound();
            var fromIds = await db.DocumentLinks.Where(l => l.ToDraftId == id && l.OwnerId == uid)
                .Select(l => l.FromDraftId).ToListAsync();
            var titles = await db.Drafts.Where(d => fromIds.Contains(d.Id))
                .OrderBy(d => d.Title)
                .Select(d => new { d.Id, d.Title }).ToListAsync();
            return Results.Ok(titles);
        });

        groupBuilder.MapPost("/{id:guid}/primary-language", async (Guid id, ChangePrimaryLanguageRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (!Languages.IsContentLanguage(req.Language))
                return Results.BadRequest(new { error = $"Unsupported language: {req.Language}" });
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            if (req.Language == draft.PrimaryLanguage) return Results.Ok(new { draft.PrimaryLanguage });

            var translations = await db.DraftTranslations.Where(t => t.DraftId == id).ToListAsync();
            var selected = translations.FirstOrDefault(t => t.Language == req.Language);
            if (selected is null) return Results.BadRequest(new { error = ErrorMessages.CreateLanguageBeforePrimary });

            var demotedLanguage = draft.PrimaryLanguage;
            // A row already keyed to the *current* primary language can only be a shadow left by an
            // older build that allowed it; adding the demoted row on top of it would violate the
            // unique (DraftId, Language) index and 500. The canonical document wins.
            if (translations.FirstOrDefault(t => t.Language == demotedLanguage) is { } shadow)
                db.DraftTranslations.Remove(shadow);

            db.DraftTranslations.Add(new DraftTranslation
            {
                DraftId = id, Language = demotedLanguage, Title = draft.Title, CedarJson = draft.CedarJson,
                UpdatedAt = draft.UpdatedAt,
                // The two were in sync a moment ago by construction — this is a relabeling, not an
                // edit — so the demoted language is up to date with its new source as of now.
                SourceSnapshotJson = selected.CedarJson, SourceLanguage = selected.Language,
            });

            draft.Title = selected.Title;
            draft.CedarJson = selected.CedarJson;
            draft.PrimaryLanguage = selected.Language;
            // Deliberately the promoted version's own timestamp rather than "now": staleness is a
            // timestamp comparison, and stamping now would flip every other language to stale for
            // a change that touched no text. Carrying it over preserves every relative recency.
            draft.UpdatedAt = selected.UpdatedAt;
            db.DraftTranslations.Remove(selected);

            // Every remaining translation was translated from a document in the *old* primary
            // language. Against the new primary that snapshot is not a stale baseline, it is a
            // meaningless one — it would render a full-document diff in the gutter. Dropping it
            // says "provenance unknown" honestly and falls back to the timestamp indicator.
            foreach (var other in translations.Where(t => t.Language != req.Language && t.Language != demotedLanguage))
            {
                other.SourceSnapshotJson = null;
                other.SourceLanguage = null;
            }

            await DraftRevisionService.RecordAsync(db, id, draft.PrimaryLanguage, draft.Title, draft.CedarJson);
            // The primary document just changed wholesale — its derived links change with it.
            await SyncDocumentLinksAsync(db, uid, id, draft.CedarJson);
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.PrimaryLanguage, draft.UpdatedAt });
        });

        groupBuilder.MapGet("/{id:guid}/revisions/{lang}", async (Guid id, string lang, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == uid);
            if (!owns) return Results.NotFound();
            var rows = await db.DraftRevisions.Where(r => r.DraftId == id && r.Language == lang)
                .OrderByDescending(r => r.CreatedAt).Take(30).ToListAsync();
            return Results.Ok(rows.Select(r => new { r.Id, r.Kind, r.Destination, r.CreatedAt, r.Title,
                fingerprint = DraftRevisionService.Fingerprint(r.Title, r.CedarJson),
                lines = DraftRevisionService.BlockCount(r.CedarJson) }));
        });

        // T-016 — the stored content of one version, plus what it would change if restored. Until
        // now the history was readable but not reachable: recovering a version meant sqlite3 on
        // the server, which is not a recovery story for anyone but Marty.
        groupBuilder.MapGet("/{id:guid}/revisions/{lang}/{revisionId:guid}", async (
            Guid id, string lang, Guid revisionId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var revision = await db.DraftRevisions.FirstOrDefaultAsync(r => r.Id == revisionId && r.DraftId == id && r.Language == lang);
            if (revision is null) return Results.NotFound();

            var current = await DraftRevisionService.ResolveAsync(db, draft, lang);
            return Results.Ok(new
            {
                revision.Id, revision.Kind, revision.Destination, revision.CreatedAt, revision.Title, revision.CedarJson,
                diffToCurrent = current is null ? null : DraftRevisionService.Diff(revision.CedarJson, current.Value.CedarJson),
                isCurrent = current is not null && current.Value.Title == revision.Title && current.Value.CedarJson == revision.CedarJson,
            });
        });

        // T-017 — any two points in the history, or one against what is in the editor now
        // ("current"). One endpoint rather than a compare-to-current special case, since the
        // client already has to pick two ends either way.
        groupBuilder.MapGet("/{id:guid}/revisions/{lang}/diff", async (
            Guid id, string lang, string from, string to, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var before = await SideAsync(from);
            var after = await SideAsync(to);
            if (before is null || after is null) return Results.NotFound();
            return Results.Ok(new { diff = DraftRevisionService.Diff(before, after) });

            async Task<string?> SideAsync(string side) => side == "current"
                ? (await DraftRevisionService.ResolveAsync(db, draft, lang))?.CedarJson
                : Guid.TryParse(side, out var revId)
                    ? (await db.DraftRevisions.FirstOrDefaultAsync(r => r.Id == revId && r.DraftId == id && r.Language == lang))?.CedarJson
                    : null;
        });

        // T-016 — restoring is an ordinary edit, not a rewind: the version being replaced is
        // recorded first, so a restore is itself undoable through the same history.
        groupBuilder.MapPost("/{id:guid}/revisions/{revisionId:guid}/restore", async (
            Guid id, Guid revisionId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var revision = await db.DraftRevisions.FirstOrDefaultAsync(r => r.Id == revisionId && r.DraftId == id);
            if (revision is null) return Results.NotFound();
            var lang = revision.Language;

            var restoredAt = DateTime.UtcNow;
            if (lang == draft.PrimaryLanguage)
            {
                await DraftRevisionService.RecordAsync(db, id, lang, draft.Title, draft.CedarJson);
                draft.Title = revision.Title;
                draft.CedarJson = revision.CedarJson;
                draft.UpdatedAt = restoredAt;
            }
            else
            {
                var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
                if (translation is null) return Results.BadRequest(new { error = ErrorMessages.NoVersionInLanguage(lang) });
                await DraftRevisionService.RecordAsync(db, id, lang, translation.Title, translation.CedarJson);
                translation.Title = revision.Title;
                translation.CedarJson = revision.CedarJson;
                translation.UpdatedAt = restoredAt;
            }

            await DraftRevisionService.RecordAsync(db, id, lang, revision.Title, revision.CedarJson, DraftRevisionService.Kinds.Restore);
            await db.SaveChangesAsync();
            return Results.Ok(new { language = lang, title = revision.Title, cedarJson = revision.CedarJson, updatedAt = restoredAt });
        });
        
        groupBuilder.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            // ADR-103 — a project keeps at least one document. The rule is enforced here as well as
            // on the module's own detach path, because a draft can be deleted from the ordinary
            // drafts list, and an invariant only one of the two doors honours is not an invariant.
            // Costs one indexed lookup; for the overwhelmingly common unfiled draft it stops there.
            var info = await db.Drafts.Where(x => x.Id == id && x.OwnerId == uid)
                .Select(x => new { x.ProjectId, x.ParentDraftId }).FirstOrDefaultAsync();
            if (await Modules.IndieDev.ProjectEndpoints.IsLastDocumentOfProjectAsync(db, id, info?.ProjectId, uid))
                return Results.Json(new { error = ErrorMessages.ProjectNeedsOneDocument }, statusCode: StatusCodes.Status409Conflict);

            var deleted = await db.Drafts
                .Where(x => x.Id == id && x.OwnerId == uid)
                .ExecuteDeleteAsync();
            if (deleted > 0)
            {
                await db.DraftGlossaryExclusions
                    .Where(x => x.DraftId == id && x.OwnerId == uid).ExecuteDeleteAsync();
                // ADR-128 — children move up to the grandparent: the subtree survives its root.
                await db.Drafts.Where(x => x.OwnerId == uid && x.ParentDraftId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ParentDraftId, info!.ParentDraftId));
                // ADR-128 — derived links die with either endpoint; a surviving row would render
                // a backlink to a document that is gone.
                await db.DocumentLinks.Where(l => l.FromDraftId == id || l.ToDraftId == id).ExecuteDeleteAsync();
                await db.DraftStatSeens.Where(x => x.DraftId == id && x.OwnerId == uid).ExecuteDeleteAsync();
                // ADR-065 — revisions hold complete copies of the document, and unlike
                // DraftTranslation (which has a real navigation property, so EF cascades it)
                // DraftRevision is keyed by a bare Guid. Without this a deleted private post lives
                // on in the database and in every backup generation of it.
                await db.DraftRevisions.Where(r => r.DraftId == id).ExecuteDeleteAsync();
                // T-141/T-123 — the links a document was on either side of. Same reasoning as the
                // revisions above, one level out: EntityLink holds bare Guids, so nothing cascades,
                // and a surviving row renders as a chip pointing at a document that is gone.
                await Modules.IndieDev.ProjectLinks.RemoveAllForAsync(db, uid, CedarClerk.Core.LinkTargets.Document, id);
            }
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

            var language = lang is not null && Languages.IsContentLanguage(lang) ? lang : draft.PrimaryLanguage;
            // Idea #4 - the exported page is a reader-facing artefact, so it carries the article
            // title. A translation already has its own title and overwrites this below.
            var title = draft.ArticleTitle ?? draft.Title;
            var cedarJson = draft.CedarJson;
            if (language != draft.PrimaryLanguage)
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
                (draft.PrimaryLanguage, draft.ArticleTitle ?? draft.Title, draft.CedarJson),
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
                    var pageName = lang == draft.PrimaryLanguage ? "index.html" : $"index.{lang}.html";
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
                    return Results.BadRequest(new { error = ErrorMessages.InvalidDocumentStructure });
            }

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            var usedBytes = await db.Assets.Where(a => a.OwnerId == uid).SumAsync(a => a.SizeBytes);
            var incomingBytes = pkg.Assets.Sum(kv => (long)kv.Value.Length);
            if (!PlanLimitations.HasStorageRoom(tier, usedBytes, incomingBytes))
                return Results.Json(new { error = $"Storage limit of your plan ({PlanLimitations.StorageLimitBytes(tier) / (1024 * 1024)}MB) exceeded. Upgrade for more." }, statusCode: StatusCodes.Status403Forbidden);

            var pathRewrites = new Dictionary<string, string>();

            foreach (var (originalName, rawBytes) in pkg.Assets)
            {
                var contentType = ImageContentSniffer.DetectContentType(rawBytes);
                if (contentType is null || !ImportImageExtensions.TryGetValue(contentType, out var ext))
                    return Results.BadRequest(new { error = $"Unsupported or invalid asset: {originalName}" });
                if (rawBytes.Length > Consts.FileSizes.ImageMaxBytes)
                    return Results.BadRequest(new { error = $"Asset too large: {originalName}" });

                var bytes = ImageMetadataStripper.Strip(rawBytes, contentType);
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

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await ImportMarkdownZipAsync(uploadCopy, uid, db, media);
        }).DisableAntiforgery()
          .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MarkdownZipMaxBytes });

        // ADR-058 — local-only bypass for imports over Cloudflare's ~100MB edge limit, triggered
        // over SSH directly on the server (never through the tunnel, so that limit never applies).
        // AllowAnonymous() opts out of the group's RequireAuthorization(); IsGenuinelyLocal() below
        // is the real gate — see its own comment for why loopback IP alone isn't enough here.
        groupBuilder.MapPost("/import-markdown-local", async (
            LocalImportMarkdownRequest req, HttpContext ctx,
            UserManager<ApplicationUser> users, CedarDbContext db, MediaPaths media, ImportTmpPaths importTmp) =>
        {
            if (!IsGenuinelyLocal(ctx))
                return Results.NotFound(); // 404, not 403 — same instinct as AdminEndpoints' admin gate

            if (!TryResolveImportTmpFile(importTmp.Dir, req.ZipFileName, out var fullPath))
                return Results.BadRequest(new { error = ErrorMessages.InvalidFileName });

            if (!File.Exists(fullPath))
                return Results.BadRequest(new { error = ErrorMessages.ImportFileNotFound });

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length == 0 || fileInfo.Length > MarkdownZipMaxBytes)
                return Results.BadRequest(new { error = $"File is too large ({MarkdownZipMaxBytes / (1024 * 1024)}MB maximum)" });

            var owner = await users.FindByEmailAsync(req.OwnerEmail);
            if (owner is null)
                return Results.BadRequest(new { error = ErrorMessages.NoAccountWithEmail });

            await using var zipStream = File.OpenRead(fullPath); // FileStream is already seekable
            return await ImportMarkdownZipAsync(zipStream, owner.Id, db, media);
        }).AllowAnonymous();
    }

    // ADR-058 — extracted verbatim from /import-markdown's handler body so both it and the
    // local-only bypass share one implementation. Every check/message/order is unchanged from
    // before the extraction — this is a pure move, not a rewrite.
    private static async Task<IResult> ImportMarkdownZipAsync(Stream seekableZipStream, string ownerId, CedarDbContext db, MediaPaths media)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(seekableZipStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = ErrorMessages.NotAZipArchive });
        }

        using (archive)
        {
            var mdEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            if (mdEntry is null)
                return Results.BadRequest(new { error = ErrorMessages.NoMarkdownInZip });

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

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, ownerId);
            var usedBytes = await db.Assets.Where(a => a.OwnerId == ownerId).SumAsync(a => a.SizeBytes);

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

                bytes = ImageMetadataStripper.Strip(bytes, contentType);
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
                    OwnerId = ownerId,
                });

                pathRewrites[originalName] = newName;
            }

            var rewrittenJson = CedarPackage.RewriteMediaPaths(docJson, pathRewrites);
            var title = titleFromHeading ?? Path.GetFileNameWithoutExtension(mdEntry.Name);
            var draft = new Draft { Title = title, CedarJson = rewrittenJson, OwnerId = ownerId };
            db.Drafts.Add(draft);
            await db.SaveChangesAsync();

            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id, unmatchedImages = unmatched });
        }
    }

    // ADR-058 — the real gate for /import-markdown-local. Loopback IP alone is NOT enough:
    // Kestrel binds only to localhost:8080, and Cloudflare Tunnel reaches the app by connecting
    // to that same address — so every tunneled request also arrives here from a loopback IP,
    // indistinguishable by IP alone from a request made directly on the box. The Host header is
    // the second, load-bearing signal: the tunnel forwards the client's original Host
    // (cedarclerk.mooexe.dev), never rewriting it to localhost — already relied on elsewhere in
    // this file for the blog's host-based routing. Only a request that both connects over
    // loopback AND was addressed to "localhost" (e.g. `curl http://localhost:8080/...` run
    // directly on the server) satisfies both. Deliberately not using CF-Connecting-IP/X-Forwarded-For
    // — ordinary, attacker-settable headers this app never validates against a trusted-proxy list.
    private static bool IsGenuinelyLocal(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
        && string.Equals(ctx.Request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    // Same "reject .. / rooted path" instinct already used for zip entries above, applied to the
    // request's file name instead — the resolved path must stay inside importTmpDir.
    private static bool TryResolveImportTmpFile(string importTmpDir, string requestedName, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(requestedName) || requestedName.Contains("..") || Path.IsPathRooted(requestedName))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(importTmpDir, requestedName));
        var normalizedDir = Path.GetFullPath(importTmpDir) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedDir, StringComparison.Ordinal))
            return false;

        fullPath = candidate;
        return true;
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
    // Duplicated rather than shared (docs/design/DESIGN.md already notes CSS is duplicated per-component
    // in this codebase, not centralized) because this needs to be fully self-contained in one
    // file with no external <link>/fetch of any kind. The palette itself is not duplicated:
    // it is inlined from DesignTokens (T-101), so the export stays self-contained without
    // carrying its own copy of the colours.
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
            :root { color-scheme: light dark; {{DesignTokens.Declarations(DesignTokens.Light)}} }
            @media (prefers-color-scheme: dark) {
                :root { {{DesignTokens.Declarations(DesignTokens.Dark)}} }
            }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--surface); color: var(--text); font-family: var(--font-sans); line-height: 1.6; }
            a { color: var(--accent); }
            img, video { max-width: 100%; height: auto; }
            .page { max-width: 720px; margin: 0 auto; padding: 40px 20px 60px; }
            .post-sheet { background: var(--sheet); border-radius: 12px; box-shadow: 0 1px 3px rgba(40,35,25,.10); padding: 32px 40px 28px; }
            .post-sheet h1 { font-size: 27px; font-weight: 700; letter-spacing: -.015em; line-height: 1.22; margin: 0 0 6px; text-align: center; }
            .post-meta { font-size: 12px; color: var(--t2); text-align: center; margin: 0 0 22px; }
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
            .made-with { text-align: center; font-size: 11.5px; color: var(--t2); margin-top: 18px; }
            {{mathAssets}}
            </style>
            </head>
            <body>
            <div class="page">
            <div class="post-sheet">
            {{titleHeading}}
            <div class="post-meta">{{DisplayTime.ToZone(publishedAt).ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}}</div>
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
