using System.Globalization;

namespace CedarClerk.Cli.Rendering;

public static class Format
{
    public static string Size(double bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024d * 1024 * 1024)).ToString("0.## GB", CultureInfo.InvariantCulture);
        if (bytes >= 1024 * 1024) return (bytes / (1024d * 1024)).ToString("0.# MB", CultureInfo.InvariantCulture);
        if (bytes >= 1024) return (bytes / 1024d).ToString("0 KB", CultureInfo.InvariantCulture);
        return bytes.ToString("0 B", CultureInfo.InvariantCulture);
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 1) return $"{span.TotalMilliseconds:0}ms";
        if (span.TotalMinutes < 1) return $"{span.TotalSeconds:0.0}s";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m {span.Seconds:00}s";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{(int)span.TotalDays}d {span.Hours:00}h";
    }

    // For a narrow tile: one unit only. "48m ago" fits where "48m 36s ago" wraps onto three lines,
    // and nobody reading a deploy age cares about the seconds.
    public static string CoarseAge(DateTimeOffset? stamp, DateTimeOffset now)
    {
        if (stamp is null) return "never";
        var span = now - stamp.Value;
        if (span < TimeSpan.Zero) return "just now";
        if (span.TotalMinutes < 1) return $"{span.TotalSeconds:0}s ago";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    // "3 hours ago" is what a person asks of a backup or a deploy; an absolute UTC stamp makes them
    // do the subtraction, and on a UTC server that subtraction is where the mistake happens.
    public static string Age(DateTimeOffset? stamp, DateTimeOffset now)
    {
        if (stamp is null) return "never";
        var span = now - stamp.Value;
        if (span < TimeSpan.Zero) return "just now";
        return Duration(span) + " ago";
    }

    public static string Percent(double fraction) =>
        (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
