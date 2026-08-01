using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;

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
    ILogger<TelegramPublishTarget> logger) : IPublishTarget
{
    public string Network => PublishNetworks.Telegram;

    // Measured against what the app actually sends today (Bot API 10.2, Blocks), not against the
    // documentation's theoretical maxima: the editor's own ceiling is 32768 characters, media is
    // fetched by URL and rejected above ~10MB (ADR-031), and a Blocks photo carries a caption but
    // no alt text. Threads: a channel post is one message, and nothing chains them.
    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.Telegram,
        MaxCharacters = Consts.Telegram.MaxPostChars,
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
            var referencedAssets = await db.Assets.Where(a => referencedNames.Contains(a.LocalPath)).ToListAsync(ct);
            foreach (var asset in referencedAssets)
                await AssetEndpoints.EnsureTelegramSafeAsync(asset, media, db, logger, compressionTargetBytes);

            var rewrites = referencedAssets.Where(a => a.TelegramLocalPath is not null)
                .ToDictionary(a => a.LocalPath, a => a.TelegramLocalPath!);
            if (rewrites.Count > 0)
                cedarJson = CedarPackage.RewriteMediaPaths(cedarJson, rewrites);
        }

        // Bot API 10.2: Blocks is the only mode that reliably embeds media with a real, natively
        // styled caption (verified 16.07.2026 against @testingandfun) — see docs/DECISIONS.md.
        var blocks = CedarToTelegramBlocksRenderer.Render(cedarJson, mainHost).ToList();

        if (blocks.Count == 0)
            return PublishOutcome.Fail("Draft is empty");

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var owner = await db.Users.Where(u => u.Id == request.OwnerId)
            .Select(u => new { u.PostSignature, u.PostSignatureUrl, u.PostSignatureTranslationsJson, u.PlanTier, u.PlanExpiresAt, u.BlogLinkText, u.BlogLinkTextTranslationsJson })
            .FirstAsync(ct);

        // Free tier always gets the fixed Cedar Clerk attribution; Pro+ can replace it with a
        // custom signature (optionally a clickable link) or clear it entirely. See Phase 8 Step 5,
        // docs/ROADMAP.md, and ADR-034 in docs/DECISIONS.md.
        var currentPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
        // FI5 — this Telegram send is already per-language, so the signature appended to it is too.
        var localizedSignature = LocalizedTextMap.Pick(owner.PostSignature, owner.PostSignatureTranslationsJson, request.Language);
        var resolvedSignature = PlanLimitations.ResolveSignature(currentPlan, localizedSignature, owner.PostSignatureUrl);
        if (resolvedSignature is { } sig)
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

        // Cross-link to the blog at the end of the Telegram post.
        if (draft.IsBlogPublished && draft.BlogSlug is not null)
        {
            var langSuffix = request.Language == draft.PrimaryLanguage ? "" : $"?lang={request.Language}";
            var blogUrl = $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}{langSuffix}";

            // I15 — the author's own wording when they set one, the built-in otherwise. Per
            // language: this line is read by whoever reads that language's version of the post.
            var blogLinkText = LocalizedTextMap.Pick(owner.BlogLinkText, owner.BlogLinkTextTranslationsJson, request.Language)
                ?? Consts.CrossLinks.DefaultBlogLinkText;
            blocks.Add(new RichParagraphBlock(new RichRunLink(new RichRunText(blogLinkText), blogUrl)));
        }

        // Phase 8 Step 6, docs/ROADMAP.md — tags extended to the Telegram export path.
        if (PostEndpoints.BuildHashtagLine(draft.Tags) is { } hashtagLine)
            blocks.Add(new RichParagraphBlock(new RichRunText(hashtagLine)));

        var content = new InputRichMessage { Blocks = blocks.Select(ToInputRichBlock).ToList() };

        Message msg;
        try
        {
            msg = await bot.Client.SendRichMessage(new ChatId(chatId), content, cancellationToken: ct);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex)
        {
            // Telegram rejected the rendered content (bad markup, unsupported tag, empty media
            // group, etc.) — surface its actual reason (plus code/retry-after when present)
            // instead of letting this bubble up into a bare unhandled-exception 500.
            logger.LogError(ex, "Telegram rejected publish of draft {DraftId} to {ChatId} (code {ErrorCode})", request.DraftId, chatId, ex.ErrorCode);
            var retryHint = ex.Parameters?.RetryAfter is { } retryAfter ? $" — retry after {retryAfter}s" : "";
            return PublishOutcome.Fail($"Telegram rejected the post: {ex.Message} (code {ex.ErrorCode}){retryHint}", StatusCodes.Status502BadGateway);
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
            db.ChannelPosts.Add(new ChannelPost { ChannelId = channelId, DraftId = request.DraftId, TelegramMessageId = msg.MessageId });

        // The document as it was actually sent — see the class comment on why this is the rewritten
        // one and why that is left alone here.
        await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title, cedarJson,
            DraftRevisionService.Kinds.Telegram, chatId, ct);

        var publicUrl = username is null ? null : $"https://t.me/{username}/{msg.MessageId}";
        return PublishOutcome.Ok(new PublishReceipt(msg.MessageId.ToString(System.Globalization.CultureInfo.InvariantCulture), publicUrl));
    }

    // Maps CedarClerk.Core's framework-agnostic RichBlock/RichRun tree (see
    // CedarToTelegramBlocksRenderer) onto the real Telegram.Bot wire types. Core stays free of a
    // Telegram.Bot dependency on purpose — this is the one place that knows about it, and T-085
    // moved it here from PostEndpoints because "the one place" is this target, not the endpoint.
    private static InputRichBlock ToInputRichBlock(CedarRichBlock block) => block switch
    {
        RichParagraphBlock p => new InputRichBlockParagraph { Text = ToRichText(p.Text) },
        RichHeadingBlock h => new InputRichBlockSectionHeading { Text = ToRichText(h.Text), Size = h.Level },
        RichListBlock l => new InputRichBlockList { Items = l.Items.Select(ToListItem).ToList() },
        RichCodeBlock c => new InputRichBlockPreformatted { Text = new RichTextText { Text = c.Code }, Language = c.Language },
        RichQuoteBlock q => new InputRichBlockBlockQuotation { Blocks = q.Blocks.Select(ToInputRichBlock).ToList() },
        RichDividerBlock => new InputRichBlockDivider(),
        RichPhotoBlock ph => new InputRichBlockPhoto { Photo = new InputMediaPhoto(ph.Url), Caption = ToCaption(ph.Caption) },
        RichVideoBlock v => new InputRichBlockVideo { Video = new InputMediaVideo(v.Url), Caption = ToCaption(v.Caption) },
        // Title is what Telegram labels the clip with; without it the player shows the generated
        // asset_<guid>.mp3 filename from the URL (I16).
        RichAudioBlock a => new InputRichBlockAudio { Audio = new InputMediaAudio(a.Url) { Title = a.Title }, Caption = ToCaption(a.Caption) },
        RichSlideshowBlock s => new InputRichBlockSlideshow { Blocks = s.Urls.Select(u => (InputRichBlock)new InputRichBlockPhoto { Photo = new InputMediaPhoto(u) }).ToList() },
        RichCollageBlock co => new InputRichBlockCollage { Blocks = co.Urls.Select(u => (InputRichBlock)new InputRichBlockPhoto { Photo = new InputMediaPhoto(u) }).ToList() },
        RichTableBlock t => new InputRichBlockTable { Cells = t.Rows.Select(row => row.Select(ToTableCell).ToList()).ToList(), IsBordered = true },
        RichMathBlock m => new InputRichBlockMathematicalExpression { Expression = m.Latex },
        RichDetailsBlock d => new InputRichBlockDetails { Summary = ToRichText(d.Summary), Blocks = d.Blocks.Select(ToInputRichBlock).ToList(), IsOpen = d.IsOpen },
        RichFooterBlock f => new InputRichBlockFooter { Text = ToRichText(f.Text) },
        RichAnchorBlock an => new InputRichBlockAnchor { Name = an.Name },
        _ => throw new NotSupportedException($"Unmapped RichBlock: {block.GetType().Name}")
    };

    private static RichBlockCaption? ToCaption(RichRun? caption) =>
        caption is null ? null : new RichBlockCaption { Text = ToRichText(caption) };

    private static RichBlockTableCell ToTableCell(RichTableCell cell) => new()
    {
        Text = ToRichText(cell.Text),
        IsHeader = cell.IsHeader,
        Colspan = cell.Colspan,
        Rowspan = cell.Rowspan
    };

    private static InputRichBlockListItem ToListItem(RichListItem item) => new()
    {
        Blocks = item.Blocks.Select(ToInputRichBlock).ToList(),
        HasCheckbox = item.HasCheckbox,
        IsChecked = item.IsChecked,
        Value = item.OrderValue
    };

    private static RichText ToRichText(RichRun run) => run switch
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
