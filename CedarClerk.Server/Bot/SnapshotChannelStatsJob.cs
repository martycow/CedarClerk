using CedarClerk.Server.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server.Bot;

[DisallowConcurrentExecution]
public class SnapshotChannelStatsJob(CedarDbContext db, TelegramBotService bot, ILogger<SnapshotChannelStatsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!bot.IsRunning) return;

        var channels = await db.Channels.ToListAsync();
        var now = DateTime.UtcNow;

        foreach (var channel in channels)
        {
            try
            {
                var count = await bot.Client.GetChatMemberCount(new ChatId(channel.TelegramChatId));
                db.ChannelStatSnapshots.Add(new ChannelStatSnapshot
                {
                    ChannelId = channel.Id,
                    MemberCount = count,
                    TakenAt = now,
                });
            }
            catch (Exception ex)
            {
                // One channel failing (bot removed as admin, channel deleted, etc.) must not stop the rest.
                logger.LogWarning(ex, "Failed to snapshot member count for channel {ChannelId} ({Title})", channel.Id, channel.Title);
            }
        }

        if (channels.Count > 0)
            await db.SaveChangesAsync();
    }
}
