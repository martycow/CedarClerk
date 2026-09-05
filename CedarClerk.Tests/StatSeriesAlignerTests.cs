using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-242 / ADR-161 §2 on the server. The shapes that matter: the window starts where the youngest
// selected source starts, a gap carries the last reading forward, delta is last minus first over
// the window, and an untracked metric is null rather than a row of zeros.
public class StatSeriesAlignerTests
{
    private static DateOnly D(int day) => new(2026, 9, day);

    private static StatReading R(int day, int member, int? likes = null) =>
        new(D(day), likes is { } l
            ? new Dictionary<string, int> { [StatMetrics.MemberCount] = member, [StatMetrics.LikeCount] = l }
            : new Dictionary<string, int> { [StatMetrics.MemberCount] = member });

    private static SourceReadings Source(string id, IReadOnlyList<string> tracked, params StatReading[] readings) =>
        new(id, id, tracked, readings, []);

    [Fact]
    public void Window_is_the_requested_days_when_every_source_is_old_enough()
    {
        var old = Source("a", [StatMetrics.MemberCount], R(1, 10), R(20, 12));

        Assert.Equal((D(11), D(20)), StatSeriesAligner.Window([old], D(20), 10));
    }

    [Fact]
    public void A_young_source_shortens_the_window()
    {
        var old = Source("a", [StatMetrics.MemberCount], R(1, 10), R(20, 12));
        var young = Source("b", [StatMetrics.MemberCount], R(17, 5));

        Assert.Equal((D(17), D(20)), StatSeriesAligner.Window([old, young], D(20), 10));
    }

    [Fact]
    public void A_source_without_readings_does_not_shorten_the_window_and_no_readings_at_all_is_null()
    {
        var old = Source("a", [StatMetrics.MemberCount], R(1, 10));
        var empty = Source("b", [StatMetrics.MemberCount]);

        Assert.Equal((D(11), D(20)), StatSeriesAligner.Window([old, empty], D(20), 10));
        Assert.Null(StatSeriesAligner.Window([empty], D(20), 10));
    }

    [Fact]
    public void Gaps_carry_the_last_reading_forward_and_the_later_reading_wins_a_day()
    {
        var source = Source("a", [StatMetrics.MemberCount], R(1, 10), R(3, 11), R(3, 12), R(5, 15));

        var aligned = StatSeriesAligner.Align([source], D(1), D(6));

        Assert.Equal([D(1), D(2), D(3), D(4), D(5), D(6)], aligned.Days);
        var values = aligned.Series.Single().Values[StatMetrics.MemberCount]!;
        Assert.Equal([10, 10, 12, 12, 15, 15], values);
    }

    [Fact]
    public void A_reading_before_the_window_is_the_carried_value_on_its_first_day()
    {
        var source = Source("a", [StatMetrics.MemberCount], R(1, 10), R(8, 20));

        var aligned = StatSeriesAligner.Align([source], D(5), D(9));

        Assert.Equal([10, 10, 10, 20, 20], aligned.Series.Single().Values[StatMetrics.MemberCount]!);
    }

    [Fact]
    public void Delta_is_last_minus_first_over_the_window_and_current_is_last()
    {
        var source = Source("a", [StatMetrics.MemberCount], R(1, 10), R(3, 12), R(5, 15));

        var item = StatSeriesAligner.Align([source], D(2), D(5)).Series.Single();

        Assert.Equal(15, item.Current[StatMetrics.MemberCount]);
        Assert.Equal(5, item.Delta[StatMetrics.MemberCount]);
    }

    [Fact]
    public void An_untracked_metric_is_null_and_absent_from_current_and_delta()
    {
        var source = Source("a", [StatMetrics.MemberCount, StatMetrics.LikeCount], R(1, 10, 2), R(2, 11, 3));

        var item = StatSeriesAligner.Align([source], D(1), D(2)).Series.Single();

        Assert.Null(item.Values[StatMetrics.ViewCount]);
        Assert.Null(item.Values[StatMetrics.CommentCount]);
        Assert.Equal([2, 3], item.Values[StatMetrics.LikeCount]!);
        Assert.False(item.Current.ContainsKey(StatMetrics.ViewCount));
        Assert.False(item.Delta.ContainsKey(StatMetrics.ViewCount));
    }

    [Fact]
    public void Sources_without_a_reading_in_or_before_the_window_are_left_out_in_input_order()
    {
        var a = Source("a", [StatMetrics.MemberCount], R(1, 1));
        var none = Source("none", [StatMetrics.MemberCount]);
        var later = Source("later", [StatMetrics.MemberCount], R(9, 1));
        var b = Source("b", [StatMetrics.MemberCount], R(2, 2));

        var aligned = StatSeriesAligner.Align([a, none, later, b], D(1), D(3));

        Assert.Equal(["a", "b"], aligned.Series.Select(s => s.Id));
    }

    [Fact]
    public void Publish_days_are_clipped_to_the_window_deduplicated_and_sorted()
    {
        var source = new SourceReadings("a", "A", [StatMetrics.MemberCount], [R(1, 1)], [D(9), D(4), D(4), D(2), D(1)]);

        var item = StatSeriesAligner.Align([source], D(2), D(5)).Series.Single();

        Assert.Equal([D(2), D(4)], item.PublishDays);
    }

    [Fact]
    public void An_inverted_window_is_empty()
    {
        var aligned = StatSeriesAligner.Align([Source("a", [StatMetrics.MemberCount], R(1, 1))], D(5), D(4));

        Assert.Empty(aligned.Days);
        Assert.Empty(aligned.Series);
    }
}
