namespace CedarClerk.Core;

/// <summary>
/// Resolved end-of-post signature: Text is always non-empty, Href is set only when the
/// signature should render as a clickable link.
/// </summary>
public sealed record ResolvedSignature(string Text, string? Href);

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
        PlanTiers.Free => 200L * 1024 * 1024,           // 200Mb
        PlanTiers.Pro => 8L * 1024 * 1024 * 1024,       // 8Gb
        PlanTiers.ProPlus => 16L * 1024 * 1024 * 1024,  // 16Gb
        PlanTiers.Forever => 100L * 1024 * 1024 * 1024, // 100Gb
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
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

    /// <summary>
    /// Free tier always gets the fixed Cedar Clerk attribution; Pro+ can replace it with a custom
    /// signature (optionally a clickable link via Href) or clear it entirely (null = no signature
    /// at all). Centralized here so Telegram/blog/static-export don't each re-implement the
    /// Free-vs-Pro gate. See Phase 8 Step 5, docs/ROADMAP.md, ADR-034 in docs/DECISIONS.md.
    /// </summary>
    public static ResolvedSignature? ResolveSignature(PlanTiers tier, string? postSignature, string? postSignatureUrl)
    {
        if (!HasCustomSignature(tier))
            return new ResolvedSignature(Consts.Signatures.FreeAttributionText, Consts.URLs.MainHost);

        return string.IsNullOrWhiteSpace(postSignature)
            ? null
            : new ResolvedSignature(postSignature, string.IsNullOrWhiteSpace(postSignatureUrl) ? null : postSignatureUrl.Trim());
    }

    public static int MaxHeaderSlots(PlanTiers tier) => tier >= PlanTiers.Pro ? 3 : 2;

    public static bool HasAiFeatures(PlanTiers tier)
    {
        return tier >= PlanTiers.ProPlus;
    }
}
