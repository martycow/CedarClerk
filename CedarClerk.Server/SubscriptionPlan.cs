using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Analytics;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// For maintaining subscription plans
/// </summary>
public static class SubscriptionPlan
{
    public static async Task<PlanTiers> EffectiveTierAsync(CedarDbContext db, string userId)
    {
        var u = await db.Users.Where(x => x.Id == userId)
            .Select(x => new { x.PlanTier, x.PlanExpiresAt })
            .FirstAsync();
        return SubscriptionPlanHelper.CheckPlanExpiration(u.PlanTier, u.PlanExpiresAt, DateTime.UtcNow);
    }

    public static string? ApplyPurchase(ApplicationUser user, string plan, DateTime nowUtc)
    {
        if (!SubscriptionPlanHelper.IsValid(plan))
            return $"Unknown plan '{plan}'";

        if (plan == Consts.Plans.Trial)
        {
            if (user.TrialUsedAt is not null)
                return "Trial has already been used on this account";
            
            user.PlanTier = PlanTiers.ProPlus;
            user.PlanExpiresAt = nowUtc + SubscriptionPlanHelper.TrialPeriod;
            user.TrialUsedAt = nowUtc;
            return null;
        }

        var tier = SubscriptionPlanHelper.TierOf(plan);
        user.PlanExpiresAt = SubscriptionPlanHelper.NextExpiry(user.PlanTier, user.PlanExpiresAt, tier, nowUtc);
        user.PlanTier = tier;
        return null;
    }

    public enum AiCharge { Ok, DailyLimit, NoCredits }

    /// <summary>
    /// T-152 — one gate for every AI call: the abuse ceiling first, then the wallet. Charged up
    /// front, the way the daily quota always was — the provider bills for a started call either
    /// way. The ledger ref is a fresh guid: an interactive call has no retry to be idempotent for,
    /// and a reused ref would make every later call free.
    /// </summary>
    public static async Task<AiCharge> TryChargeAiAsync(CedarDbContext db, string userId, int credits)
    {
        if (!await TryConsumeAiCallAsync(db, userId)) return AiCharge.DailyLimit;
        return await CreditWallet.TryChargeAsync(db, userId, credits, CreditReasons.Ai, Guid.NewGuid().ToString("N"))
            ? AiCharge.Ok
            : AiCharge.NoCredits;
    }

    /// <summary>
    /// T-361 — hands a credit back when an AI job fails for our reason (provider error, timeout).
    /// A fresh ref, so it is a real second ledger movement the user can see, not an idempotent
    /// reversal of the original charge. The daily-count is deliberately NOT decremented: the abuse
    /// ceiling is about attempts, and a failed attempt still hit the provider.
    /// </summary>
    public static async Task RefundAiAsync(CedarDbContext db, string userId, int credits) =>
        await CreditWallet.GrantAsync(db, userId, credits, CreditReasons.AiRefund, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The synchronous counterpart of the jobs' onFailure hook: hand the credit back and answer
    /// the failure in one step, so a call site cannot refund without failing or fail without
    /// refunding. Used by the 1-credit paths, which have no job to hang a callback on.
    /// </summary>
    public static async Task<IResult> RefundAiAndFailAsync(
        CedarDbContext db, string userId, int credits, string error,
        int statusCode = StatusCodes.Status502BadGateway)
    {
        await RefundAiAsync(db, userId, credits);
        return Results.Json(new { error }, statusCode: statusCode);
    }

    /// <summary>
    /// The refusal to return, or null when the call is paid for. Every AI call in the app comes
    /// through here, which is why <c>ai_used</c> is recorded here rather than at six call sites —
    /// and why a refusal is recorded too: "asked and was turned away" is the half of AI spend that
    /// says whether the limits are set right (<c>docs/product/METRICS.md</c>).
    /// </summary>
    public static async Task<IResult?> ChargeAiOrRefuseAsync(
        CedarDbContext db, string userId, int credits, ProductAnalytics analytics, string kind)
    {
        var charge = await TryChargeAiAsync(db, userId, credits);
        analytics.Track(userId, Consts.Analytics.Events.AiUsed, new()
        {
            ["kind"] = kind,
            ["credits"] = credits,
            ["outcome"] = charge switch
            {
                AiCharge.DailyLimit => "daily_limit",
                AiCharge.NoCredits => "no_credits",
                _ => "charged",
            },
        });

        return charge switch
        {
            AiCharge.DailyLimit => Results.Json(
                new { error = ErrorMessages.AiDailyLimitReached(PlanLimitations.AiDailyLimit) },
                statusCode: StatusCodes.Status429TooManyRequests),
            AiCharge.NoCredits => Results.Json(
                new { error = ErrorMessages.NotEnoughCreditsForAi },
                statusCode: StatusCodes.Status402PaymentRequired),
            _ => null,
        };
    }

    public static async Task<bool> TryConsumeAiCallAsync(CedarDbContext db, string userId)
    {
        var today = DateTime.UtcNow.Date;
        var usage = await db.AiUsages.FirstOrDefaultAsync(a => a.OwnerId == userId && a.Day == today);
        if (usage is null)
        {
            db.AiUsages.Add(new AiUsage
            {
                OwnerId = userId, 
                Day = today, 
                Count = 1
            });
            return true;
        }
        
        if (usage.Count >= PlanLimitations.AiDailyLimit)
            return false;
        
        usage.Count++;
        return true;
    }

    public static async Task<Channel?> ResolveOwnedChannelAsync(CedarDbContext db, string userId, string chatId)
    {
        var trimmed = chatId.Trim();
        if (trimmed.StartsWith('@'))
        {
            var username = trimmed[1..].ToLower();
            return await db.Channels.FirstOrDefaultAsync(c =>
                c.OwnerId == userId &&
                c.Username != null &&
                c.Username.ToLower() == username);
        }
        return long.TryParse(trimmed, out var numericId)
            ? await db.Channels.FirstOrDefaultAsync(c => c.OwnerId == userId && c.TelegramChatId == numericId)
            : null;
    }
}
