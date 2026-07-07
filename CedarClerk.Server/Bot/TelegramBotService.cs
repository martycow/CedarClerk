using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server.Bot;

public class TelegramBotService(IConfiguration cfg, ILogger<TelegramBotService> logger) : BackgroundService
{
    private TelegramBotClient? _client;
    public TelegramBotClient Client => _client ?? throw new InvalidOperationException("Bot is not started");

    public bool IsRunning => _client is not null;

    public User Me { get; private set; } = default!;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var token = cfg["Cedar:BotToken"];
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Cedar:BotToken not set — bot is disabled");
            return;
        }

        _client = new TelegramBotClient(token, cancellationToken: ct);
        Me = await _client.GetMe(ct);
        logger.LogInformation("Bot @{Username} (id {Id}) is running", Me.Username, Me.Id);

        _client.OnError += (ex, source) =>
        {
            logger.LogError(ex, "Bot error from {Source}", source);
            return Task.CompletedTask;
        };
        _client.OnMessage += (msg, type) => SafeHandle(() => OnMessage(msg), "message");
        
        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task SafeHandle(Func<Task> handler, string kind)
    {
        try { await handler(); }
        catch (Exception ex) { logger.LogError(ex, "Unhandled error in {Kind} handler", kind); }
    }

    private async Task OnMessage(Message message)
    {
        if (message.Text is not { } text) return;

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var (command, arg) = (parts[0], parts.Length > 1 ? parts[1] : "");

        Func<Chat, string, Task>? handler = command switch
        {
            "/start" => HandleStart,
            "/richtest" => HandleRichTest,
            _ => null
        };

        if (handler is null)
        {
            logger.LogInformation("Unknown command '{Command}' from chat {ChatId}", command, message.Chat.Id);
            return;
        }
        await handler(message.Chat, arg);
    }

    private async Task HandleStart(Chat chat, string arg)
    {
        await Client.SendMessage(chat, $"Cedar Clerk Bot. Current time (UTC): {DateTime.UtcNow})");
    }

    private async Task HandleRichTest(Chat chat, string arg)
    {
        await Client.SendRichMessage(chat.Id, new InputRichMessage
        {
            Markdown = RickTextFixture.Text
        });
    }
}