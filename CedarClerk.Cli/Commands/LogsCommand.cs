using System.ComponentModel;
using CedarClerk.Cli.Parsing;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

public sealed class LogsSettings : CedarSettings
{
    [CommandOption("-n|--tail <LINES>")]
    [Description("How many lines to read. Bounded on purpose - the journal holds ~1.5M lines a day.")]
    public int Tail { get; init; } = CliConsts.DefaultLogLines;

    [CommandOption("-e|--errors")]
    [Description("Warnings and worse only.")]
    public bool ErrorsOnly { get; init; }

    [CommandOption("-f|--follow")]
    [Description("Keep the connection open and print new lines as they arrive. Ctrl+C to stop.")]
    public bool Follow { get; init; }

    [CommandOption("-s|--since <WHEN>")]
    [Description("journalctl's own syntax, e.g. '-1h', 'today', '2026-08-12 08:00'.")]
    public string Since { get; init; } = "";
}

// journalctl, read without sudo (ADR-118 decision 5 — the unit runs as the login user, so its
// entries belong to that user; the rules file used to say otherwise and was corrected).
public sealed class LogsCommand : AsyncCommand<LogsSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, LogsSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(LogsSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        var lines = Math.Clamp(settings.Tail, 1, CliConsts.MaxLogLines);
        var command = Build(settings, lines);

        if (settings.Follow)
        {
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += onCancel;

            session.Note($"following {CliConsts.ServiceName} - Ctrl+C to stop");
            try
            {
                await session.Runner.RunRemoteStreamingAsync(command,
                    line => Print(session, JournalParser.Parse(line)), cancellation.Token);
            }
            catch (OperationCanceledException) { }
            finally { Console.CancelKeyPress -= onCancel; }
            return 0;
        }

        var result = await session.Runner.RunRemoteAsync(command, cancellationToken);
        var parsed = JournalParser.Parse(result.StdOut);

        if (session.Json)
        {
            session.WriteJson(parsed.Select((line, index) => new
            {
                line.Timestamp,
                Level = JournalParser.EffectiveLevel(parsed, index).ToString(),
                line.Text
            }));
            return result.ExitCode;
        }

        if (parsed.Count == 0)
        {
            session.Note(settings.ErrorsOnly
                ? "nothing at warning level or above in that window."
                : "no lines came back.");
            if (result.StdErr.Trim().Length > 0) session.Problem(result.StdErr.Trim());
            return result.ExitCode;
        }

        session.Console.Write(Ui.Rule(session.Glyphs,
            $"[grey]{CliConsts.ServiceName} · last {parsed.Count} lines[/]", session.Console.Profile.Width));

        for (var i = 0; i < parsed.Count; i++)
            Write(session, parsed[i], JournalParser.EffectiveLevel(parsed, i));

        return result.ExitCode;
    }

    private static string Build(LogsSettings settings, int lines)
    {
        // -q suppresses the "you are not seeing messages from other users" hint, which is advice
        // about other units and reads like a permission error when it appears above real output.
        var parts = new List<string> { "journalctl", "-q", "-u", CliConsts.ServiceName, "--no-pager" };

        if (settings.ErrorsOnly) parts.Add("-p warning");
        if (settings.Since.Length > 0) parts.Add($"--since '{settings.Since.Replace("'", "")}'");

        if (settings.Follow) parts.Add("-f");
        // A follow still needs a starting window, or journalctl replays the entire journal first.
        parts.Add($"-n {lines}");

        return string.Join(' ', parts);
    }

    private static void Print(Session session, IReadOnlyList<LogLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
            Write(session, lines[i], JournalParser.EffectiveLevel(lines, i));
    }

    // Two levels are in play: the effective one decides the colour, so a stack trace stays red under
    // the line that introduced it, while the line's own level decides the tag — a continuation
    // repeating "info" four times would claim four log entries where there is one.
    private static void Write(Session session, LogLine line, LogLevel effective)
    {
        var colour = effective switch
        {
            LogLevel.Critical or LogLevel.Error => Palette.Danger,
            LogLevel.Warn => Palette.Warn,
            LogLevel.Debug or LogLevel.Trace => Palette.Faint,
            _ => Palette.Muted
        };

        var continuation = line.Level == LogLevel.Continuation;
        var tag = line.Level switch
        {
            LogLevel.Critical => "crit",
            LogLevel.Error => "fail",
            LogLevel.Warn => "warn",
            LogLevel.Debug => "dbug",
            LogLevel.Trace => "trce",
            LogLevel.Continuation => "    ",
            _ => "info"
        };

        // The level word sits beside the colour so a monochrome terminal loses nothing.
        session.Console.MarkupLine(
            $"[grey35]{Markup.Escape(continuation ? new string(' ', 15) : line.Timestamp.PadRight(15))}[/] " +
            $"[{Palette.Hex(colour)}]{tag}[/] " +
            $"{(continuation ? "  " : "")}" +
            $"[{Palette.Hex(continuation ? Palette.Muted : Palette.Text)}]{Markup.Escape(line.Text)}[/]");
    }
}
