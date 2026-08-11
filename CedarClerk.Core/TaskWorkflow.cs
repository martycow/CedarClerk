namespace CedarClerk.Core;

/// <summary>
/// Where a task is (T-123, ADR-106). Four columns, matching the board in the design handoff.
///
/// Strings rather than an enum, for the reason <c>DocumentTypes</c> and <c>AssetKinds</c> are:
/// a fifth column should be a constant and a UI, not a migration. The order of <see cref="All"/>
/// is the board's left-to-right order and the list view's sort — one place decides both.
/// </summary>
public static class TaskStatuses
{
    public const string Backlog = "backlog";
    public const string Planned = "planned";
    public const string InProgress = "in_progress";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [Backlog, Planned, InProgress, Done];

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    /// <summary>
    /// Everything that is not <see cref="Done"/>. "Open" is the count the project list and the
    /// dashboard show, so what it means is decided here rather than in each query.
    /// </summary>
    public static bool IsOpen(string? status) => status != Done;

    /// <summary>Position on the board. An unknown status sorts last rather than throwing.</summary>
    public static int Order(string? status)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i] == status) return i;
        return All.Count;
    }
}

/// <summary>
/// How urgent a task is: 1, 2 or 3, shown as P1/P2/P3 (T-123).
///
/// A small int rather than a string because it is sorted by, and sorting "high/medium/low" as text
/// puts high after low. Three levels, not five: the design tints only P1, and a scale nobody can
/// tell apart in the middle is a scale that gets used as two levels anyway.
/// </summary>
public static class TaskPriorities
{
    public const int Highest = 1;
    public const int Normal = 2;
    public const int Lowest = 3;

    public static readonly IReadOnlyList<int> All = [Highest, Normal, Lowest];

    public static bool IsKnown(int priority) => priority is >= Highest and <= Lowest;

    /// <summary>Anything out of range becomes <see cref="Normal"/> — a task is never left unrankable.</summary>
    public static int Clamp(int priority) => IsKnown(priority) ? priority : Normal;
}
