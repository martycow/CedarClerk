using CedarClerk.Core;

namespace CedarClerk.Tests;

public class PlanQuotasTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    public void Free_tier_allows_only_one_channel(int currentCount, bool expected)
    {
        Assert.Equal(expected, PlanQuotas.CanConnectAnotherChannel(PlanTiers.Free, currentCount));
    }

    [Fact]
    public void Pro_tier_has_no_channel_limit()
    {
        Assert.True(PlanQuotas.CanConnectAnotherChannel(PlanTiers.Pro, 50));
    }

    [Fact]
    public void Free_tier_storage_within_budget_is_allowed()
    {
        Assert.True(PlanQuotas.HasStorageRoom(PlanTiers.Free, currentUsageBytes: 100, incomingBytes: 200));
    }

    [Fact]
    public void Free_tier_storage_exceeding_budget_is_rejected()
    {
        Assert.False(PlanQuotas.HasStorageRoom(PlanTiers.Free, currentUsageBytes: PlanQuotas.FreeStorageBytes - 10, incomingBytes: 20));
    }

    [Fact]
    public void Free_tier_storage_exactly_at_budget_is_allowed()
    {
        Assert.True(PlanQuotas.HasStorageRoom(PlanTiers.Free, currentUsageBytes: PlanQuotas.FreeStorageBytes - 20, incomingBytes: 20));
    }

    [Fact]
    public void Pro_tier_has_no_storage_limit()
    {
        Assert.True(PlanQuotas.HasStorageRoom(PlanTiers.Pro, currentUsageBytes: PlanQuotas.FreeStorageBytes * 100, incomingBytes: PlanQuotas.FreeStorageBytes));
    }
}
