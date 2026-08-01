using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Publishing;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server;

public static class PostEndpoints
{ 
    // Language is nullable rather than defaulting to a literal: which language a draft "is" became
    // a per-draft property in ADR-064, so the default has to be resolved against the draft itself.
    public record ExportRequest(Guid DraftId, string ChatId, string Format = Consts.ContentTypes.Markdown, string? Language = null, string CompressionLevel = "standard", string? ConfirmedFingerprint = null);
    public record UpdatePreviewRequest(Guid DraftId, string Kind, string? ChatId = null, string? Language = null);

    // "small"/"standard"/"high" — see the export modal's compression-level control and the ADR
    // following ADR-031 in docs/DECISIONS.md. Unknown/missing values fall back to "standard".
    public static long ResolveCompressionTargetBytes(string? level) => level switch
    {
        "small" => Consts.FileSizes.TelegramCompressSmallBytes,
        "high" => Consts.FileSizes.TelegramCompressHighBytes,
        _ => Consts.FileSizes.TelegramSafeImageBytes,
    };

    // Telegram auto-links a plain "#word" in message text client-side — no special API/entity
    // needed, RichRunText is plain text, not HTML, so no escaping either. Spaces are stripped
    // from within a tag since a hashtag can't contain them (e.g. "my tag" -> "#mytag"). See the
    // ADR following ADR-035, docs/DECISIONS.md, for the Phase 8 Step 6 decision.
    public static string? BuildHashtagLine(string tags)
    {
        var list = BlogEndpoints.SplitTags(tags);
        if (list.Count == 0)
            return null;

        return string.Join(" ", list.Select(t => "#" + t.Replace(" ", "")));
    }

    public record PublishResult(int? MessageId, string? Error, int StatusCode = StatusCodes.Status400BadRequest)
    {
        public bool Success => Error is null;
    }

