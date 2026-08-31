using System.Security.Claims;
using CedarClerk.Server.Publishing;
using CedarClerk.Core;
using CedarClerk.Server.Bot;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

using CedarClerk.Localization;

namespace CedarClerk.Server;

public static class ChannelEndpoints
{
    public record ConnectChannelRequest(string ChatId);
    public record KnownChatDto(long TelegramChatId, string Title, string? Username, string Type);
    public record SignaturePatchRequest(string? PostSignature, string? PostSignatureTranslationsJson, string? PostSignatureUrl);

    // Same bound the profile's client-authored JSON blobs get (AuthEndpoints.PreferenceJsonMaxChars):
    // generous, but a misbehaving client cannot grow Channels rows without limit.
    public const int SignatureTranslationsMaxChars = 16_000;

    public static void MapChannelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/channels").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await db.Channels.Where(c => c.OwnerId == uid)
                .Select(c => new
                {
                    c.Id, c.Title, c.TelegramChatId, c.Username,
                    AvatarUrl = c.AvatarPath == null ? null : "/media/" + c.AvatarPath,
                    c.PostSignature, c.PostSignatureTranslationsJson, c.PostSignatureUrl,
                })
                .ToListAsync();
        });

        group.MapPost("/", async (ConnectChannelRequest req, ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot, MediaPaths media, ILogger<Channel> logger) =>
        {
            if (!bot.IsRunning)
                return Results.Json(new { error = ErrorMessages.BotNotRunningNoToken }, statusCode: StatusCodes.Status503ServiceUnavailable);

            ChatFullInfo chat;
            try
            {
                chat = await bot.Client.GetChat(new ChatId(req.ChatId));
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = ErrorMessages.ChannelNotFoundOrNoAccess });
            }

            var member = await bot.Client.GetChatMember(chat.Id, bot.Me.Id);

            if (chat.Type is not (ChatType.Channel or ChatType.Group or ChatType.Supergroup))
                return Results.BadRequest(new { error = ErrorMessages.UnsupportedChatType });

            if (!BotChatAccess.CanPost(chat.Type, member))
                return Results.BadRequest(new { error = chat.Type == ChatType.Channel
                    ? "Bot must have an Admin with the right to send messages OR Creator."
                    : "Bot must be an Admin or Creator of the Group/Supergroup." });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var account = await db.Users.FirstAsync(u => u.Id == uid);

            // T-359 (audit finding 1) — the bot being an admin is not enough: the *caller* must
            // be an admin/creator of this chat too, or any account could claim a channel the
            // shared bot was added to by its real owner. Telegram's membership is the authority,
            // and the caller has to have linked their Telegram for us to ask.
            if (account.TelegramUserId is not { } callerTgId)
                return Results.BadRequest(new { error = ErrorMessages.LinkTelegramBeforeChannel });
            ChatMember callerMember;
            try { callerMember = await bot.Client.GetChatMember(chat.Id, callerTgId); }
            catch (Exception) { return Results.BadRequest(new { error = ErrorMessages.NotChannelAdmin }); }
            if (!BotChatAccess.IsAdminOrCreator(callerMember))
                return Results.BadRequest(new { error = ErrorMessages.NotChannelAdmin });

            var tier = SubscriptionPlanHelper.CheckPlanExpiration(account.PlanTier, account.PlanExpiresAt, DateTime.UtcNow);
            var channelCount = await db.Channels.CountAsync(c => c.OwnerId == uid);
            if (!PlanLimitations.CanConnectAnotherChannel(tier, channelCount))
                return Results.Json(new { error = $"Your plan allows {PlanLimitations.MaxChannels(tier)} connected channel(s). Upgrade for more." }, statusCode: StatusCodes.Status403Forbidden);

            // Anti channel-cycling on Free: after deleting a channel, a DIFFERENT one can only be
            // connected after the cooldown (reconnecting the same channel is always fine).
            if (tier == PlanTiers.Free
                && account.FreeChannelCooldownUntil is { } cooldown && cooldown > DateTime.UtcNow
                && account.LastDeletedTelegramChatId != chat.Id)
                return Results.Json(new { error = $"On the Free plan you can switch to a different channel after {cooldown:d MMM yyyy}. Upgrade to Pro to connect more channels." }, statusCode: StatusCodes.Status403Forbidden);

            var channel = new Channel
            {
                Title = chat.Title ?? chat.Username ?? req.ChatId,
                TelegramChatId = chat.Id,
                Username = chat.Username,
                OwnerId = uid,
            };
            db.Channels.Add(channel);
            // T-085 — the general row publishing runs against, kept in step with the channel here
            // rather than only at startup, so a channel connected today is publishable today.
            await TelegramTargetProjection.EnsureAsync(db, channel);

            // Take the first snapshot right away so the stats UI isn't empty until the next 4 AM job run.
            try
            {
                var count = await bot.Client.GetChatMemberCount(new ChatId(channel.TelegramChatId));
                db.ChannelStatSnapshots.Add(new ChannelStatSnapshot { ChannelId = channel.Id, MemberCount = count });
                await ChannelAvatar.RefreshAsync(bot.Client, channel, media.Dir, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to take initial member-count snapshot for channel {ChannelId}", channel.Id);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { channel.Id, channel.Title, channel.TelegramChatId, channel.Username });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == uid);
            if (channel is null) return Results.NotFound();

            // Free tier: deleting starts the switch-cooldown so the 1-channel limit can't be
            // bypassed by delete→connect cycling. Reconnecting this same channel stays allowed.
            var account = await db.Users.FirstAsync(u => u.Id == uid);
            if (SubscriptionPlanHelper.CheckPlanExpiration(account.PlanTier, account.PlanExpiresAt, DateTime.UtcNow) == PlanTiers.Free)
            {
                account.FreeChannelCooldownUntil = DateTime.UtcNow + PlanLimitations.FreeChannelSwitchCooldown;
                account.LastDeletedTelegramChatId = channel.TelegramChatId;
            }

            // Deactivated, not deleted (T-085): the target row carries LastPublishedAt and is the
            // remaining answer to "where did this post go" once the channel row is gone.
            await TelegramTargetProjection.DeactivateAsync(db, channel);
            await ChannelDeletion.CascadeAsync(db, uid, channel.Id);
            db.Channels.Remove(channel);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/stats", async (Guid id, ClaimsPrincipal user, CedarDbContext db, int days = 30) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Channels.AnyAsync(c => c.Id == id && c.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var snapshots = await db.ChannelStatSnapshots
                .Where(s => s.ChannelId == id)
                .OrderByDescending(s => s.TakenAt)
                .Take(days)
                .OrderBy(s => s.TakenAt)
                .Select(s => new
                {
                    s.TakenAt, s.MemberCount, s.ViewCount,
                    // ADR-205 — a channel's likes and comments are the channel's. The two blog-
                    // attributed columns stay in the table for the history they already hold and
                    // are not served under a Telegram source's name any more.
                    LikeCount = s.TelegramReactionCount,
                    CommentCount = s.TelegramCommentCount,
                })
                .ToListAsync();

            var now = DateTime.UtcNow;

            // Publish-event markers for the growth chart (T-244): when something went out, on the
            // same time axis the snapshots draw. Dates only — the chart needs positions, not posts.
            var since = now.AddDays(-days);
            var publishDates = await db.ChannelPosts
                .Where(p => p.ChannelId == id && p.OwnerId == uid && p.PublishedAt >= since)
                .OrderBy(p => p.PublishedAt)
                .Select(p => p.PublishedAt)
                .ToListAsync();

            var current = snapshots.Count > 0 ? snapshots[^1].MemberCount : (int?)null;
            var points = snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.MemberCount)).ToList();
            var deltaWeek = ChannelStatsCalculator.DeltaOverDays(points, 7, now);

            var currentViews = snapshots.Count > 0 ? snapshots[^1].ViewCount : (int?)null;
            var currentLikes = snapshots.Count > 0 ? snapshots[^1].LikeCount : (int?)null;
            var currentComments = snapshots.Count > 0 ? snapshots[^1].CommentCount : (int?)null;
            var deltaWeekViews = ChannelStatsCalculator.DeltaOverDays(snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.ViewCount)).ToList(), 7, now);
            var deltaWeekLikes = ChannelStatsCalculator.DeltaOverDays(snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.LikeCount)).ToList(), 7, now);
            var deltaWeekComments = ChannelStatsCalculator.DeltaOverDays(snapshots.Select(s => new ChannelStatPoint(s.TakenAt, s.CommentCount)).ToList(), 7, now);

            return Results.Ok(new
            {
                current, deltaWeek,
                currentViews, deltaWeekViews,
                currentLikes, deltaWeekLikes,
                currentComments, deltaWeekComments,
                snapshots,
                publishDates,
            });
        });

        // Wave 2 item 11 — the channel's own signature trio, replacing the owner-level one at send
        // time when PostSignature is non-null (TelegramPublishTarget.PickSignatureSource). A null
        // PostSignature clears the whole override: the trio moves together, so no channel keeps
        // stray translations or a URL under the owner's wording.
        group.MapPatch("/{id:guid}/signature", async (Guid id, SignaturePatchRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var (status, error, channel) = await ApplySignatureAsync(db, uid, id, req);
            return status switch
            {
                StatusCodes.Status200OK => Results.Ok(new
                {
                    postSignature = channel!.PostSignature,
                    postSignatureTranslationsJson = channel.PostSignatureTranslationsJson,
                    postSignatureUrl = channel.PostSignatureUrl,
                }),
                StatusCodes.Status404NotFound => Results.NotFound(),
                _ => Results.Json(new { error }, statusCode: status),
            };
        });

        // Best-time hints (no ML, item 12): which UTC hours this channel's posts have historically
        // earned the most engagement in. Hours with fewer than two posts say nothing; a young
        // channel honestly answers with an empty list and the UI shows no hint at all.
        group.MapGet("/{id:guid}/best-times", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var owns = await db.Channels.AnyAsync(c => c.Id == id && c.OwnerId == uid);
            if (!owns) return Results.NotFound();

            var posts = await db.ChannelPosts
                .Where(p => p.ChannelId == id && p.OwnerId == uid)
                .Select(p => new PublishedPostSample(p.PublishedAt, p.ReactionCount, p.CommentCount))
                .ToListAsync();

            return Results.Ok(BestTimeCalculator.Compute(posts));
        });

        // Chats the bot is known to be in (tracked live from Telegram's my_chat_member updates —
        // see TelegramBotService) that aren't already connected by anyone. Scoped to chats where
        // the REQUESTING user's linked Telegram identity is actually an admin (via the
        // BotKnownChatAdmin cache) — the bot is shared across every Cedar Clerk account, so without
        // this filter everyone would see every chat the bot is in, including other users' channels.
        // Users who haven't linked a Telegram account (Settings > Account) get an empty list, since
        // there's no identity to scope against — they can still connect by typing @username/id.
        group.MapGet("/known", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var telegramUserId = await db.Users.Where(u => u.Id == uid).Select(u => u.TelegramUserId).FirstAsync();
            if (telegramUserId is null) return new List<KnownChatDto>();

            var connectedIds = db.Channels.Select(c => c.TelegramChatId);
            return await db.BotKnownChats
                .Where(k => k.BotCanPost && !connectedIds.Contains(k.TelegramChatId)
                    && db.BotKnownChatAdmins.Any(a => a.BotKnownChatId == k.Id && a.TelegramUserId == telegramUserId))
                .OrderByDescending(k => k.LastSeenAt)
                .Select(k => new KnownChatDto(k.TelegramChatId, k.Title, k.Username, k.Type))
                .ToListAsync();
        });

        // Re-checks the bot's current status and admin list in every known chat (title/username/
        // posting rights/admins may have changed since we last saw an update for it). Does NOT
        // discover chats the bot was already in before this feature started tracking
        // my_chat_member updates — for those, connecting still works the old way (type
        // @username/chat id manually).
        group.MapPost("/refresh-known-chats", async (ClaimsPrincipal user, CedarDbContext db, TelegramBotService bot, MediaPaths media, ILogger<Channel> logger) =>
        {
            if (!bot.IsRunning)
                return Results.Json(new { error = ErrorMessages.BotNotRunningNoToken }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var telegramUserId = await db.Users.Where(u => u.Id == uid).Select(u => u.TelegramUserId).FirstAsync();

            // The connected channels' pictures ride along: this is the button a reader presses
            // when the list looks stale, and a stale picture is one of the ways it looks stale.
            // db.Channels is tenant-filtered, so this is already only the caller's own channels.
            foreach (var channel in await db.Channels.ToListAsync())
            {
                channel.AvatarFetchedAt = null;
                await ChannelAvatar.RefreshAsync(bot.Client, channel, media.Dir, logger);
            }

            // T-359 (audit finding 4) — was a walk of every BotKnownChat by any authenticated
            // account, making 2-3 shared-token Bot API calls per row (a flood-limit lever) and
            // latching other owners' rows to BotCanPost=false on a transient failure. Bounded to
            // the chats where the caller's own linked Telegram is an admin; unlinked → nothing.
            var known = telegramUserId is null
                ? new List<BotKnownChat>()
                : await db.BotKnownChats
                    .Where(k => db.BotKnownChatAdmins.Any(a => a.BotKnownChatId == k.Id && a.TelegramUserId == telegramUserId))
                    .ToListAsync();
            foreach (var chat in known)
            {
                try
                {
                    var fullChat = await bot.Client.GetChat(new ChatId(chat.TelegramChatId));
                    var member = await bot.Client.GetChatMember(chat.TelegramChatId, bot.Me.Id);

                    chat.Title = fullChat.Title ?? fullChat.Username ?? chat.Title;
                    chat.Username = fullChat.Username;
                    chat.Type = fullChat.Type.ToString();
                    chat.BotCanPost = BotChatAccess.CanPost(fullChat.Type, member);
                    chat.LastSeenAt = DateTime.UtcNow;

                    if (chat.BotCanPost)
                        await BotKnownChatSync.SyncAdminsAsync(db, bot.Client, chat);
                }
                catch (Exception ex)
                {
                    // Bot was removed from the chat, chat was deleted, etc. — leave the stale row
                    // as not-postable rather than deleting the discovery history.
                    chat.BotCanPost = false;
                    logger.LogWarning(ex, "Failed to refresh known chat {ChatId}", chat.TelegramChatId);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { refreshed = known.Count });
        });
    }

    public static async Task<(int Status, string? Error, Channel? Channel)> ApplySignatureAsync(
        CedarDbContext db, string uid, Guid channelId, SignaturePatchRequest req)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId && c.OwnerId == uid);
        if (channel is null) return (StatusCodes.Status404NotFound, null, null);

        if (req.PostSignatureTranslationsJson is { Length: > SignatureTranslationsMaxChars })
            return (StatusCodes.Status400BadRequest, ErrorMessages.SignatureTranslationsTooLarge, null);

        var signature = string.IsNullOrWhiteSpace(req.PostSignature) ? null : req.PostSignature.Trim();

        if (signature is null)
        {
            // Clearing the override is available on every plan — it restores the default behaviour.
            channel.PostSignature = null;
            channel.PostSignatureTranslationsJson = null;
            channel.PostSignatureUrl = null;
        }
        else
        {
            // The same save-time gate the owner-level signature has (AuthEndpoints /signature);
            // send-time gating via PlanLimitations.ResolveSignature stays unchanged on top.
            var account = await db.Users.FirstAsync(u => u.Id == uid);
            var plan = SubscriptionPlanHelper.CheckPlanExpiration(account.PlanTier, account.PlanExpiresAt, DateTime.UtcNow);
            if (!PlanLimitations.HasCustomSignature(plan))
                return (StatusCodes.Status403Forbidden, ErrorMessages.SignatureIsPro, null);

            channel.PostSignature = signature;
            channel.PostSignatureTranslationsJson = NormalizeTranslations(req.PostSignatureTranslationsJson);
            channel.PostSignatureUrl = string.IsNullOrWhiteSpace(req.PostSignatureUrl) ? null : req.PostSignatureUrl.Trim();
        }

        await db.SaveChangesAsync();
        return (StatusCodes.Status200OK, null, channel);
    }

    /// <summary>Rebuilds the translations blob through LocalizedTextMap, so what is stored is
    /// exactly what sends will read: blank values dropped, malformed JSON degraded to null.</summary>
    public static string? NormalizeTranslations(string? translationsJson)
    {
        string? normalized = null;
        foreach (var (lang, text) in LocalizedTextMap.All(translationsJson))
            normalized = LocalizedTextMap.Set(normalized, lang, text);
        return normalized;
    }
}

public sealed record PublishedPostSample(DateTime PublishedAtUtc, int Reactions, int Comments);

public sealed record BestTimeSlot(int Hour, int Posts, double AvgReactions, double AvgComments);

public static class BestTimeCalculator
{
    public const int MinPostsPerHour = 2;

    public static IReadOnlyList<BestTimeSlot> Compute(IReadOnlyList<PublishedPostSample> posts) =>
        posts.GroupBy(p => p.PublishedAtUtc.Hour)
            .Where(g => g.Count() >= MinPostsPerHour)
            .Select(g => new BestTimeSlot(g.Key, g.Count(),
                g.Average(p => (double)p.Reactions), g.Average(p => (double)p.Comments)))
            .OrderByDescending(s => s.AvgReactions + s.AvgComments)
            .ThenBy(s => s.Hour)
            .ToList();
}
