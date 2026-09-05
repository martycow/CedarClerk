namespace CedarClerk.Core;

// The metric keys the Stats tab draws, byte-exact with its MetricKey union.
public static class StatMetrics
{
    public const string MemberCount = "memberCount";
    public const string ViewCount = "viewCount";
    public const string LikeCount = "likeCount";
    public const string CommentCount = "commentCount";

    public static readonly IReadOnlyList<string> All = [MemberCount, ViewCount, LikeCount, CommentCount];
}

/// <summary>One reading of a source, keyed by the calendar day it was taken on in the display zone.</summary>
public sealed record StatReading(DateOnly Day, IReadOnlyDictionary<string, int> Values);

/// <summary>Readings ascending by the moment they were taken — two on one day resolve to the later.</summary>
public sealed record SourceReadings(
    string Id,
    string Name,
    IReadOnlyList<string> Tracked,
    IReadOnlyList<StatReading> Readings,
    IReadOnlyList<DateOnly> PublishDays);

public sealed record AlignedSeries(
    string Id,
    string Name,
    IReadOnlyList<string> Tracked,
    IReadOnlyDictionary<string, IReadOnlyList<int>?> Values,
    IReadOnlyDictionary<string, int> Current,
    IReadOnlyDictionary<string, int> Delta,
    IReadOnlyList<DateOnly> PublishDays);

public sealed record StatSeries(IReadOnlyList<DateOnly> Days, IReadOnlyList<AlignedSeries> Series)
{
    public static readonly StatSeries Empty = new([], []);
}

// ADR-161 §2 moved to the server: one axis for every drawn line, starting where the youngest
// selected source starts, and a gap holds the last reading rather than a zero or a stretched first
// value — the readings are running totals, so the last one is what is known until the next.
public static class StatSeriesAligner
{
    /// <summary>
    /// <c>to</c> is today; <c>from</c> is the later of today − (days − 1) and the first reading of
    /// the youngest source that has one. Null when no source has a reading at all.
    /// </summary>
    public static (DateOnly From, DateOnly To)? Window(IReadOnlyList<SourceReadings> sources, DateOnly today, int days)
    {
        DateOnly? youngest = null;
        foreach (var source in sources)
        {
            if (source.Readings.Count == 0) continue;
            var first = source.Readings.Min(r => r.Day);
            if (youngest is null || first > youngest) youngest = first;
        }
        if (youngest is null) return null;

        var from = today.AddDays(-(days - 1));
        if (youngest > from) from = youngest.Value;
        if (from > today) from = today;
        return (from, today);
    }

    public static StatSeries Align(IReadOnlyList<SourceReadings> sources, DateOnly from, DateOnly to)
    {
        if (to < from) return StatSeries.Empty;

        var dayCount = to.DayNumber - from.DayNumber + 1;
        var days = new List<DateOnly>(dayCount);
        for (var i = 0; i < dayCount; i++) days.Add(from.AddDays(i));

        var series = new List<AlignedSeries>();
        foreach (var source in sources)
        {
            var byDay = new SortedDictionary<DateOnly, IReadOnlyDictionary<string, int>>();
            foreach (var reading in source.Readings) byDay[reading.Day] = reading.Values;
            if (byDay.Count == 0 || byDay.Keys.First() > to) continue;

            var ordered = byDay.ToList();
            var values = new Dictionary<string, IReadOnlyList<int>?>();
            var current = new Dictionary<string, int>();
            var delta = new Dictionary<string, int>();
            foreach (var metric in StatMetrics.All)
            {
                if (!source.Tracked.Contains(metric))
                {
                    values[metric] = null;
                    continue;
                }

                var dense = new int[dayCount];
                var next = 0;
                var carried = ordered[0].Value.GetValueOrDefault(metric);
                for (var i = 0; i < dayCount; i++)
                {
                    while (next < ordered.Count && ordered[next].Key <= days[i])
                        carried = ordered[next++].Value.GetValueOrDefault(metric);
                    dense[i] = carried;
                }

                values[metric] = dense;
                current[metric] = dense[^1];
                delta[metric] = dense[^1] - dense[0];
            }

            var publishDays = source.PublishDays
                .Where(d => d >= from && d <= to)
                .Distinct()
                .Order()
                .ToList();

            series.Add(new AlignedSeries(source.Id, source.Name, source.Tracked, values, current, delta, publishDays));
        }

        return new StatSeries(days, series);
    }
}
