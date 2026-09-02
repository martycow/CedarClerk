using System.Collections.Concurrent;

namespace CedarClerk.Core;

/// <summary>Validation and lookup for account-owned IANA timezones (ADR-244).</summary>
public static class TimeZones
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo?> Cache =
        new(StringComparer.Ordinal);

    public static bool IsValid(string? id) => Resolve(id) is not null;

    public static string NormalizeOrDefault(string? id) =>
        Resolve(id) is not null ? id!.Trim() : Consts.General.DisplayTimeZone;

    public static TimeZoneInfo ResolveOrDefault(string? id) =>
        Resolve(id) ?? Resolve(Consts.General.DisplayTimeZone)
        ?? TimeZoneInfo.CreateCustomTimeZone("PST", TimeSpan.FromHours(-8), "PST", "PST");

    private static TimeZoneInfo? Resolve(string? raw)
    {
        var id = raw?.Trim();
        if (string.IsNullOrEmpty(id)) return null;

        // Browsers consume the same stored value through Intl.DateTimeFormat. Windows timezone ids
        // may resolve in .NET but do not resolve there, so the profile contract accepts IANA only.
        if (id != "UTC" && !id.Contains('/')) return null;

        return Cache.GetOrAdd(id, static candidate =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { return null; }
            catch (InvalidTimeZoneException) { return null; }
        });
    }
}
