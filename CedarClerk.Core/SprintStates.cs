namespace CedarClerk.Core;

// Where a sprint sits relative to today (T-124, ADR-111). Derived, never stored: a status column is
// wrong the second the clock passes the end date, and keeping it right needs a job, a fix-on-read or
// a "close this sprint" button — machinery serving a copy of a fact already written in two dates.
public static class SprintStates
{
    public const string Planned = "planned";
    public const string Current = "current";

    // The dates ran out, NOT "everything is done" — the planner goes on showing unfinished tasks
    // rather than hiding them along with the date (ADR-111).
    public const string Finished = "finished";

    public static readonly IReadOnlyList<string> All = [Current, Planned, Finished];

    // Inclusive at both ends: "ends 16 Aug" reads as a day of work, not midnight on the 15th.
    public static string Of(DateTime startsAt, DateTime endsAt, DateTime today)
    {
        var day = today.Date;
        if (day < startsAt.Date) return Planned;
        if (day > endsAt.Date) return Finished;
        return Current;
    }

    // Overlapping sprints are allowed (ADR-111), so "current" can match more than one and the caller
    // breaks the tie by the later start.
    public static int Order(string state) => state switch
    {
        Current => 0,
        Planned => 1,
        _ => 2,
    };
}
