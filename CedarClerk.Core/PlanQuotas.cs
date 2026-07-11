namespace CedarClerk.Core;

public static class PlanQuotas
{
    public const int FreeMaxChannels = 1;
    public const long FreeStorageBytes = 200L * 1024 * 1024;

    public static bool CanConnectAnotherChannel(PlanTiers tier, int currentChannelCount) =>
        tier == PlanTiers.Pro || currentChannelCount < FreeMaxChannels;

    public static bool HasStorageRoom(PlanTiers tier, long currentUsageBytes, long incomingBytes) =>
        tier == PlanTiers.Pro || currentUsageBytes + incomingBytes <= FreeStorageBytes;
}
