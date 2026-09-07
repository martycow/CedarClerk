namespace CedarClerk.Localization;

// SQLite loses DateTime.Kind, so stored Unspecified values must be interpreted as UTC.
public static class DisplayTime
{
    public static DateTime ToZone(DateTime utc, string? timeZoneId = null) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), TimeZones.ResolveOrDefault(timeZoneId));

    public static DateTime? ToZone(DateTime? utc, string? timeZoneId = null) =>
        utc is { } value ? ToZone(value, timeZoneId) : null;

    public static string Abbreviation(DateTime utc, string? timeZoneId = null)
    {
        var zoneId = TimeZones.NormalizeOrDefault(timeZoneId);
        var zone = TimeZones.ResolveOrDefault(zoneId);
        if (zoneId == TimeZones.DefaultId)
            return zone.IsDaylightSavingTime(AsUtc(utc))
                ? TimeZones.DaylightAbbreviation
                : TimeZones.StandardAbbreviation;
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
