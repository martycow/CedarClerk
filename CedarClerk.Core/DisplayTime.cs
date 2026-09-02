namespace CedarClerk.Core;

// The single place a stored UTC instant becomes the time printed on a page (ADR-115). SQLite has
// nowhere to keep the kind, so values read back arrive Unspecified and are treated as UTC — which is
// what they always are here (DateTime.UtcNow everywhere, file times as LastWriteTimeUtc).
public static class DisplayTime
{
    /// <summary>The UTC instant as wall-clock time in the display zone.</summary>
    public static DateTime ToZone(DateTime utc, string? timeZoneId = null) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), TimeZones.ResolveOrDefault(timeZoneId));

    public static DateTime? ToZone(DateTime? utc, string? timeZoneId = null) =>
        utc is { } value ? ToZone(value, timeZoneId) : null;

    /// <summary>A compact, unambiguous label for the configured zone at this instant.</summary>
    public static string Abbreviation(DateTime utc, string? timeZoneId = null)
    {
        var zoneId = TimeZones.NormalizeOrDefault(timeZoneId);
        var zone = TimeZones.ResolveOrDefault(zoneId);
        if (zoneId == Consts.General.DisplayTimeZone)
            return zone.IsDaylightSavingTime(AsUtc(utc))
                ? Consts.General.DisplayTimeZoneDaylight
                : Consts.General.DisplayTimeZoneStandard;
        if (zoneId == "UTC") return "UTC";

        var offset = zone.GetUtcOffset(AsUtc(utc));
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        offset = offset.Duration();
        return $"UTC{sign}{offset.Hours:00}:{offset.Minutes:00}";
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

}
