using System.Globalization;

namespace CedarClerk.Cli.Parsing;

public sealed record DiskUsage(long TotalBytes, long UsedBytes, long FreeBytes)
{
    public double UsedFraction => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes;
    public static readonly DiskUsage Unknown = new(0, 0, 0);
}

public sealed record MemoryUsage(long TotalBytes, long UsedBytes, long AvailableBytes, long SwapTotalBytes)
{
    // "Used" from free(1) counts buffers and cache as free, which is what the kernel means by
    // available memory — the number that says whether this box is in trouble.
    public double UsedFraction => TotalBytes <= 0 ? 0 : 1 - (double)AvailableBytes / TotalBytes;
    public static readonly MemoryUsage Unknown = new(0, 0, 0, 0);
}

public sealed record ServiceState(
    string ActiveState, string SubState, string UnitFileState,
    DateTimeOffset? SinceUtc, int MainPid, int Restarts, long MemoryBytes)
{
    public bool IsActive => ActiveState.Equals("active", StringComparison.OrdinalIgnoreCase);
    // The droplet's unit is `disabled`, so a maintenance reboot leaves the site down (T-143). The
    // status screen says so out loud rather than only reporting that it is running right now.
    public bool SurvivesReboot => UnitFileState.Equals("enabled", StringComparison.OrdinalIgnoreCase);
    public static readonly ServiceState Unknown = new("unknown", "", "", null, 0, 0, 0);
}

// Parsers for the fixed-format tools the status screen reads. Every one of them returns an "unknown"
// value rather than throwing: a status screen that crashes because one field moved is worse than one
// that renders and admits it could not read that field.
public static class SystemFacts
{
    // df -Pk: POSIX output, so exactly six columns and no line wrapping whatever the mount is called.
    public static DiskUsage ParseDf(string text)
    {
        foreach (var raw in Lines(text))
        {
            var fields = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6) continue;
            if (!long.TryParse(fields[1], out var totalKb)) continue;   // skips the header row
            if (!long.TryParse(fields[2], out var usedKb)) continue;
            if (!long.TryParse(fields[3], out var freeKb)) continue;
            return new DiskUsage(totalKb * 1024, usedKb * 1024, freeKb * 1024);
        }
        return DiskUsage.Unknown;
    }

    // free -b: "Mem:" then total used free shared buff/cache available.
    public static MemoryUsage ParseFree(string text)
    {
        long total = 0, used = 0, available = 0, swapTotal = 0;
        foreach (var raw in Lines(text))
        {
            var fields = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;

            if (fields[0].StartsWith("Mem:", StringComparison.OrdinalIgnoreCase) && fields.Length >= 7)
            {
                long.TryParse(fields[1], out total);
                long.TryParse(fields[2], out used);
                long.TryParse(fields[6], out available);
            }
            else if (fields[0].StartsWith("Swap:", StringComparison.OrdinalIgnoreCase))
            {
                long.TryParse(fields[1], out swapTotal);
            }
        }
        return total == 0 ? MemoryUsage.Unknown : new MemoryUsage(total, used, available, swapTotal);
    }

    // systemctl show -p Key=Value output. Unset properties come back as an empty value, not absent.
    public static ServiceState ParseSystemctlShow(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Lines(text))
        {
            var split = raw.IndexOf('=');
            if (split <= 0) continue;
            map[raw[..split].Trim()] = raw[(split + 1)..].Trim();
        }
        if (map.Count == 0) return ServiceState.Unknown;

        return new ServiceState(
            Get(map, "ActiveState", "unknown"),
            Get(map, "SubState", ""),
            Get(map, "UnitFileState", ""),
            ParseSystemdTimestamp(Get(map, "ActiveEnterTimestamp", "")),
            ParseInt(Get(map, "MainPID", "0")),
            ParseInt(Get(map, "NRestarts", "0")),
            ParseLong(Get(map, "MemoryCurrent", "0")));
    }

    // "Wed 2026-08-12 08:06:57 UTC" — systemd prints the weekday first and the zone last.
    public static DateTimeOffset? ParseSystemdTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var fields = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3) return null;

        if (!DateTime.TryParseExact(
                $"{fields[1]} {fields[2]}", "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
            return null;

        // The droplet runs UTC (ADR-114). Any other zone name is read as UTC too rather than
        // guessed at — an hour's error in an uptime is better than a crash, and it is visible.
        return new DateTimeOffset(stamp, TimeSpan.Zero);
    }

    // du -sb output: "980371270\t/path/to/dir", one line per argument, missing paths simply absent.
    public static Dictionary<string, long> ParseDu(string text)
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var raw in Lines(text))
        {
            var fields = raw.Split(new[] { '\t', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            if (!long.TryParse(fields[0], out var bytes)) continue;
            sizes[fields[1].Trim()] = bytes;
        }
        return sizes;
    }

    // stat -c '%s %n' — size and name, one line per file.
    public static Dictionary<string, long> ParseStatSizes(string text) => ParseDu(text);

    private static IEnumerable<string> Lines(string text) =>
        (text ?? "").Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0);

    private static string Get(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static int ParseInt(string value) => int.TryParse(value, out var parsed) ? parsed : 0;

    private static long ParseLong(string value) => long.TryParse(value, out var parsed) ? parsed : 0;
}
