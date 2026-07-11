namespace CedarClerk.Core;

/// <summary>
/// Here are defined the limitations for stuff which depend on the Subscription plan
/// </summary>
public static class PlanLimitations
{
    public const int AiDailyLimit = 20;
    public static readonly TimeSpan FreeChannelSwitchCooldown = TimeSpan.FromDays(7);
    
    public static int MaxChannels(PlanTiers tier) => tier switch
    {
        PlanTiers.Free => 1,
        PlanTiers.Pro => 3,
        _ => 10,
    };

    public static long StorageLimitBytes(PlanTiers tier) => tier switch
    {
        PlanTiers.Free => 200L * 1024 * 1024,
        PlanTiers.Pro => 1024L * 1024 * 1024,
        _ => 5L * 1024 * 1024 * 1024, // ProPlus, Forever
    };

    public static bool CanConnectAnotherChannel(PlanTiers tier, int currentChannelCount)
    {
        return currentChannelCount < MaxChannels(tier);
    }

    public static bool HasStorageRoom(PlanTiers tier, long currentUsageBytes, long incomingBytes)
    {
        return currentUsageBytes + incomingBytes <= StorageLimitBytes(tier);
    }

    public static bool HasCustomSignature(PlanTiers tier)
    {
        return tier >= PlanTiers.Pro;
    }

    public static bool HasAiFeatures(PlanTiers tier)
    {
        return tier >= PlanTiers.ProPlus;
    }
}
