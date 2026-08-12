using System.Globalization;

namespace CedarClerk.Cli.Parsing;

public sealed record SarSample(TimeSpan Time, double Value);

// Reads one named column out of sar's plain text (ADR-118: sysstat samples every 10 minutes, so
// this is the tool's only source of real history).
//
// The column is found by NAME from the header row, never by index. sar's layout differs between
// versions — kbavail was added to -r, and a machine without it would shift every field after it —
// so an index would read memory pressure off the wrong column and be believed.
public static class SarParser
{
    public static IReadOnlyList<SarSample> Parse(string text, string column)
    {
        var samples = new List<SarSample>();
        if (string.IsNullOrWhiteSpace(text)) return samples;

        int columnIndex = -1;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // "Linux 6.8.0 (host) 08/12/26 _x86_64_ (1 CPU)" — the banner, repeated per file.
            if (line.StartsWith("Linux", StringComparison.Ordinal)) continue;
            // Averages are a summary of the series, not a point in it.
            if (line.StartsWith("Average:", StringComparison.Ordinal)) continue;

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;

            // A header repeats whenever two days' files are concatenated, and the column may sit at
            // a different index in each — so the index is re-read every time one appears.
            var headerAt = Array.FindIndex(fields, f => f.Equals(column, StringComparison.OrdinalIgnoreCase));
            if (headerAt > 0)
            {
                columnIndex = headerAt;
                continue;
            }

            if (columnIndex < 0 || fields.Length <= columnIndex) continue;
            // The header carries a timestamp of its own and the same AM/PM field a data row has, so
            // the index taken from it already lines up — no correction here, deliberately.
            if (!TryParseTime(fields, out var time)) continue;

            if (!double.TryParse(fields[columnIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;

            samples.Add(new SarSample(time, value));
        }

        return samples;
    }

    // The inverse of an idle column: what the machine was actually doing.
    public static IReadOnlyList<SarSample> ParseCpuBusy(string text)
    {
        var idle = Parse(text, "%idle");
        return idle.Select(s => new SarSample(s.Time, Math.Clamp(100 - s.Value, 0, 100))).ToList();
    }

    private static bool TryParseTime(string[] fields, out TimeSpan time)
    {
        time = default;
        if (!TimeSpan.TryParseExact(fields[0], @"hh\:mm\:ss", CultureInfo.InvariantCulture, out time))
            return false;

        if (fields.Length > 1 && (fields[1] is "AM" or "PM"))
        {
            if (fields[1] == "PM" && time.Hours < 12) time = time.Add(TimeSpan.FromHours(12));
            if (fields[1] == "AM" && time.Hours == 12) time = time.Subtract(TimeSpan.FromHours(12));
        }
        return true;
    }
}
