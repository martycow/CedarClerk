using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// Telegram as an <see cref="IPublishTarget"/> (T-085). Everything here was moved out of
/// `PostEndpoints.PublishAsync` **without a behaviour change** — same order of operations, same
/// error strings, same status codes, same rows written. That is the whole point of doing this
/// before a second network exists (ADR-070/078): the abstraction has to be shaped by the code
/// that already works, and then proved unchanged by the smoke suite.
///
/// One inherited oddity is preserved deliberately and recorded rather than fixed here: the
/// revision this writes holds the document with media paths rewritten to their Telegram-safe
/// derivatives, not the original. It does not affect the publish guard (which fingerprints the
/// current document on both sides) but it does make the diff on the next publish show every
/// compressed image as changed. Fixing it is a behaviour change, so it is a backlog row (T-104).
/// </summary>
public class TelegramPublishTarget(
    CedarDbContext db,
    TelegramBotService bot,
    IConfiguration cfg,
    MediaPaths media,
    MediaGrant grants,
    ILogger<TelegramPublishTarget> logger) : IPublishTarget
{
    public string Network => PublishNetworks.Telegram;

    // Measured against what the app actually sends today (Bot API 10.3, Blocks), not against the
    // documentation's theoretical maxima: the editor's own ceiling is 32768 characters, media is
    // fetched by URL and rejected above ~10MB (ADR-031), and a Blocks photo carries a caption but
    // no alt text. Threads: a channel post is one message, and nothing chains them.
    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.Telegram,
        MaxCharacters = Consts.Telegram.MaxPostChars,
        ThreadPartCharacters = Consts.Telegram.ThreadPartChars,
        MaxMediaItems = 10,
        MaxImageBytes = Consts.FileSizes.TelegramSafeImageBytes,
        SupportsVideo = true,
        SupportsAudio = true,
        SupportsRichText = true,
        SupportsHeadings = true,
        SupportsLists = true,
        SupportsTables = true,
        SupportsCodeBlocks = true,
        SupportsMath = true,
        SupportsLinkPreview = true,
        SupportsAltText = false,
        SupportsThreads = false,
        // A channel without a @username has no public post URL — the case ADR-065 found code
        // treating as "never published".
        PostsHavePublicUrls = true,
    };

    public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        if (!bot.IsRunning)
            return PublishOutcome.Fail(ErrorMessages.BotNotRunning, StatusCodes.Status503ServiceUnavailable);

        var chatId = request.Target.RemoteId;
        var cedarJson = request.CedarJson;
        var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;

        // Telegram rejects a photo fetched by URL above ~TelegramSafeImageBytes (confirmed
        // empirically 19.07.2026 — see ADR in docs/DECISIONS.md), so swap in a Telegram-safe
        // derivative for this render only — the stored draft/blog/.cedar export always keep the
        // original, untouched. Generated lazily here (not just at upload time) so assets uploaded
        // before this feature existed still get a derivative instead of failing forever.
        var compressionTargetBytes = PostEndpoints.ResolveCompressionTargetBytes(request.CompressionLevel);
        var referencedNames = CedarPackage.FindReferencedMediaPaths(cedarJson);
        if (referencedNames.Count > 0)
        {
            // T-359 (audit finding 3) — owner-scoped: the queue runs under a platform context with
            // the tenant filter off, so without this predicate a draft could name another account's
            // asset by file name (blog-published guids are public) and have its bytes uploaded and
            // its Telegram file_id row mutated.
            var referencedAssets = await db.Assets
                .Where(a => a.OwnerId == request.OwnerId && referencedNames.Contains(a.LocalPath)).ToListAsync(ct);
            foreach (var asset in referencedAssets)
                await AssetEndpoints.EnsureTelegramSafeAsync(asset, media, db, logger, compressionTargetBytes);

            var rewrites = referencedAssets.Where(a => a.TelegramLocalPath is not null)
                .ToDictionary(a => a.LocalPath, a => a.TelegramLocalPath!);
            if (rewrites.Count > 0)
                cedarJson = CedarPackage.RewriteMediaPaths(cedarJson, rewrites);
        }

        // Bot API 10.3: Blocks is the only mode that reliably embeds media with a real, natively
        // styled caption (verified 16.07.2026 against @testingandfun) — see docs/DECISIONS.md.
        var blocks = CedarToTelegramBlocksRenderer.Render(cedarJson, mainHost).ToList();

        if (blocks.Count == 0)
            return PublishOutcome.Fail(ErrorMessages.DraftIsEmpty);

        // T-106 — one part of a thread. The split is recomputed from the document rather than
        // carried in the job row: the same document and the same limits produce the same parts, so
        // there is nothing to keep in sync. (Editing the post mid-thread would shift the later
        // parts — which is what ADR-065's publish guard is for.)
        if (request.Part is { Count: > 1 } part)
        {
            var parts = TelegramThreadSplitter.Split(blocks, Capabilities);
            if (part.Index >= parts.Count)
                return PublishOutcome.Fail(ErrorMessages.ThreadPartGone, StatusCodes.Status409Conflict);
            blocks = parts[part.Index].Blocks.ToList();
        }

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var owner = await db.Users.Where(u => u.Id == request.OwnerId)
            .Select(u => new { u.PostSignature, u.PostSignatureUrl, u.PostSignatureTranslationsJson, u.PlanTier, u.PlanExpiresAt, u.BlogLinkText, u.BlogLinkTextTranslationsJson })
            .FirstAsync(ct);
        // The Channel row behind this target, when there is one: it can carry a per-channel
        // signature trio and it is where the last pinned message id lives.
        var channel = request.Target.ChannelId is { } linkedChannelId
            ? await db.Channels.FirstOrDefaultAsync(c => c.Id == linkedChannelId && c.OwnerId == request.OwnerId, ct)
            : null;

        // The signature, the cross-link and the hashtags close the *publication*, not every message
        // of it — repeated eight times they read as noise rather than as a signature (T-106).
        var isLastPart = request.Part is not { Count: > 1 } || request.Part.Index == request.Part.Count - 1;

        // Free tier always gets the fixed Cedar Clerk attribution; Pro+ can replace it with a
        // custom signature (optionally a clickable link) or clear it entirely. See Phase 8 Step 5,
        // docs/tasks/ROADMAP.md, and ADR-034 in docs/DECISIONS.md.
        var currentPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
        // FI5 — this Telegram send is already per-language, so the signature appended to it is too.
        // A channel with its own signature replaces the owner-level trio wholesale; the plan gate
        // below stays the same either way (free tier keeps the Cedar attribution).
        var (signatureText, signatureTranslationsJson, signatureUrl) = PickSignatureSource(
            channel?.PostSignature, channel?.PostSignatureTranslationsJson, channel?.PostSignatureUrl,
            owner.PostSignature, owner.PostSignatureTranslationsJson, owner.PostSignatureUrl);
        var localizedSignature = LocalizedTextMap.Pick(signatureText, signatureTranslationsJson, request.Language);
        var resolvedSignature = PlanLimitations.ResolveSignature(currentPlan, localizedSignature, signatureUrl);
        if (isLastPart && resolvedSignature is { } sig)
        {
            // B17 — bold, so the signature reads as a signature in the channel rather than as one
            // more paragraph of the post. A linked signature is bolded inside the link, since
            // Telegram renders a bold run within a link fine but not a link inside bold.
            blocks.AddRange(sig.Text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(l => (CedarRichBlock)new RichParagraphBlock(sig.Href is null
                    ? new RichRunBold(new RichRunText(l))
                    : new RichRunLink(new RichRunBold(new RichRunText(l)), sig.Href))));
        }

        // Cross-link to the blog at the end of the Telegram post. The host comes from the draft's
        // owner, never from the request: this runs from the queue too, and a channel message is
        // history that cannot be corrected once a per-owner slug has pointed at the wrong blog.
        if (isLastPart && await MicroThreadPlan.BlogUrlAsync(draft, request.Language, db, cfg, ct) is { } blogUrl)
        {
            // I15 — the author's own wording when they set one, the built-in otherwise. Per
            // language: this line is read by whoever reads that language's version of the post.
            var blogLinkText = LocalizedTextMap.Pick(owner.BlogLinkText, owner.BlogLinkTextTranslationsJson, request.Language)
                ?? Consts.CrossLinks.DefaultBlogLinkText;
            blocks.Add(new RichParagraphBlock(new RichRunLink(new RichRunText(blogLinkText), blogUrl)));
        }

        // Phase 8 Step 6, docs/tasks/ROADMAP.md — tags extended to the Telegram export path.
        if (isLastPart && PostEndpoints.BuildHashtagLine(draft.Tags) is { } hashtagLine)
            blocks.Add(new RichParagraphBlock(new RichRunText(hashtagLine)));

        // "3/8" as a footer, not a heading: the first message should open with the post, not with
        // its own bookkeeping — but a reader who arrives mid-thread still needs to know there is more.
        if (request.Part is { Count: > 1 } counted)
        {
            blocks.Add(new RichFooterBlock(new RichRunText($"{counted.Index + 1}/{counted.Count}")));
        }

        // ADR-088/ADR-089 — media on this server's own disk reaches Telegram as bytes, not as a
        // URL to fetch. The URL round-trip (Kestrel → tunnel → Cloudflare → Telegram's fetcher,
        // with its timeout, its concurrency and its negative cache) produced both "wrong type of
        // the web page content" and "failed to get HTTP URL content" on perfectly served files.
        // SendRichMessage itself is JSON-only (RequestBase, not FileRequestBase — ADR-089), so
        // each file is pre-uploaded once through SendPhoto/Video/Audio to the owner's own bot
        // chat, the buffer message deleted, and Blocks sent by the cached file_id. External URLs
        // (YouTube thumbnails) still go by URL and keep ADR-087's per-send stamp.
        var deliverByUpload = !string.Equals(cfg[Consts.Telegram.MediaDeliveryCfg],
            Consts.Telegram.MediaDeliveryUrl, StringComparison.OrdinalIgnoreCase);
        var cacheStamp = DateTime.UtcNow.Ticks.ToString("x");
        var fileIds = deliverByUpload
            ? await ResolveFileIdsAsync(blocks, request.OwnerId, ct)
            : new Dictionary<string, string>();
        // ADR-091 — the stamp busts Telegram's negative cache of OUR origin; a foreign host may
        // refuse unknown query strings outright (img.youtube.com answers 404), so external URLs
        // go out exactly as rendered.
        InputFile ResolveMedia(string url) =>
            !TryLocalMediaFileName(url, out var fileName) ? new InputFileUrl(url)
            : fileIds.TryGetValue(fileName, out var fileId) ? InputFile.FromFileId(fileId)
            : new InputFileUrl(StampUrl(url, cacheStamp, grants.Issue(fileName)));

        var wireBlocks = blocks.Select(b => ToInputRichBlock(b, ResolveMedia)).ToList();
        // CTA buttons close the publication the way the signature does — once, on the last part,
        // and at the wire level only: they are a per-post send setting, not document content, so
        // the stored draft/blog/.cedar export never see them.
        if (isLastPart && BuildCtaButtons(draft.CtaButtonsJson) is { } ctaButtons)
            wireBlocks.Add(ctaButtons);
        var content = new InputRichMessage { Blocks = wireBlocks };

        Message msg;
        try
        {
            // Each part replies to the one before it, which is what makes Telegram show a thread
            // rather than eight loose posts. Only the first part rings: eight notifications for one
            // publication is how a channel loses subscribers.
            var replyTo = request.Part?.ReplyToRemoteId is { } previous && int.TryParse(previous, out var replyId)
                ? new ReplyParameters { MessageId = replyId }
                : null;

            msg = await bot.Client.SendRichMessage(new ChatId(chatId), content,
                replyParameters: replyTo,
                disableNotification: ShouldSilence(request.Silent, request.Part),
                cancellationToken: ct);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex)
        {
            // Telegram rejected the rendered content (bad markup, unsupported tag, empty media
            // group, etc.) — surface its actual reason (plus code/retry-after when present)
            // instead of letting this bubble up into a bare unhandled-exception 500.
            logger.LogError(ex, "Telegram rejected publish of draft {DraftId} to {ChatId} (code {ErrorCode})", request.DraftId, chatId, ex.ErrorCode);
            var retryHint = ex.Parameters?.RetryAfter is { } retryAfter ? $" — retry after {retryAfter}s" : "";
            // Telegram's own code, not a blanket 502 (fixed 01.08.2026). Everything used to come
            // back as 502, which T-090's queue reads as "could not have posted, try again" — so a
            // document Telegram had *refused* was sent three times before being reported. A 400 is
            // the network's verdict on the content and repeating it only wastes two more minutes.
            var status = ex.ErrorCode switch
            {
                400 or 401 or 403 or 404 => ex.ErrorCode,
                429 => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status502BadGateway,
            };
            return PublishOutcome.Fail($"Telegram rejected the post: {ex.Message} (code {ex.ErrorCode}){retryHint}", status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else — a mapping bug, a network-level RequestException, a JSON parse failure
            // in the renderer — gets the same readable-error treatment rather than an opaque 500.
            logger.LogError(ex, "Unexpected failure publishing draft {DraftId} to {ChatId}", request.DraftId, chatId);
            return PublishOutcome.Fail($"Publish failed: {ex.GetType().Name}: {ex.Message}", StatusCodes.Status500InternalServerError);
        }

        var username = await ResolveChannelUsernameAsync(db, chatId, ct);
        draft.LastTelegramChatId = chatId;
        draft.LastTelegramMessageId = msg.MessageId;
        draft.LastTelegramUsername = username;
        if (request.Target.ChannelId is { } channelId)
            db.ChannelPosts.Add(new ChannelPost { ChannelId = channelId, OwnerId = draft.OwnerId, DraftId = request.DraftId, TelegramMessageId = msg.MessageId });

        // Pin the thread root, not every part, and never let pinning decide the publish outcome:
        // the message is already in the channel, so a missing can_pin_messages right is a warning
        // in the log, not a failed send. The previous pin is released first so "pin after send"
        // reads as "keep the latest post pinned" rather than piling pins up.
        if (channel is not null && ShouldPin(request.PinAfterSend, request.Part))
        {
            if (channel.LastPinnedMessageId is { } previousPinned)
            {
                try { await bot.Client.UnpinChatMessage(new ChatId(chatId), previousPinned, cancellationToken: ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not unpin message {MessageId} in {ChatId}", previousPinned, chatId);
                }
            }
            try
            {
                await bot.Client.PinChatMessage(new ChatId(chatId), msg.MessageId, disableNotification: true, cancellationToken: ct);
                channel.LastPinnedMessageId = msg.MessageId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not pin message {MessageId} in {ChatId}", msg.MessageId, chatId);
            }
        }

        // The revision is the baseline for "what would an update overwrite", so it is recorded once
        // per publication — on the last part, when the whole document has actually gone out.
        if (isLastPart)
        {
            await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title, cedarJson,
                DraftRevisionService.Kinds.Telegram, chatId, ct);
        }

        var publicUrl = username is null ? null : $"https://t.me/{username}/{msg.MessageId}";
        return PublishOutcome.Ok(new PublishReceipt(msg.MessageId.ToString(System.Globalization.CultureInfo.InvariantCulture), publicUrl));
    }

    // Maps CedarClerk.Core's framework-agnostic RichBlock/RichRun tree (see
    // CedarToTelegramBlocksRenderer) onto the real Telegram.Bot wire types. Core stays free of a
    // Telegram.Bot dependency on purpose — this is the one place that knows about it, and T-085
    // moved it here from PostEndpoints because "the one place" is this target, not the endpoint.
    //
    // `media` decides what Telegram receives for a media URL — the bytes of a local file (ADR-088)
    // or the URL itself. The resolver-free overload keeps URL delivery for tests and callers that
    // have no disk to read from.
    public static InputRichBlock ToInputRichBlock(CedarRichBlock block) => ToInputRichBlock(block, url => url);

    public static InputRichBlock ToInputRichBlock(CedarRichBlock block, Func<string, InputFile> media) => block switch
    {
        RichParagraphBlock p => new InputRichBlockParagraph { Text = ToRichText(p.Text) },
        RichHeadingBlock h => new InputRichBlockSectionHeading { Text = ToRichText(h.Text), Size = h.Level },
        RichListBlock l => new InputRichBlockList { Items = l.Items.Select(i => ToListItem(i, media)).ToList() },
        RichCodeBlock c => new InputRichBlockPreformatted { Text = new RichTextText { Text = c.Code }, Language = c.Language },
        RichQuoteBlock q => new InputRichBlockBlockQuotation { Blocks = q.Blocks.Select(b => ToInputRichBlock(b, media)).ToList() },
        // The expandable variant carries rich text, not nested blocks (Bot API 10.3) — the
        // renderer only emits it for paragraph-only quotes, and the paragraphs join by newline.
        RichExpandableQuoteBlock eq => new InputRichBlockExpandableBlockQuotation { Text = FlattenQuoteText(eq.Blocks) },
        RichDividerBlock => new InputRichBlockDivider(),
        RichPhotoBlock ph => new InputRichBlockPhoto { Photo = new InputMediaPhoto(media(ph.Url)), Caption = ToCaption(ph.Caption) },
        RichVideoBlock v => new InputRichBlockVideo { Video = new InputMediaVideo(media(v.Url)), Caption = ToCaption(v.Caption) },
        // Title is what Telegram labels the clip with; without it the player shows the generated
        // asset_<guid>.mp3 filename from the URL (I16).
        RichAudioBlock a => new InputRichBlockAudio { Audio = new InputMediaAudio(media(a.Url)) { Title = a.Title }, Caption = ToCaption(a.Caption) },
        RichSlideshowBlock s => new InputRichBlockSlideshow { Blocks = s.Urls.Select(u => (InputRichBlock)new InputRichBlockPhoto { Photo = new InputMediaPhoto(media(u)) }).ToList() },
        RichCollageBlock co => new InputRichBlockCollage { Blocks = co.Urls.Select(u => (InputRichBlock)new InputRichBlockPhoto { Photo = new InputMediaPhoto(media(u)) }).ToList() },
        RichTableBlock t => new InputRichBlockTable { Cells = t.Rows.Select(row => row.Select(ToTableCell).ToList()).ToList(), IsBordered = true },
        RichMathBlock m => new InputRichBlockMathematicalExpression { Expression = m.Latex },
        RichDetailsBlock d => new InputRichBlockDetails { Summary = ToRichText(d.Summary), Blocks = d.Blocks.Select(b => ToInputRichBlock(b, media)).ToList(), IsOpen = d.IsOpen },
        RichFooterBlock f => new InputRichBlockFooter { Text = ToRichText(f.Text) },
        RichAnchorBlock an => new InputRichBlockAnchor { Name = an.Name },
        _ => throw new NotSupportedException($"Unmapped RichBlock: {block.GetType().Name}")
    };

    /// <summary>
    /// ADR-089 — file_ids for every local media file the blocks reference: cached on the Asset
    /// where possible, minted by a silent self-deleting upload to the owner's bot chat otherwise.
    /// A file that cannot be uploaded (no linked Telegram, bot chat never opened) is simply
    /// absent from the result and falls back to URL delivery.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveFileIdsAsync(
        IReadOnlyList<CedarRichBlock> blocks, string ownerId, CancellationToken ct)
    {
        var fileIds = new Dictionary<string, string>();
        var refs = blocks.SelectMany(MediaRefs)
            .Where(r => TryLocalMediaFileName(r.Url, out _))
            .Select(r => (Name: LocalName(r.Url), r.Kind))
            .DistinctBy(r => r.Name)
            .Where(r => File.Exists(Path.Combine(media.Dir, r.Name)))
            .ToList();
        if (refs.Count == 0) return fileIds;

        // The storage chat is the owner's own private chat with the bot — the one place the bot
        // can put a buffer message that belongs to this user and to nobody else's eyes.
        var storageChat = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => u.TelegramUserId).FirstOrDefaultAsync(ct);
        var names = refs.Select(r => r.Name).ToList();
        // Owner-scoped for the same reason as PublishAsync's referenced-asset query (finding 3):
        // the file_id cache must never be read from or written to another account's Asset row.
        var assets = await db.Assets
            .Where(a => a.OwnerId == ownerId && (names.Contains(a.LocalPath) || names.Contains(a.TelegramLocalPath!)))
            .ToListAsync(ct);
        var dirty = false;

        foreach (var (name, kind) in refs)
        {
            var asset = assets.FirstOrDefault(a => a.TelegramLocalPath == name || a.LocalPath == name);
            if (asset is { TelegramFileId: not null } && asset.TelegramFileIdSourcePath == name)
            {
                fileIds[name] = asset.TelegramFileId;
                continue;
            }
            if (storageChat is null) continue;   // no linked Telegram — URL fallback for all new files

            try
            {
                await using var stream = File.OpenRead(Path.Combine(media.Dir, name));
                var input = InputFile.FromStream(stream, name);
                var uploaded = kind switch
                {
                    MediaKind.Video => await bot.Client.SendVideo(storageChat, input, disableNotification: true, cancellationToken: ct),
                    MediaKind.Audio => await bot.Client.SendAudio(storageChat, input, disableNotification: true, cancellationToken: ct),
                    _ => await bot.Client.SendPhoto(storageChat, input, disableNotification: true, cancellationToken: ct),
                };
                var fileId = uploaded.Video?.FileId ?? uploaded.Audio?.FileId ?? uploaded.Document?.FileId
                    ?? uploaded.Photo?.MaxBy(p => (long)p.Width * p.Height)?.FileId;
                if (fileId is null)
                {
                    logger.LogWarning("Buffer upload of {Name} returned a message with no recognisable media", name);
                    continue;
                }

                fileIds[name] = fileId;
                if (asset is not null)
                {
                    asset.TelegramFileId = fileId;
                    asset.TelegramFileIdSourcePath = name;
                    dirty = true;
                }

                // The buffer message has served its purpose the moment the file_id exists.
                try { await bot.Client.DeleteMessage(storageChat, uploaded.MessageId, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not delete buffer message for {Name}", name); }
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {
                // A file that cannot be pre-uploaded is not a reason to refuse the publication —
                // it falls back to the URL path, which is exactly what every publish did before.
                logger.LogWarning(ex, "Pre-upload of {Name} failed (code {Code}) — falling back to URL delivery", name, ex.ErrorCode);
            }
        }

        // Cached ids are worth keeping even if the send below fails — that is the whole point.
        if (dirty) await db.SaveChangesAsync(ct);
        return fileIds;
    }

    private static string LocalName(string url)
    {
        TryLocalMediaFileName(url, out var name);
        return name;
    }

    private enum MediaKind { Photo, Video, Audio }

    private static IEnumerable<(string Url, MediaKind Kind)> MediaRefs(CedarRichBlock block) => block switch
    {
        RichPhotoBlock p => [(p.Url, MediaKind.Photo)],
        RichVideoBlock v => [(v.Url, MediaKind.Video)],
        RichAudioBlock a => [(a.Url, MediaKind.Audio)],
        RichSlideshowBlock s => s.Urls.Select(u => (u, MediaKind.Photo)),
        RichCollageBlock c => c.Urls.Select(u => (u, MediaKind.Photo)),
        RichQuoteBlock q => q.Blocks.SelectMany(MediaRefs),
        RichDetailsBlock d => d.Blocks.SelectMany(MediaRefs),
        RichListBlock l => l.Items.SelectMany(i => i.Blocks.SelectMany(MediaRefs)),
        _ => [],
    };

    /// <summary>
    /// ADR-088 — the trailing filename of a URL that points into this server's own /media/, or
    /// false for anything foreign. Existence on disk is the caller's check, not this one's.
    /// </summary>
    public static bool TryLocalMediaFileName(string url, out string fileName)
    {
        fileName = "";
        var idx = url.LastIndexOf("/media/", StringComparison.Ordinal);
        if (idx < 0) return false;
        var name = url[(idx + "/media/".Length)..];
        var query = name.IndexOf('?');
        if (query >= 0) name = name[..query];
        // A name with path separators is not one of our generated asset names — refuse rather
        // than let a crafted URL reach outside the media directory.
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..")) return false;
        fileName = name;
        return true;
    }

    /// <summary>
    /// ADR-087's per-send cache-buster, plus the grant that lets Telegram's fetcher — anonymous,
    /// and pulling a file no published post claims yet — read it at all (T-285).
    /// </summary>
    public static string StampUrl(string url, string stamp, string? grant = null)
    {
        var stamped = url.Contains('?') ? $"{url}&v={stamp}" : $"{url}?v={stamp}";
        return grant is null ? stamped : $"{stamped}&{MediaGrant.QueryKey}={Uri.EscapeDataString(grant)}";
    }

    /// <summary>
    /// Whether this channel's own signature trio replaces the owner-level one: a non-null channel
    /// signature is the whole switch, and the trio moves together — a channel that overrides the
    /// text never keeps the owner's translations or URL under it.
    /// </summary>
    public static (string? Text, string? TranslationsJson, string? Url) PickSignatureSource(
        string? channelText, string? channelTranslationsJson, string? channelUrl,
        string? ownerText, string? ownerTranslationsJson, string? ownerUrl) =>
        channelText is not null
            ? (channelText, channelTranslationsJson, channelUrl)
            : (ownerText, ownerTranslationsJson, ownerUrl);

    /// <summary>Later thread parts never ring, whatever the author chose for the publication.</summary>
    public static bool ShouldSilence(bool silent, ThreadPartRef? part) => silent || part is { Index: > 0 };

    /// <summary>Only the thread root gets pinned — one publication, one pin.</summary>
    public static bool ShouldPin(bool pinAfterSend, ThreadPartRef? part) =>
        pinAfterSend && part is not { Index: > 0 };

    /// <summary>
    /// Draft.CtaButtonsJson (a JSON array of {"text","url"}) as a wire-level button row, or null
    /// when nothing valid remains. Invalid entries — blank or over-long text, anything but an
    /// absolute http/https URL — drop silently, and only the first three buttons ride along.
    /// </summary>
    public static InputRichBlockButtons? BuildCtaButtons(string? ctaButtonsJson)
    {
        var buttons = ParseCtaButtons(ctaButtonsJson);
        return buttons.Count == 0 ? null : new InputRichBlockButtons
        {
            Buttons = buttons
                .Select(b => new RichMessageButton { Text = new RichTextText { Text = b.Text }, Url = b.Url })
                .ToList(),
        };
    }

    public const int MaxCtaButtons = 3;
    public const int MaxCtaButtonTextLength = 32;

    public static IReadOnlyList<(string Text, string Url)> ParseCtaButtons(string? ctaButtonsJson)
    {
        if (string.IsNullOrWhiteSpace(ctaButtonsJson)) return [];

        JsonNode? root;
        try { root = JsonNode.Parse(ctaButtonsJson); }
        catch (System.Text.Json.JsonException) { return []; }
        if (root is not JsonArray items) return [];

        var buttons = new List<(string Text, string Url)>();
        foreach (var item in items)
        {
            var text = ((string?)item?["text"])?.Trim();
            var url = ((string?)item?["url"])?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > MaxCtaButtonTextLength) continue;
            if (url is null
                || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) continue;

            buttons.Add((text, url));
            if (buttons.Count == MaxCtaButtons) break;
        }
        return buttons;
    }

    /// <summary>
    /// An expandable quote's paragraphs as one rich text, joined by newlines. Non-paragraph
    /// blocks contribute nothing — the renderer guarantees there are none.
    /// </summary>
    public static RichText FlattenQuoteText(IReadOnlyList<CedarRichBlock> blocks)
    {
        var texts = new List<RichText>();
        foreach (var run in blocks.OfType<RichParagraphBlock>().Select(p => p.Text))
        {
            if (texts.Count > 0) texts.Add(new RichTextText { Text = "\n" });
            texts.Add(ToRichText(run));
        }
        return texts.Count == 1 ? texts[0] : new RichTextArray { Array = texts.ToArray() };
    }

    public static RichBlockCaption? ToCaption(RichRun? caption) =>
        caption is null ? null : new RichBlockCaption { Text = ToRichText(caption) };

    // Align and Valign are NOT optional, and their enums start at 1 — leaving them unset makes the
    // wire value 0, which Telegram.Bot's serializer refuses with "Can't serialize value 0 for enum
    // RichBlockTableCellAlign". That threw on every post containing a table, i.e. tables have never
    // once published successfully (found in production 01.08.2026; TASKS.md had them listed as
    // "implemented but never exercised with a real post", which is exactly what this was).
    //
    // Left/Middle rather than something cleverer: CedarClerk.Core's RichTableCell carries no
    // alignment, because the TipTap table node does not set one either. Inventing centred headers
    // here would make the channel disagree with the editor.
    public static RichBlockTableCell ToTableCell(RichTableCell cell) => new()
    {
        Text = ToRichText(cell.Text),
        IsHeader = cell.IsHeader,
        Colspan = cell.Colspan,
        Rowspan = cell.Rowspan,
        Align = RichBlockTableCellAlign.Left,
        Valign = RichBlockTableCellValign.Middle,
    };

    public static InputRichBlockListItem ToListItem(RichListItem item) => ToListItem(item, url => url);

    public static InputRichBlockListItem ToListItem(RichListItem item, Func<string, InputFile> media) => new()
    {
        Blocks = item.Blocks.Select(b => ToInputRichBlock(b, media)).ToList(),
        HasCheckbox = item.HasCheckbox,
        IsChecked = item.IsChecked,
        Value = item.OrderValue
    };

    public static RichText ToRichText(RichRun run) => run switch
    {
        RichRunText t => new RichTextText { Text = t.Text },
        RichRunBold b => new RichTextBold { Text = ToRichText(b.Inner) },
        RichRunItalic i => new RichTextItalic { Text = ToRichText(i.Inner) },
        RichRunUnderline u => new RichTextUnderline { Text = ToRichText(u.Inner) },
        RichRunStrike s => new RichTextStrikethrough { Text = ToRichText(s.Inner) },
        RichRunCode c => new RichTextCode { Text = ToRichText(c.Inner) },
        RichRunSpoiler sp => new RichTextSpoiler { Text = ToRichText(sp.Inner) },
        RichRunLink l => new RichTextUrl { Text = ToRichText(l.Inner), Url = l.Url },
        RichRunDateTime dt => new RichTextDateTime
        {
            Text = new RichTextText { Text = dt.FallbackText },
            UnixTime = DateTimeOffset.FromUnixTimeSeconds(dt.UnixSeconds).UtcDateTime,
            DateTimeFormat = dt.Format
        },
        RichRunMath m => new RichTextMathematicalExpression { Expression = m.Latex },
        RichRunSequence seq => new RichTextArray { Array = seq.Runs.Select(ToRichText).ToArray() },
        RichRunAnchorLink al => new RichTextAnchorLink { Text = ToRichText(al.Inner), AnchorName = al.AnchorName },
        _ => throw new NotSupportedException($"Unmapped RichRun: {run.GetType().Name}")
    };

    private static async Task<string?> ResolveChannelUsernameAsync(CedarDbContext db, string chatId, CancellationToken ct)
    {
        var trimmed = chatId.Trim();
        if (trimmed.StartsWith('@'))
            return trimmed[1..];

        return long.TryParse(trimmed, out var numericId)
            ? (await db.Channels.FirstOrDefaultAsync(c => c.TelegramChatId == numericId, ct))?.Username
            : null;
    }
}
