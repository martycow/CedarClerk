using System.Diagnostics;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CedarClerk.Server.Bot;

public class TelegramBotService(IConfiguration cfg, ILogger<TelegramBotService> logger, IServiceScopeFactory scopeFactory) : BackgroundService
{
    public TelegramBotClient Client => _client ?? throw new InvalidOperationException("Bot is not started");
    public bool IsRunning => _client is not null;
    public User Me { get; private set; } = default!;
    
    private TelegramBotClient? _client;
    private Stopwatch _sw = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var token = cfg[Consts.Telegram.BotTokenCfg];
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Cedar:BotToken not set — bot is disabled");
            return;
        }

        var client = new TelegramBotClient(token, cancellationToken: ct);
        try
        {
            Me = await client.GetMe(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // To prevent the whole server crash
            logger.LogError(ex, "Failed to connect to Telegram — bot is disabled for this run");
            return;
        }

        logger.LogInformation("Bot @{Username} (id {Id}) is running", Me.Username, Me.Id);
        
        _sw.Start();

        client.OnError += OnError;
        client.OnMessage += OnMessageReceived;
        client.OnUpdate += OnUpdate;
        _client = client;

        await Task.Delay(Timeout.Infinite, ct);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client == null)
            return base.StopAsync(cancellationToken);
        
        _sw.Stop();
        
        _client.OnError -= OnError;
        _client.OnMessage -= OnMessageReceived;
        _client.OnUpdate -= OnUpdate;
        return base.StopAsync(cancellationToken);
    }

    private Task OnError(Exception exception, HandleErrorSource source)
    {
        logger.LogError(exception, "Bot error from {Source}", source);
        return Task.CompletedTask;
    }
    
    private Task OnMessageReceived(Message message, UpdateType type)
    {
        return SafeHandle(() => ProcessMessage(message), "message");
    }
    
    private Task OnUpdate(Update update)
    {
        return SafeHandle(() => OnUpdateReceived(update), "update");
    }

    private async Task SafeHandle(Func<Task> handler, string kind)
    {
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in {Kind} handler", kind);
        }
    }

    private async Task ProcessMessage(Message message)
    {
        if (message.SuccessfulPayment is { } payment)
        {
            var payloadParts = payment.InvoicePayload.Split(':', 2);
            var (plan, userId) = payloadParts.Length == 2
                ? (payloadParts[0], payloadParts[1])
                : (Consts.Plans.Pro, payment.InvoicePayload); // legacy payload without plan

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            if (await db.Payments.AnyAsync(p => p.ExternalId == payment.TelegramPaymentChargeId))
                return; // duplicate update delivery

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
            {
                logger.LogWarning("SuccessfulPayment with unknown payload {Payload}", payment.InvoicePayload);
                return;
            }

            var error = SubscriptionPlan.ApplyPurchase(user, plan, DateTime.UtcNow);
            if (error is not null)
            {
                logger.LogWarning("Stars payment for user {UserId} not applied: {Error}", user.Id, error);
                await Client.SendMessage(message.Chat, $"Payment received, but: {error}. Please contact support.");
                return;
            }

            db.Payments.Add(new Payment
            {
                OwnerId = user.Id,
                Provider = "telegram-stars",
                Plan = plan,
                ExternalId = payment.TelegramPaymentChargeId,
                Amount = payment.TotalAmount,
                Currency = payment.Currency, // "XTR"
            });
            
            await db.SaveChangesAsync();
            logger.LogInformation("Telegram Stars payment — user {UserId} on plan {Plan} until {ExpiresAt}", user.Id, plan, user.PlanExpiresAt);
            await Client.SendMessage(message.Chat, $"Payment received — your plan is active until {user.PlanExpiresAt:d MMM yyyy} (auto-renews for subscriptions). Enjoy Cedar Clerk!");
            return;
        }

        if (message.Text is not { } text) 
            return;

        // Processing pre-defined Bot commands sent by User via message, like "/start"
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) 
            return;
        
        var (command, arg) = (parts[0], parts.Length > 1 ? parts[1] : "");

        Func<Chat, string, Task>? handler = command switch
        {
            Consts.PreDefinedCommands.Start => HandleStartCommand,
            _ => null
        };

        if (handler is null)
        {
            logger.LogInformation("Unknown command '{Command}' from chat {ChatId}", command, message.Chat.Id);
            return;
        }
        await handler(message.Chat, arg);
    }

    private async Task HandleStartCommand(Chat chat, string arg)
    {
        var startMsg = $"Cedar Clerk Bot v{Consts.CurrentVersion}\n" +
                       $"Current Server UTC Time: {DateTime.UtcNow}\n" +
                       $"Time elapsed since start: {_sw.Elapsed}\n\n" +
                       $"Add me to Channels, Groups and Supergroups.\n\n\n" +
                       $"Only for Channels: and assign me as an Administrator with the right to post messages)";
        
        await Client.SendMessage(chat, startMsg);
    }
    
    private async Task OnUpdateReceived(Update update)
    {
        // Telegram asks to confirm before charging the user with Stars. Nothing to validate on our side, so we approve.
        if (update.PreCheckoutQuery is { } pcq)
        {
            await Client.AnswerPreCheckoutQuery(pcq.Id);
            return;
        }

        // On Bot Update (after added/removed, rights changed etc) we put in he DB the info about the chat and the bot's rights
        if (update.MyChatMember is not { } cm) 
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var known = await db.BotKnownChats.FirstOrDefaultAsync(k => k.TelegramChatId == cm.Chat.Id);
        if (known is null)
        {
            known = new BotKnownChat { TelegramChatId = cm.Chat.Id };
            db.BotKnownChats.Add(known);
        }

        known.Title = cm.Chat.Title ?? cm.Chat.Username ?? known.Title;
        known.Username = cm.Chat.Username;
        known.Type = cm.Chat.Type.ToString();
        known.BotCanPost = BotChatAccess.CanPost(cm.Chat.Type, cm.NewChatMember);
        known.LastSeenAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        if (known.BotCanPost)
        {
            try
            {
                await BotKnownChatSync.SyncAdminsAsync(db, Client, known);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync admin list for newly known chat {ChatId}", known.TelegramChatId);
            }
        }
    }
}