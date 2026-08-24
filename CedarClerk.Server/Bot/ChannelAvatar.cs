
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Server.Bot;

// The channel's picture, kept as a file rather than as a file_id: a file_id is only valid for as
// long as Telegram says it is, and the blog header has to draw something on a cold start with no
// bot running at all.
public static class ChannelAvatar
{
    // Telegram is asked again only when the copy is a day old. The picture changes about as often
    // as the channel name does, and the header renders on every blog request.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public const string Folder = "channels";

    public static bool IsFresh(Channel channel) =>
        channel.AvatarPath is not null && channel.AvatarFetchedAt is { } at && DateTime.UtcNow - at < MaxAge;

    /// <summary>
    /// Copies the chat photo down if there is one and the copy is stale. Returns true when the row
    /// changed and the caller still has to save.
    /// </summary>
    public static async Task<bool> RefreshAsync(ITelegramBotClient client, Channel channel, string mediaDir, ILogger? logger = null)
    {
        if (IsFresh(channel)) return false;

        try
        {
            var chat = await client.GetChat(new ChatId(channel.TelegramChatId));
            var fileId = chat.Photo?.BigFileId;
            if (fileId is null)
            {
                if (channel.AvatarPath is null && channel.AvatarFetchedAt is not null) return false;
                Delete(mediaDir, channel.AvatarPath);
                channel.AvatarPath = null;
                channel.AvatarFetchedAt = DateTime.UtcNow;
                return true;
            }

            var dir = Path.Combine(mediaDir, Folder);
            Directory.CreateDirectory(dir);
            // The id is the name: one picture per channel, overwritten in place, so a channel that
            // changes its photo every week does not leave a week of orphans behind.
            var relative = $"{Folder}/{channel.Id}.jpg";
            var full = Path.Combine(mediaDir, Folder, $"{channel.Id}.jpg");

            await using (var stream = File.Create(full))
                await client.GetInfoAndDownloadFile(fileId, stream);

            channel.AvatarPath = relative;
            channel.AvatarFetchedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            // A channel the bot was thrown out of, a network blip, a photo-less chat: the old copy
            // stays and the header keeps drawing it. The stamp is moved anyway so a permanently
            // unreachable channel is not re-asked on every request.
            logger?.LogWarning(ex, "Failed to refresh the avatar of channel {ChannelId}", channel.Id);
            channel.AvatarFetchedAt = DateTime.UtcNow;
            return true;
        }
    }

    private static void Delete(string mediaDir, string? relative)
    {
        if (relative is null) return;
        try { File.Delete(Path.Combine(mediaDir, relative.Replace('/', Path.DirectorySeparatorChar))); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
