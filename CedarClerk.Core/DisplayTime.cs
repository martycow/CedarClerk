namespace CedarClerk.Core;

/// <summary>
/// Wall-clock time as the reader sees it (ADR-115).
///
/// The server stores and transports instants in UTC — that does not change. This is the single
/// place that turns one into the time printed on a page, and the zone it uses is
/// <see cref="Consts.General.DisplayTimeZone"/>.
///
/// Values read back from SQLite arrive as <see cref="DateTimeKind.Unspecified"/> because the file
/// format has nowhere to keep the kind, so everything here treats an unspecified kind as UTC —
/// which is what it always is in this codebase (<c>DateTime.UtcNow</c> everywhere, file times taken
/// as <c>LastWriteTimeUtc</c>).
/// </summary>
public static class DisplayTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>The UTC instant as wall-clock time in the display zone.</summary>
    public static DateTime ToZone(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone);

    public static DateTime? ToZone(DateTime? utc) => utc is { } value ? ToZone(value) : null;

    /// <summary>"PDT" or "PST" for that instant — the summer/winter distinction is the whole point.</summary>
    public static string Abbreviation(DateTime utc) =>
        Zone.IsDaylightSavingTime(AsUtc(utc)) ? Consts.General.DisplayTimeZoneDaylight : Consts.General.DisplayTimeZoneStandard;

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    // .NET 6+ accepts both IANA and Windows ids on both platforms, so the first lookup normally
    // wins everywhere. The fallbacks exist so a host with no tz database prints a slightly wrong
    // time instead of throwing on the first page render.
    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { Consts.General.DisplayTimeZone, Consts.General.DisplayTimeZoneWindows })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            Consts.General.DisplayTimeZoneStandard, TimeSpan.FromHours(-8), Consts.General.DisplayTimeZoneStandard, Consts.General.DisplayTimeZoneStandard);
    }
}
