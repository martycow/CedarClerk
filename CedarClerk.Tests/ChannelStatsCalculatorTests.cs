using CedarClerk.Core;

namespace CedarClerk.Tests;

public class ChannelStatsCalculatorTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void No_snapshots_returns_null()
    {
        Assert.Null(ChannelStatsCalculator.DeltaOverDays([], 7, Now));
    }

    [Fact]
    public void Channel_younger_than_window_returns_null()
    {
        var snapshots = new[]
        {
            new ChannelStatPoint(Now.AddDays(-2), 100),
            new ChannelStatPoint(Now.AddDays(-1), 105),
            new ChannelStatPoint(Now, 110),
        };
        Assert.Null(ChannelStatsCalculator.DeltaOverDays(snapshots, 7, Now));
    }

    [Fact]
    public void Computes_delta_against_oldest_snapshot_within_window()
    {
        var snapshots = new[]
        {
            new ChannelStatPoint(Now.AddDays(-10), 80),
            new ChannelStatPoint(Now.AddDays(-8), 90),
            new ChannelStatPoint(Now.AddDays(-6), 100),
            new ChannelStatPoint(Now.AddDays(-1), 118),
            new ChannelStatPoint(Now, 120),
        };
        // baseline = last snapshot at or before (Now - 7d) => the -8d snapshot (90); current = 120
        Assert.Equal(30, ChannelStatsCalculator.DeltaOverDays(snapshots, 7, Now));
    }

    [Fact]
    public void Negative_delta_when_member_count_dropped()
    {
        var snapshots = new[]
        {
            new ChannelStatPoint(Now.AddDays(-8), 100),
            new ChannelStatPoint(Now, 95),
        };
        Assert.Equal(-5, ChannelStatsCalculator.DeltaOverDays(snapshots, 7, Now));
    }
}
