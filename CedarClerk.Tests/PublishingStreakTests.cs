using CedarClerk.Server;

namespace CedarClerk.Tests;

// T-166/T-244. The edges that matter are exactly the ones a wall calendar gets wrong: where a week
// begins (ISO, Monday, UTC), what a gap does to a run, and what the still-unfinished current week
// counts for. Fixture Mondays: 2026-08-24 is a Monday; so are 2026-08-03/10/17 and 2026-07-06/13/20.
public class PublishingStreakTests
{
    private static DateTime Utc(int month, int day, int hour = 12) =>
        new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = Utc(8, 26); // Wednesday of the week starting 2026-08-24

    [Fact]
    public void Week_start_is_the_iso_monday()
    {
        Assert.Equal(Utc(8, 24, 0), PublishingStreaks.WeekStartUtc(Utc(8, 26)));           // Wednesday
        Assert.Equal(Utc(8, 24, 0), PublishingStreaks.WeekStartUtc(Utc(8, 30, 23)));       // Sunday
        Assert.Equal(Utc(8, 24, 0), PublishingStreaks.WeekStartUtc(Utc(8, 24, 0)));        // Monday itself
    }

    [Fact]
    public void Sunday_night_and_monday_morning_fall_into_different_weeks()
    {
        var result = PublishingStreaks.Compute([Utc(8, 23, 23), Utc(8, 24, 0)], Now);

        Assert.Equal(1, result.Weeks.Single(w => w.WeekStartUtc == Utc(8, 17, 0)).Publishes);
        Assert.Equal(1, result.Weeks.Single(w => w.WeekStartUtc == Utc(8, 24, 0)).Publishes);
    }

    [Fact]
    public void Consecutive_weeks_including_the_current_one_count_as_the_streak()
    {
        var result = PublishingStreaks.Compute([Utc(8, 11), Utc(8, 19), Utc(8, 25)], Now);

        Assert.Equal(3, result.CurrentStreakWeeks);
        Assert.Equal(3, result.LongestStreakWeeks);
    }

    [Fact]
    public void An_empty_current_week_does_not_break_the_streak_yet()
    {
        var result = PublishingStreaks.Compute([Utc(8, 11), Utc(8, 19)], Now);

        Assert.Equal(2, result.CurrentStreakWeeks);
    }

    [Fact]
    public void A_gap_resets_the_current_streak_but_not_the_longest()
    {
        var events = new[] { Utc(7, 7), Utc(7, 15), Utc(7, 23), Utc(8, 25) }; // 3 weeks, gap, 1 week

        var result = PublishingStreaks.Compute(events, Now);

        Assert.Equal(1, result.CurrentStreakWeeks);
        Assert.Equal(3, result.LongestStreakWeeks);
    }

    [Fact]
    public void No_events_at_all_is_a_zero_streak_over_a_full_window_of_zeroes()
    {
        var result = PublishingStreaks.Compute([], Now);

        Assert.Equal(0, result.CurrentStreakWeeks);
        Assert.Equal(0, result.LongestStreakWeeks);
        Assert.Equal(PublishingStreaks.WeeksServed, result.Weeks.Count);
        Assert.All(result.Weeks, w => Assert.Equal(0, w.Publishes));
    }

    [Fact]
    public void The_window_ends_at_the_current_week_and_counts_multiple_publishes()
    {
        var result = PublishingStreaks.Compute([Utc(8, 24), Utc(8, 25), Utc(8, 26)], Now);

        Assert.Equal(Utc(8, 24, 0), result.Weeks[^1].WeekStartUtc);
        Assert.Equal(3, result.Weeks[^1].Publishes);
        Assert.Equal(result.Weeks.OrderBy(w => w.WeekStartUtc).Select(w => w.WeekStartUtc), result.Weeks.Select(w => w.WeekStartUtc));
    }

    [Fact]
    public void Events_older_than_the_window_still_shape_the_longest_streak()
    {
        var events = Enumerable.Range(0, 5).Select(i => Utc(1, 5).AddDays(7 * i)).ToArray(); // Jan, 5 weeks

        var result = PublishingStreaks.Compute(events, Now, weeksServed: 4);

        Assert.Equal(4, result.Weeks.Count);
        Assert.Equal(0, result.CurrentStreakWeeks);
        Assert.Equal(5, result.LongestStreakWeeks);
    }
}
