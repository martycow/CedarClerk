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

    public static void MapPostEndpoints(this WebApplication app)
    {
        app.MapPost("/api/posts/export", async (ExportRequest req, ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (draft is null) 
                return Results.NotFound(new { error = "Draft not found" });

            if (!bot.IsRunning)
                return Results.Problem("Telegram bot is not running (no token configured)", statusCode: StatusCodes.Status503ServiceUnavailable);
            
            var html = CedarToTelegramHtmlRenderer.Render(draft.CedarJson);
            if (string.IsNullOrWhiteSpace(html))
                return Results.BadRequest(new { error = "Draft is empty" });

            var msg = await bot.Client.SendRichMessage(new ChatId(req.ChatId), new InputRichMessage { Html = html });
            return Results.Ok(new { messageId = msg.MessageId, chatId = req.ChatId });
        }).RequireAuthorization();
    }
}