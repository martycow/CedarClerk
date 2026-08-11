namespace CedarClerk.Core;

/// <summary>
/// Where a sprint sits relative to today (T-124, ADR-111).
///
/// **Derived, never stored.** A status column is wrong the second the clock passes the end date,
/// and keeping it right needs a background job, or a fix-on-read, or a "close this sprint" button
/// — machinery serving a copy of a fact already written in two dates.
/// </summary>
public static class SprintStates
{
    public const string Planned = "planned";
    public const string Current = "current";

    /// <summary>
    /// The dates have run out. **Not** "everything in it is done" — a sprint with unfinished tasks
    /// still ends on the day it ends, and the planner goes on showing those tasks rather than
    /// hiding them along with the date (ADR-111).
    /// </summary>
    public const string Finished = "finished";

    public static readonly IReadOnlyList<string> All = [Current, Planned, Finished];

    /// <summary>
    /// Compared by calendar day and inclusive at both ends: a sprint that ends today is still the
    /// current one today. "Ends 16 Aug" reads as a day of work, not as midnight on the 15th.
    /// </summary>
    public static string Of(DateTime startsAt, DateTime endsAt, DateTime today)
    {
        var day = today.Date;
        if (day < startsAt.Date) return Planned;
        if (day > endsAt.Date) return Finished;
        return Current;
    }

    /// <summary>
    /// Order for the planner: the sprint in progress, then what is coming, then what has run out.
    /// Overlaps are allowed on purpose (ADR-111) — forbidding them protects nothing and gets in
    /// the way the first time dates move — so "current" can legitimately match more than one, and
    /// the caller breaks the tie by the later start.
    /// </summary>
    public static int Order(string state) => state switch
    {
        Current => 0,
        Planned => 1,
        _ => 2,
    };
}
