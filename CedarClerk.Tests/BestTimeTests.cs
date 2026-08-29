using CedarClerk.Server;

namespace CedarClerk.Tests;

// Item 12 — no ML, just honest arithmetic over what the channel has already done. The empty list
// for a young channel is part of the contract: the UI hides the hint rather than inventing one.
public class BestTimeTests
{
    private static PublishedPostSample At(int hour, int reactions, int comments, int day = 1) =>
        new(new DateTime(2026, 8, day, hour, 30, 0, DateTimeKind.Utc), reactions, comments);

    [Fact]
    public void Hours_with_a_single_post_say_nothing()
    {
        var slots = BestTimeCalculator.Compute([At(9, 100, 50)]);

        Assert.Empty(slots);
    }

    [Fact]
    public void No_posts_at_all_is_an_empty_answer()
    {
        Assert.Empty(BestTimeCalculator.Compute([]));
    }

    [Fact]
    public void Known_fixture_yields_the_expected_hours_in_engagement_order()
    {
        var slots = BestTimeCalculator.Compute(
        [
            At(9, 10, 2), At(9, 20, 4, day: 2),          // avg 15 + 3 = 18
            At(18, 40, 10), At(18, 60, 14, day: 2),      // avg 50 + 12 = 62
            At(12, 5, 1), At(12, 7, 1, day: 2), At(12, 9, 1, day: 3), // avg 7 + 1 = 8
            At(23, 999, 999),                             // one post — excluded
        ]);

        Assert.Equal([18, 9, 12], slots.Select(s => s.Hour));

        var top = slots[0];
        Assert.Equal(2, top.Posts);
        Assert.Equal(50, top.AvgReactions);
        Assert.Equal(12, top.AvgComments);

        var noon = slots[2];
        Assert.Equal(3, noon.Posts);
        Assert.Equal(7, noon.AvgReactions);
        Assert.Equal(1, noon.AvgComments);
    }

    [Fact]
    public void Ties_order_by_hour_so_the_answer_is_stable()
    {
        var slots = BestTimeCalculator.Compute(
        [
            At(15, 10, 5), At(15, 10, 5, day: 2),
            At(8, 10, 5), At(8, 10, 5, day: 2),
        ]);

        Assert.Equal([8, 15], slots.Select(s => s.Hour));
    }

    [Fact]
    public void Grouping_is_by_utc_hour_across_days()
    {
        var slots = BestTimeCalculator.Compute([At(9, 4, 0, day: 1), At(9, 8, 2, day: 15)]);

        var slot = Assert.Single(slots);
        Assert.Equal(9, slot.Hour);
        Assert.Equal(2, slot.Posts);
        Assert.Equal(6, slot.AvgReactions);
        Assert.Equal(1, slot.AvgComments);
    }
}
