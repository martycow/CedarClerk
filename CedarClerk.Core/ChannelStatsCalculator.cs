namespace CedarClerk.Core;

public record ChannelStatPoint(DateTime TakenAt, int MemberCount);

public static class ChannelStatsCalculator
{
    // snapshots must be ordered ascending by TakenAt. Returns null when there isn't yet a
    // snapshot old enough to compare against (e.g. channel connected less than `days` ago).
    public static int? DeltaOverDays(IReadOnlyList<ChannelStatPoint> snapshotsAscending, int days, DateTime now)
    {
        if (snapshotsAscending.Count == 0) return null;

        var current = snapshotsAscending[^1].MemberCount;
        var cutoff = now.AddDays(-days);

        ChannelStatPoint? baseline = null;
        foreach (var point in snapshotsAscending)
        {
            if (point.TakenAt > cutoff) break;
            baseline = point;
        }

        return baseline is null ? null : current - baseline.MemberCount;
    }
}
