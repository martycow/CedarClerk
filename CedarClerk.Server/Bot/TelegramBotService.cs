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
    private CancellationToken _stopToken;
    private DateTime _lastPollErrorAt = DateTime.MinValue;
    private int _pollErrorStreak;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _stopToken = ct;
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

        _client = client;

        // ADR-205 — the reaction updates are why this is StartReceiving and not the OnMessage/
        // OnUpdate events: Telegram leaves MessageReaction and MessageReactionCount out of the
        // default set and only sends them to a bot that names them here, and the event API has
        // nowhere to name them. Every other kind on the list is one this service already handled,
        // spelled out rather than inherited — an allow-list that is written down is the only kind
        // that can be read.
        client.StartReceiving(
            (_, update, _) => OnUpdate(update),
            (_, exception, source, _) => OnError(exception, source),
            new ReceiverOptions
            {
                AllowedUpdates =
                [
                    UpdateType.Message,
                    UpdateType.MyChatMember,
                    UpdateType.PreCheckoutQuery,
                    UpdateType.MessageReactionCount,
                ],
            },
            ct);

        await Task.Delay(Timeout.Infinite, ct);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client == null)
            return base.StopAsync(cancellationToken);
        
        _sw.Stop();
        // The polling loop stops with the token StartReceiving was handed; there is no handler to
        // detach any more.
        return base.StopAsync(cancellationToken);
    }

    private async Task OnError(Exception exception, HandleErrorSource source)
    {
        if (source != HandleErrorSource.PollingError)
        {
            logger.LogError(exception, "Bot error from {Source}", source);
            return;
        }

        // The polling loop awaits this handler before retrying getUpdates, so the delay below is the retry backoff.
        // Without it a dead network floods the logs with a full stack trace per instant retry (11 GB in two days, 02-04.08.2026).
        _pollErrorStreak = DateTime.UtcNow - _lastPollErrorAt > TimeSpan.FromMinutes(2) ? 1 : _pollErrorStreak + 1;
        _lastPollErrorAt = DateTime.UtcNow;

        if (_pollErrorStreak == 1)
            logger.LogError(exception, "Bot error from {Source}", source);
        else if (_pollErrorStreak % 100 == 0)
            logger.LogError("Polling still failing ({Count} in a row): {Message}", _pollErrorStreak, exception.Message);

        var seconds = Math.Min(60, 1 << Math.Min(6, _pollErrorStreak));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), _stopToken);
        }
        catch (OperationCanceledException)
        {
        }
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

            // ADR-092 — a credit purchase, not a plan. A pack by id, or ADR-189's custom amount,
            // which carries the count in the payload because there is no pack to look up.
            if (plan.StartsWith(Consts.Plans.CreditPackPrefix, StringComparison.Ordinal)
                && CreditsOfPayload(plan[Consts.Plans.CreditPackPrefix.Length..]) is { } purchasedCredits)
            {
                await CreditWallet.GrantAsync(db, user.Id, purchasedCredits, CreditReasons.Purchase, payment.TelegramPaymentChargeId);
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
                logger.LogInformation("Telegram Stars credits purchase — user {UserId}, {Credits} credits", user.Id, purchasedCredits);
                await Client.SendMessage(message.Chat, $"Payment received — {purchasedCredits} credits added to your Cedar Clerk balance.");
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

    // ADR-205 — a comment on a channel post is a message in the linked discussion group replying to
    // the post's automatic forward. Every other message falls straight through.
    private async Task CountCommentAsync(Message message)
    {
        if (message.ReplyToMessage is null) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        if (await TelegramEngagement.ApplyCommentAsync(db, message))
            await db.SaveChangesAsync();
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
        // Dispatched here rather than by an OnMessage handler since ADR-205 moved the service off
        // the event API; a message is one update kind among the four the allow-list names.
        if (update.Message is { } message)
        {
            await ProcessMessage(message);
            await CountCommentAsync(message);
            return;
        }

        // Telegram asks to confirm before charging the user with Stars. Nothing to validate on our side, so we approve.
        if (update.PreCheckoutQuery is { } pcq)
        {
            await Client.AnswerPreCheckoutQuery(pcq.Id);
            return;
        }

        // ADR-205 — the anonymous reaction tally on a channel post. The update carries the whole
        // current count, so nothing here adds up deltas.
        if (update.MessageReactionCount is { } reactions)
        {
            using var reactionScope = scopeFactory.CreateScope();
            var reactionDb = reactionScope.ServiceProvider.GetRequiredService<CedarDbContext>();
            if (await TelegramEngagement.ApplyReactionsAsync(reactionDb, reactions))
                await reactionDb.SaveChangesAsync();
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

    /// <summary>
    /// How many credits an invoice payload bought: a pack by id, or ADR-189's custom amount, which
    /// carries the count itself because there is no pack to look up. Null when it is neither.
    /// </summary>
    private static int? CreditsOfPayload(string id) =>
        CreditPacks.Find(id) is { } pack ? pack.Credits
        : id.StartsWith(Consts.Plans.CustomCreditsPrefix, StringComparison.Ordinal)
          && int.TryParse(id[Consts.Plans.CustomCreditsPrefix.Length..], out var credits)
            ? CreditPacks.ValidCustom(credits)
            : null;
}