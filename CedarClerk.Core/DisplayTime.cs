namespace CedarClerk.Core;

// The single place a stored UTC instant becomes the time printed on a page (ADR-115). SQLite has
// nowhere to keep the kind, so values read back arrive Unspecified and are treated as UTC — which is
// what they always are here (DateTime.UtcNow everywhere, file times as LastWriteTimeUtc).
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
