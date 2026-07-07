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
    public record ExportRequest(Guid DraftId, string ChatId);

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
        IConfiguration cfg)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == ownerId);
        if (draft is null)
            return new PublishResult(null, "Draft not found", StatusCodes.Status404NotFound);

        if (!bot.IsRunning)
            return new PublishResult(null, "Telegram bot is not running (no token configured)", StatusCodes.Status503ServiceUnavailable);

        var baseUrl = cfg["Cedar:PublicBaseUrl"] ?? Consts.Url;
        var html = CedarToTelegramHtmlRenderer.Render(draft.CedarJson, baseUrl);
        if (string.IsNullOrWhiteSpace(html))
            return new PublishResult(null, "Draft is empty");

        var msg = await bot.Client.SendRichMessage(new ChatId(chatId), new InputRichMessage { Html = html });
        return new PublishResult(msg.MessageId, null);
    }

    public static void MapPostEndpoints(this WebApplication app)
    {
        app.MapPost("/api/posts/export", async (ExportRequest req, ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await PublishAsync(req.DraftId, req.ChatId, uid, db, bot, cfg);
            if (!result.Success)
                return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);

            return Results.Ok(new { messageId = result.MessageId, chatId = req.ChatId });
        }).RequireAuthorization();
    }
}
