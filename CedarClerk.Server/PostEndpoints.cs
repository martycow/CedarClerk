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
        var isMarkdown = format == Consts.ContentTypes.Markdown;
        
        var rendered = isMarkdown
            ? CedarToTelegramMarkdownRenderer.Render(cedarJson, mainHost)
            : CedarToTelegramHtmlRenderer.Render(cedarJson, mainHost);
        
        if (string.IsNullOrWhiteSpace(rendered))
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
            rendered += isMarkdown
                ? "\n\n" + signature
                : string.Concat(signature.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Select(l => $"<p>{System.Net.WebUtility.HtmlEncode(l)}</p>"));
        }

        // Adding Cross-link to Blog site in the end of Telegram post
        if (draft.IsBlogPublished && draft.BlogSlug is not null)
        {
            var langSuffix = language == Languages.Primary ? "" : $"?lang={language}";
            var blogUrl = $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}{langSuffix}";
            
            rendered += isMarkdown ? 
                $"\n\n[Read on the blog]({blogUrl})" : 
                $"<p><a href=\"{blogUrl}\">Read on the blog &#8594;</a></p>";
        }

        var content = isMarkdown
            ? new InputRichMessage { Markdown = rendered }
            : new InputRichMessage { Html = rendered };

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
}
