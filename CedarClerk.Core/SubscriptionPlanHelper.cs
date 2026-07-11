namespace CedarClerk.Core;

/// <summary>
/// 
/// </summary>
public static class SubscriptionPlanHelper
{
    public static readonly TimeSpan SubscriptionPeriod = TimeSpan.FromDays(30);
    public static readonly TimeSpan TrialPeriod = TimeSpan.FromDays(7);
    
    /// <summary>
    /// Grace covers renewal-webhook lag so auto-renewed users never see a flicker to Free
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(2);

    public static bool IsValid(string plan) => plan is 
        Consts.Plans.Free or 
        Consts.Plans.Pro or 
        Consts.Plans.ProPlus or 
        Consts.Plans.Trial;

    public static PlanTiers TierOf(string plan)
    {
        return plan switch
        {
            Consts.Plans.Pro => PlanTiers.Pro,
            Consts.Plans.ProPlus or Consts.Plans.Trial => PlanTiers.ProPlus,
            _ => throw new ArgumentException($"Unknown plan '{plan}'"),
        };
    }
    
    public static int PriceUsd(string plan) => plan switch
    {
        Consts.Plans.Pro => Consts.Plans.ProPrice,
        Consts.Plans.ProPlus => Consts.Plans.ProPlusPrice,
        Consts.Plans.Trial => Consts.Plans.TrialPrice,
        _ => throw new ArgumentException($"Unknown plan '{plan}'"),
    };
    
    /// <summary>
    /// Check if Plan Tier is lapsed or not 
    /// </summary>
    /// <returns>Current plan</returns>
    public static PlanTiers CheckPlanExpiration(PlanTiers tier, DateTime? expiresAt, DateTime nowUtc)
    {
        var isProSubscriptionLapsed = tier is PlanTiers.Pro or PlanTiers.ProPlus
            && expiresAt is not null
            && expiresAt.Value <= nowUtc;
        
        return isProSubscriptionLapsed ? PlanTiers.Free : tier;
    }

    public static DateTime NextExpiry(PlanTiers currentTier, DateTime? currentExpiresAt, PlanTiers purchasedTier, DateTime nowUtc)
    {
        var baseline = currentTier == purchasedTier && currentExpiresAt > nowUtc
            ? currentExpiresAt.Value
            : nowUtc;
        return baseline + SubscriptionPeriod + Grace;
    }
}
