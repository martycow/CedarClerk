using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server;

public static class PostEndpoints
{ 
    public record ExportRequest(Guid DraftId, string ChatId, string Format = "Html");

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
        string format = "Html")
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == ownerId);
        if (draft is null)
            return new PublishResult(null, "Draft not found", StatusCodes.Status404NotFound);

        if (!bot.IsRunning)
            return new PublishResult(null, "Telegram bot is not running (no token configured)", StatusCodes.Status503ServiceUnavailable);

        var baseUrl = cfg["Cedar:PublicBaseUrl"] ?? Consts.Url;
        var isMarkdown = format == "Markdown";
        var rendered = isMarkdown
            ? CedarToTelegramMarkdownRenderer.Render(draft.CedarJson, baseUrl)
            : CedarToTelegramHtmlRenderer.Render(draft.CedarJson, baseUrl);
        if (string.IsNullOrWhiteSpace(rendered))
            return new PublishResult(null, "Draft is empty");

        if (draft.IsBlogPublished && draft.BlogSlug is not null)
        {
            var blogUrl = $"https://{cfg["Cedar:BlogHost"] ?? BlogEndpoints.DefaultBlogHost}/{draft.BlogSlug}";
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
            var result = await PublishAsync(req.DraftId, req.ChatId, uid, db, bot, cfg, req.Format);
            if (!result.Success)
                return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);

            return Results.Ok(new { messageId = result.MessageId, chatId = req.ChatId });
        }).RequireAuthorization();
    }
}
