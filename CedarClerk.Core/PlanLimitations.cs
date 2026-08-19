namespace CedarClerk.Core;

// Text is always non-empty; Href is set only when the signature renders as a clickable link.
public sealed record ResolvedSignature(string Text, string? Href);

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
        // Sized to the droplet's disk, not to generosity (ADR-129) — raising later is easy, lowering isn't.
        PlanTiers.Free => 100L * 1024 * 1024,           // 100Mb
        PlanTiers.Pro => 1L * 1024 * 1024 * 1024,       // 1Gb
        PlanTiers.ProPlus => 3L * 1024 * 1024 * 1024,   // 3Gb
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

    // Centralized so Telegram, blog and static export do not each re-implement the Free-vs-Pro gate
    // (ADR-034). Free always gets the fixed attribution; Pro+ can replace or clear it.
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
