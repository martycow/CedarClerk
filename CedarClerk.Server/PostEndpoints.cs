using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server;

public static class PostEndpoints
{ 
    public record ExportRequest(Guid DraftId, string ChatId, string Format = Consts.ContentTypes.Markdown, string Language = Languages.Primary);

    public record PublishResult(int? MessageId, string? Error, int StatusCode = StatusCodes.Status400BadRequest)
    {
        public bool Success => Error is null;
    }

    public static async Task<PublishResult> PublishAsync(
        Guid draftId,
        string chatId,
        string ownerId,
        CedarDbContext db,
        TelegramBotService bot,
        IConfiguration cfg,
        string format = Consts.ContentTypes.Markdown,
        string language = Languages.Primary)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == ownerId);
        if (draft is null)
            return new PublishResult(null, ErrorMessages.DraftNotFound, StatusCodes.Status404NotFound);

        var targetChannel = await SubscriptionPlan.ResolveOwnedChannelAsync(db, ownerId, chatId);
        if (targetChannel is null)
            return new PublishResult(null, "You can only publish to your connected channels — connect this channel first (Channels popup)", StatusCodes.Status403Forbidden);

        var cedarJson = draft.CedarJson;
        if (language != Languages.Primary)
        {
            var translation = await db.DraftTranslations.FirstOrDefaultAsync(t => t.DraftId == draftId && t.Language == language);
            if (translation is null)
                return new PublishResult(null, $"No {language.ToUpperInvariant()} version of this draft", StatusCodes.Status404NotFound);
            
            cedarJson = translation.CedarJson;
        }

        if (!bot.IsRunning)
            return new PublishResult(null, ErrorMessages.BotNotRunning, StatusCodes.Status503ServiceUnavailable);

        var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;

        // Bot API 10.2: Blocks is the only mode that reliably embeds media with a real, natively
        // styled caption (verified 16.07.2026 against @testingandfun) — see docs/DECISIONS.md.
        // `format` is still accepted (API/ScheduledPost compat) but no longer changes what's sent.
        var blocks = CedarToTelegramBlocksRenderer.Render(cedarJson, mainHost).ToList();

        if (blocks.Count == 0)
            return new PublishResult(null, "Draft is empty");

        var owner = await db.Users.Where(u => u.Id == ownerId)
            .Select(u => new { u.PostSignature, u.PlanTier, u.PlanExpiresAt })
            .FirstAsync();

        // Adding Signature (Custom of Default)
        var currentPlan = SubscriptionPlanHelper.CheckPlanExpiration(owner.PlanTier, owner.PlanExpiresAt, DateTime.UtcNow);
        var signature = PlanLimitations.HasCustomSignature(currentPlan)
            ? owner.PostSignature
            : null;

        if (!string.IsNullOrWhiteSpace(signature))
        {
            blocks.AddRange(signature.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(l => (CedarRichBlock)new RichParagraphBlock(new RichRunText(l))));
        }

        // Adding Cross-link to Blog site in the end of Telegram post
        if (draft.IsBlogPublished && draft.BlogSlug is not null)
        {
            var langSuffix = language == Languages.Primary ? "" : $"?lang={language}";
            var blogUrl = $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}{langSuffix}";

            blocks.Add(new RichParagraphBlock(new RichRunLink(new RichRunText("Read on the blog →"), blogUrl)));
        }

        var content = new InputRichMessage { Blocks = blocks.Select(ToInputRichBlock).ToList() };

        Message msg;
        try
        {
            msg = await bot.Client.SendRichMessage(new ChatId(chatId), content);
        }
        catch (Telegram.Bot.Exceptions.ApiRequestException ex)
        {
            // Telegram rejected the rendered content (bad markup, unsupported tag, etc.) — surface
            // its actual reason instead of letting this bubble up into a bare unhandled-exception 500.
            return new PublishResult(null, $"Telegram rejected the post: {ex.Message}", StatusCodes.Status502BadGateway);
        }

        draft.LastTelegramChatId = chatId;
        draft.LastTelegramMessageId = msg.MessageId;
        draft.LastTelegramUsername = await ResolveChannelUsernameAsync(db, chatId);
        await db.SaveChangesAsync();

        return new PublishResult(msg.MessageId, null);
    }
    
    public static void MapPostEndpoints(this WebApplication app)
    {
        app.MapPost("/api/posts/export", async (ExportRequest req, ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await PublishAsync(req.DraftId, req.ChatId, uid, db, bot, cfg, req.Format, req.Language);
            
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

    // Maps CedarClerk.Core's framework-agnostic RichBlock/RichRun tree (see
    // CedarToTelegramBlocksRenderer) onto the real Telegram.Bot wire types. Core stays free of a
    // Telegram.Bot dependency on purpose — this is the one place that knows about it.
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
        RichAudioBlock a => new InputRichBlockAudio { Audio = new InputMediaAudio(a.Url), Caption = ToCaption(a.Caption) },
        RichSlideshowBlock s => new InputRichBlockSlideshow { Blocks = s.Urls.Select(u => (InputRichBlock)new InputRichBlockPhoto { Photo = new InputMediaPhoto(u) }).ToList() },
        RichCollageBlock co => new InputRichBlockCollage { Blocks = co.Urls.Select(u => (InputRichBlock)new InputRichBlockPhoto { Photo = new InputMediaPhoto(u) }).ToList() },
        RichTableBlock t => new InputRichBlockTable { Cells = t.Rows.Select(row => row.Select(ToTableCell).ToList()).ToList(), IsBordered = true },
        RichMathBlock m => new InputRichBlockMathematicalExpression { Expression = m.Latex },
        RichDetailsBlock d => new InputRichBlockDetails { Summary = ToRichText(d.Summary), Blocks = d.Blocks.Select(ToInputRichBlock).ToList(), IsOpen = d.IsOpen },
        RichFooterBlock f => new InputRichBlockFooter { Text = ToRichText(f.Text) },
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
        _ => throw new NotSupportedException($"Unmapped RichRun: {run.GetType().Name}")
    };
}
