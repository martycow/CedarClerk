using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server;

public static class BotKnownChatSync
{
    public static async Task SyncAdminsAsync(CedarDbContext db, TelegramBotClient client, BotKnownChat chat)
    {
        var admins = await client.GetChatAdministrators(new ChatId(chat.TelegramChatId));

        var existing = await db.BotKnownChatAdmins.Where(a => a.BotKnownChatId == chat.Id).ToListAsync();
        db.BotKnownChatAdmins.RemoveRange(existing);
        db.BotKnownChatAdmins.AddRange(admins.Select(a => new BotKnownChatAdmin
        {
            BotKnownChatId = chat.Id,
            TelegramUserId = a.User.Id,
        }));
    }
}
