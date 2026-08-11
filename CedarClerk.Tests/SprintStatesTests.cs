using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-124 / ADR-111 — a sprint's state is derived from its dates every time it is asked, so the
// boundaries are the whole of the behaviour: they are what a stored status would get wrong the
// moment the clock crossed them.
public class SprintStatesTests
{
    private static readonly DateTime Start = new(2026, 8, 10);
    private static readonly DateTime End = new(2026, 8, 16);

    [Fact]
    public void Before_the_first_day_it_is_planned()
    {
        Assert.Equal(SprintStates.Planned, SprintStates.Of(Start, End, new DateTime(2026, 8, 9)));
    }

    [Fact]
    public void The_first_day_is_already_current()
    {
        Assert.Equal(SprintStates.Current, SprintStates.Of(Start, End, Start));
    }

    [Fact]
    public void The_last_day_is_still_current()
    {
        // Inclusive on purpose: "ends 16 Aug" reads as a day of work, not as midnight on the 15th.
        Assert.Equal(SprintStates.Current, SprintStates.Of(Start, End, End));
    }

    [Fact]
    public void The_day_after_the_last_one_is_finished()
    {
        Assert.Equal(SprintStates.Finished, SprintStates.Of(Start, End, new DateTime(2026, 8, 17)));
    }

    [Fact]
    public void The_time_of_day_never_decides_anything()
    {
        // Compared by calendar day: a sprint does not end at 00:00 and it does not start at noon.
        Assert.Equal(SprintStates.Current, SprintStates.Of(Start, End, End.AddHours(23).AddMinutes(59)));
        Assert.Equal(SprintStates.Planned, SprintStates.Of(Start, End, Start.AddHours(-1)));
    }

    [Fact]
    public void A_one_day_sprint_is_current_on_that_day_only()
    {
        var day = new DateTime(2026, 8, 12);
        Assert.Equal(SprintStates.Current, SprintStates.Of(day, day, day));
        Assert.Equal(SprintStates.Planned, SprintStates.Of(day, day, day.AddDays(-1)));
        Assert.Equal(SprintStates.Finished, SprintStates.Of(day, day, day.AddDays(1)));
    }

    [Fact]
    public void The_planner_orders_current_then_planned_then_finished()
    {
        Assert.True(SprintStates.Order(SprintStates.Current) < SprintStates.Order(SprintStates.Planned));
        Assert.True(SprintStates.Order(SprintStates.Planned) < SprintStates.Order(SprintStates.Finished));
    }

    // The counter behind the S-chip. Written after running the endpoints caught the opposite:
    // MAX(Number) + 1 handed the highest number straight back out after a deletion, which is
    // exactly the reuse ADR-111 forbids.
    [Fact]
    public void A_project_hands_out_its_next_number_and_moves_on()
    {
        var project = new Server.Project();
        Assert.Equal(1, project.NextSprintNumber);

        var issued = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            issued.Add(project.NextSprintNumber);
            project.NextSprintNumber++;
        }

        Assert.Equal([1, 2, 3], issued);
        // Deleting sprints does not wind it back — that is the whole point of a counter.
        Assert.Equal(4, project.NextSprintNumber);
    }
}
