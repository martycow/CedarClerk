namespace CedarClerk.Core;

// Where a task is (T-123, ADR-106). Strings rather than an enum, like DocumentTypes and AssetKinds:
// a fifth column should be a constant and a UI, not a migration. All is the board's left-to-right
// order and the list view's sort, so one place decides both.
public static class TaskStatuses
{
    public const string Backlog = "backlog";
    public const string Planned = "planned";
    public const string InProgress = "in_progress";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [Backlog, Planned, InProgress, Done];

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    // "Open" is the count the project list and the dashboard show, so it is decided here rather than
    // in each query.
    public static bool IsOpen(string? status) => status != Done;

    // An unknown status sorts last rather than throwing.
    public static int Order(string? status)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i] == status) return i;
        return All.Count;
    }
}

// P1/P2/P3 (T-123). An int because it is sorted by, and sorting "high/medium/low" as text puts high
// after low. Three levels, not five: a scale nobody can tell apart in the middle gets used as two.
public static class TaskPriorities
{
    public const int Highest = 1;
    public const int Normal = 2;
    public const int Lowest = 3;

    public static readonly IReadOnlyList<int> All = [Highest, Normal, Lowest];

    public static bool IsKnown(int priority) => priority is >= Highest and <= Lowest;

    // Out of range becomes Normal — a task is never left unrankable.
    public static int Clamp(int priority) => IsKnown(priority) ? priority : Normal;
}
