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
    public record ExportRequest(Guid DraftId, string ChatId, string Format = Consts.Html, string Language = Languages.Primary);

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
        string format = Consts.Html,
        string language = Languages.Primary)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == ownerId);
        if (draft is null)
            return new PublishResult(null, ErrorMessages.DraftNotFound, StatusCodes.Status404NotFound);

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

        var baseUrl = cfg[Consts.PublicBaseUrlCfg] ?? Consts.Url;
        var isMarkdown = format == Consts.Markdown;
        var rendered = isMarkdown
            ? CedarToTelegramMarkdownRenderer.Render(cedarJson, baseUrl)
            : CedarToTelegramHtmlRenderer.Render(cedarJson, baseUrl);
        if (string.IsNullOrWhiteSpace(rendered))
            return new PublishResult(null, "Draft is empty");

        // The user's standard signature goes at the very end of the post content itself,
        // before the "Read on the blog" cross-link (which is navigation, not content).
        var signature = await db.Users.Where(u => u.Id == ownerId).Select(u => u.PostSignature).FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(signature))
        {
            rendered += isMarkdown
                ? "\n\n" + signature
                : string.Concat(signature.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Select(l => $"<p>{System.Net.WebUtility.HtmlEncode(l)}</p>"));
        }

        if (draft.IsBlogPublished && draft.BlogSlug is not null)
        {
            var langSuffix = language == Languages.Primary ? "" : $"?lang={language}";
            var blogUrl = $"https://{cfg[Consts.BlogHostCfg] ?? Consts.DefaultBlogHost}/{draft.BlogSlug}{langSuffix}";
            rendered += isMarkdown ? $"\n\n[Read on the blog]({blogUrl})" : $"<p><a href=\"{blogUrl}\">Read on the blog &#8594;</a></p>";
        }

        var content = isMarkdown
            ? new InputRichMessage { Markdown = rendered }
            : new InputRichMessage { Html = rendered };
        var msg = await bot.Client.SendRichMessage(new ChatId(chatId), content);

        draft.LastTelegramChatId = chatId;
        draft.LastTelegramMessageId = msg.MessageId;
        draft.LastTelegramUsername = await ResolveChannelUsernameAsync(db, chatId);
        await db.SaveChangesAsync();

        return new PublishResult(msg.MessageId, null);
    }

    // Resolves a @username for building a t.me link — either directly from the chatId (if it's
    // already an @handle) or by looking up a connected Channel with a matching numeric chat id.
    // Returns null for private channels with no public username (no link can be built there).
    private static async Task<string?> ResolveChannelUsernameAsync(CedarDbContext db, string chatId)
    {
        var trimmed = chatId.Trim();
        if (trimmed.StartsWith('@'))
            return trimmed[1..];

        return long.TryParse(trimmed, out var numericId)
            ? (await db.Channels.FirstOrDefaultAsync(c => c.TelegramChatId == numericId))?.Username
            : null;
    }

    public static void MapPostEndpoints(this WebApplication app)
    {
        app.MapPost("/api/posts/export", async (ExportRequest req, ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await PublishAsync(req.DraftId, req.ChatId, uid, db, bot, cfg, req.Format, req.Language);
            if (!result.Success)
                return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);

            return Results.Ok(new { messageId = result.MessageId, chatId = req.ChatId });
        }).RequireAuthorization();
    }
}