    /// <summary>
    /// The network-agnostic half of publishing (T-085, ADR-078): resolve the draft and the language,
    /// resolve which of the owner's targets is meant, hand the document to that network's
    /// <see cref="IPublishTarget"/>, and record the outcome on the target row. Everything Telegram
    /// knows about — the bot, media compression, the Blocks renderer, the wire mapping, the
    /// ChannelPost row — moved into <see cref="TelegramPublishTarget"/> and nothing about it is
    /// visible from here.
    ///
    /// The signature keeps `chatId` rather than a target id: the client's contract is unchanged by
    /// this refactor, and the resolution from a chat id to a target row happens below.
    /// </summary>
    public static async Task<PublishResult> PublishAsync(
        Guid draftId,
        string chatId,
        string ownerId,
        CedarDbContext db,
        IEnumerable<IPublishTarget> targets,
        string format = Consts.ContentTypes.Markdown,
        string? language = null,
        ILogger? logger = null,
        string compressionLevel = "standard",
        CancellationToken ct = default)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == ownerId, ct);
        if (draft is null)
            return new PublishResult(null, ErrorMessages.DraftNotFound, StatusCodes.Status404NotFound);

        language ??= draft.PrimaryLanguage;

        var targetChannel = await SubscriptionPlan.ResolveOwnedChannelAsync(db, ownerId, chatId);
        if (targetChannel is null)
            return new PublishResult(null, "You can only publish to your connected channels — connect this channel first (Channels popup)", StatusCodes.Status403Forbidden);

        // The channel is the permission check (unchanged); the target row is what publishing runs
        // against. Ensure rather than look up, so a channel connected before this table existed —
        // or one whose backfill has not run — publishes instead of failing on a missing row.
        var target = await TelegramTargetProjection.EnsureAsync(db, targetChannel, ct);

        var document = await DraftRevisionService.ResolveAsync(db, draft, language, ct);
        if (document is null)
            return new PublishResult(null, ErrorMessages.NoVersionInLanguage(language), StatusCodes.Status404NotFound);
        var (title, cedarJson) = document.Value;

        var implementation = targets.FirstOrDefault(t => t.Network == target.Network);
        if (implementation is null)
            return new PublishResult(null, $"No publisher is configured for {target.Network}", StatusCodes.Status501NotImplemented);

        var outcome = await implementation.PublishAsync(new PublishRequest
        {
            DraftId = draftId,
            OwnerId = ownerId,
            Language = language,
            Title = title,
            CedarJson = cedarJson,
            Target = target,
            CompressionLevel = compressionLevel,
        }, ct);

        // Recorded on the target either way: a connection that is failing should be visible before
        // the next send rather than after it.
        target.LastError = outcome.Error;
        if (outcome.Success)
            target.LastPublishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (!outcome.Success)
            return new PublishResult(null, outcome.Error, outcome.StatusCode);

        // int, because every existing caller (the export endpoint's response and ScheduledPost
        // .MessageId) is Telegram-shaped and generalising them is T-090's job, not this refactor's.
        return new PublishResult(int.TryParse(outcome.Receipt!.RemoteId, out var id) ? id : null, null);
    }

    public static void MapPostEndpoints(this WebApplication app)
    {
        // ADR-065 — what an update would overwrite, for one language and one destination. The client
        // asks once per language it is about to publish; "has this been published here before" is
        // answered from the revision log rather than from whatever the client thinks it knows,
        // because the client's own signal (a public post URL) is absent for channels with no
        // @username — an ordinary private-channel setup that silently skipped the guard entirely.
        app.MapPost("/api/posts/update-preview", async (UpdatePreviewRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            // The blog has exactly one destination per draft, so its revisions carry no destination
            // at all — keying them to the slug would silently lose the baseline the moment the
            // owner edits the post's URL (FI3.4), which is precisely when a diff matters.
            var (kind, destination) = req.Kind == DraftRevisionService.Kinds.Blog
                ? (DraftRevisionService.Kinds.Blog, (string?)null)
                : (DraftRevisionService.Kinds.Telegram, req.ChatId);

            var preview = await DraftRevisionService.PreviewAsync(db, draft, req.Language ?? draft.PrimaryLanguage, kind, destination);
            return preview is null ? Results.NotFound() : Results.Ok(preview);
        }).RequireAuthorization();

        app.MapPost("/api/posts/export", async (ExportRequest req, ClaimsPrincipal user, CedarDbContext db, IEnumerable<IPublishTarget> targets, ILogger<Program> logger) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            // ADR-065 — re-sending over a live post requires naming the version that was previewed.
            // Deliberately here rather than inside PublishAsync: the scheduled-post job publishes
            // without a browser, and its confirmation happened when the send was scheduled.
            var guarded = await db.Drafts.FirstOrDefaultAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (guarded is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });
            var language = req.Language ?? guarded.PrimaryLanguage;
            if (!await DraftRevisionService.ConfirmationSatisfiedAsync(db, guarded, language,
                    DraftRevisionService.Kinds.Telegram, req.ChatId, req.ConfirmedFingerprint))
            {
                var fresh = await DraftRevisionService.PreviewAsync(db, guarded, language, DraftRevisionService.Kinds.Telegram, req.ChatId);
                return Results.Json(new { error = ErrorMessages.PublishConfirmationStale, preview = fresh }, statusCode: StatusCodes.Status409Conflict);
            }
            var result = await PublishAsync(req.DraftId, req.ChatId, uid, db, targets, req.Format, req.Language, logger, req.CompressionLevel);
            
            return result.Success ? 
                Results.Ok(new { messageId = result.MessageId, chatId = req.ChatId }) : 
                Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        }).RequireAuthorization();
    }


    private static async Task<string?> ResolveChannelUsernameAsync(CedarDbContext db, string chatId)
    {
        var trimmed = chatId.Trim();
        if (trimmed.StartsWith('@'))
            return trimmed[1..];

        return long.TryParse(trimmed, out var numericId)
            ? (await db.Channels.FirstOrDefaultAsync(c => c.TelegramChatId == numericId))?.Username
            : null;
    }

}
