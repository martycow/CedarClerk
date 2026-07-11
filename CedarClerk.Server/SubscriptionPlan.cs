using CedarClerk.Core;
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
            var username = trimmed[1..];
            return await db.Channels.FirstOrDefaultAsync(c =>
                c.OwnerId == userId && 
                c.Username != null && 
                c.Username.Equals(username, StringComparison.CurrentCultureIgnoreCase));
        }
        return long.TryParse(trimmed, out var numericId)
            ? await db.Channels.FirstOrDefaultAsync(c => c.OwnerId == userId && c.TelegramChatId == numericId)
            : null;
    }
}
