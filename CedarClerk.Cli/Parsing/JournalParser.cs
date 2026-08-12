using System.Text.RegularExpressions;

namespace CedarClerk.Cli.Parsing;

public enum LogLevel { Trace, Debug, Info, Warn, Error, Critical, Continuation }

public sealed record LogLine(string Timestamp, LogLevel Level, string Text)
{
    public bool IsProblem => Level is LogLevel.Warn or LogLevel.Error or LogLevel.Critical;
}

// Turns journalctl output into something the log view can colour.
//
// Two layers have to come off. journald wraps every message in "Aug 12 08:17:44 host dotnet[123]: ",
// and inside that sits ASP.NET Core's own console format, which puts the level on one line
// ("warn: Some.Category[42]") and the message on the next, indented. So a continuation line is a
// real category of its own here, not a parse failure — it must inherit the colour of the line above
// or a stack trace turns into grey noise directly under the red line that introduced it.
public static class JournalParser
{
    private static readonly Regex SyslogPrefix = new(
        @"^(?<stamp>[A-Z][a-z]{2}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+\S+\s+[^:]+:\s?",
        RegexOptions.Compiled);

    private static readonly Regex LevelPrefix = new(
        @"^(?<level>trce|dbug|info|warn|fail|crit):\s",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<LogLine> Parse(string text)
    {
        var lines = new List<LogLine>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        var carried = LogLevel.Info;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            // Without -q journalctl prints a "you are not seeing messages from other users" notice.
            // It is advice, not a log line (see .claude/rules/production-environment.md).
            if (line.StartsWith("Hint:", StringComparison.Ordinal)) continue;
            if (line.StartsWith("      Users in groups", StringComparison.Ordinal)) continue;
            if (line.StartsWith("-- ", StringComparison.Ordinal)) continue;

            var stamp = "";
            var match = SyslogPrefix.Match(line);
            if (match.Success)
            {
                stamp = match.Groups["stamp"].Value;
                line = line[match.Length..];
            }

            var level = LevelPrefix.Match(line);
            if (level.Success)
            {
                carried = ToLevel(level.Groups["level"].Value);
                lines.Add(new LogLine(stamp, carried, line[level.Length..].Trim()));
            }
            else
            {
                lines.Add(new LogLine(stamp, LogLevel.Continuation, line.Trim()));
            }
        }

        return lines;
    }

    // A continuation belongs to whatever introduced it; this is what the view colours by.
    public static LogLevel EffectiveLevel(IReadOnlyList<LogLine> lines, int index)
    {
        for (var i = index; i >= 0; i--)
            if (lines[i].Level != LogLevel.Continuation) return lines[i].Level;
        return LogLevel.Info;
    }

    private static LogLevel ToLevel(string token) => token.ToLowerInvariant() switch
    {
        "trce" => LogLevel.Trace,
        "dbug" => LogLevel.Debug,
        "info" => LogLevel.Info,
        "warn" => LogLevel.Warn,
        "fail" => LogLevel.Error,
        "crit" => LogLevel.Critical,
        _ => LogLevel.Info
    };
}
