using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Parsing;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Commands;

public sealed class TestSettings : CedarSettings
{
    [CommandOption("-s|--smoke")]
    [Description("Also run the Playwright smoke suite against a scratch database.")]
    public bool Smoke { get; init; }

    [CommandOption("-b|--backend")]
    [Description("Backend only.")]
    public bool Backend { get; init; }

    [CommandOption("-f|--frontend")]
    [Description("Frontend only.")]
    public bool Frontend { get; init; }
}

// Scripts/test.ps1, with every result drawn as it lands (Marty's ask; ADR-118 decision 8).
//
// The verdict is the script's exit code and nothing else. The grid can be short — a runner that
// changes its output format quietly stops being parsed — but it can never turn a red run green,
// because it is not what decides.
public sealed class TestCommand : AsyncCommand<TestSettings>
{
    private const int MaxGlyphs = 4000;

    // The step names Scripts/test.ps1 passes to Invoke-Step. Anything else printing the same header
    // shape belongs to the step that is running, not beside it.
    private static readonly string[] Steps =
    {
        "Backend (dotnet test)",
        "Frontend units (vitest)",
        "Contrast contract",
        "Smoke (Playwright, isolated database)"
    };

    protected override Task<int> ExecuteAsync(CommandContext context, TestSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(TestSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        var arguments = new List<string> { "-Detailed" };
        if (settings.Smoke) arguments.Add("-Smoke");
        if (settings.Backend) arguments.Add("-Backend");
        if (settings.Frontend) arguments.Add("-Frontend");

        var tracker = new TestRunTracker(Steps);
        var watch = Stopwatch.StartNew();
        var gate = new object();

        // Keyed by the selection, because -Backend and -Smoke are not the same suite and one
        // remembered number for all of them would be wrong for every run but one.
        var memoryKey = arguments.Count == 1
            ? "full"
            : string.Join('+', arguments.Skip(1).Select(flag => flag.TrimStart('-').ToLowerInvariant()));
        var expected = RunMemory.Expected(memoryKey);

        var exe = Shell.PowerShell();
        var args = Shell.ScriptArgs(session.Config.TestScript, arguments.ToArray());

        if (session.DryRun || session.Json)
        {
            var plain = await session.Runner.RunLocalStreamingAsync(
                exe, args, session.Config.RepoRoot, line => { lock (gate) tracker.Feed(line); }, cancellationToken);
            tracker.Finish();
            if (session.Json)
                session.WriteJson(new
                {
                    exitCode = plain.ExitCode,
                    passed = tracker.Passed,
                    failed = tracker.Failed,
                    skipped = tracker.Skipped,
                    phases = tracker.Phases.Select(p => new { p.Name, count = p.Events.Count })
                });
            return plain.ExitCode;
        }

        CommandResult result = CommandResult.Empty();

        // Live hides the cursor to take over the screen, and on Windows that throws outright when
        // output is redirected ("The handle is invalid") - so a `cedar test` from a script used to
        // die before running a single test. There is nothing to animate into a pipe anyway: run it
        // plainly and write the finished grid once.
        if (Console.IsOutputRedirected || !session.Console.Profile.Capabilities.Interactive)
        {
            result = await session.Runner.RunLocalStreamingAsync(
                exe, args, session.Config.RepoRoot, line => { lock (gate) tracker.Feed(line); }, cancellationToken);
            tracker.Finish();
            session.Console.Write(Frame(session, tracker, watch.Elapsed, running: false, expected));

            RunMemory.Remember(memoryKey, tracker.Total);
            Verdict(session, tracker, result, watch.Elapsed);
            return result.ExitCode;
        }

        await session.Console.Live(new Markup("[grey]starting…[/]")).StartAsync(async ctx =>
        {
            var run = session.Runner.RunLocalStreamingAsync(
                exe, args, session.Config.RepoRoot,
                line => { lock (gate) tracker.Feed(line); },
                cancellationToken);

            while (!run.IsCompleted)
            {
                lock (gate) ctx.UpdateTarget(Frame(session, tracker, watch.Elapsed, running: true, expected));
                ctx.Refresh();
                // ~12 frames a second: fast enough to look live, slow enough that redrawing several
                // hundred glyphs does not become the reason the run feels slow.
                await Task.Delay(80);
            }

            result = await run;
            tracker.Finish();
            lock (gate) ctx.UpdateTarget(Frame(session, tracker, watch.Elapsed, running: false, expected));
            ctx.Refresh();
        });

        RunMemory.Remember(memoryKey, tracker.Total);
        Verdict(session, tracker, result, watch.Elapsed);
        return result.ExitCode;
    }

    // internal so the grid itself can be asserted on: the field of empty cells only exists while a
    // run is in flight, which is exactly the state a live screenshot cannot be taken of.
    internal static IRenderable Frame(Session session, TestRunTracker tracker, TimeSpan elapsed, bool running, int expected)
    {
        var glyphs = session.Glyphs;
        var rows = new List<IRenderable>();

        var width = Math.Max(20, session.Console.Profile.Width - 8);
        var events = tracker.AllEvents.ToList();

        // The field is as wide as the run is expected to be: empty cells for everything not reported
        // yet, filled ones behind them. While the run is going that makes it a bar filling up rather
        // than a list appending. The final frame drops back to the real count, so a remembered number
        // that turned out wrong leaves nothing behind.
        var cells = running ? Math.Max(expected, events.Count) : events.Count;

        // A suite this size fits; a runaway one gets counted instead of drawn, so the frame can
        // never grow past the screen and start scrolling the live region away.
        if (cells <= MaxGlyphs)
        {
            var grid = new StringBuilder();
            for (var i = 0; i < cells; i++)
            {
                if (i > 0 && i % width == 0) grid.Append('\n');
                grid.Append(i < events.Count ? Glyph(events[i].Outcome, glyphs) : Pending(glyphs));
            }
            if (cells == 0) grid.Append($"[grey]{(running ? "waiting for the first result…" : "no results")}[/]");
            rows.Add(new Markup(grid.ToString()));
        }
        else
        {
            rows.Add(new Markup($"[grey]{events.Count} results (too many to draw individually)[/]"));
        }

        rows.Add(new Markup(""));
        rows.Add(new Markup(Counters(tracker, elapsed, glyphs)));

        var phase = tracker.Phases.LastOrDefault();
        if (phase is not null)
            rows.Add(new Markup(
                $"[grey]{Markup.Escape(phase.Name)}[/] [grey35]{Markup.Escape(Truncate(tracker.LastLine, session.Console.Profile.Width - 20))}[/]"));

        var failures = tracker.Failures.Take(5).ToList();
        foreach (var failure in failures)
            rows.Add(new Markup($"[{Palette.Hex(Palette.Danger)}]{glyphs.Bad}[/] [grey]{Markup.Escape(Truncate(failure.Name, 100))}[/]"));

        return new Panel(new Rows(rows))
        {
            Border = session.Glyphs.Border,
            BorderStyle = new Style(running ? Palette.Accent : tracker.Failed > 0 ? Palette.Danger : Palette.Ok),
            Header = new PanelHeader($" [{Palette.Hex(Palette.Accent)}]tests[/] "),
            Padding = new Padding(1, 0, 1, 0)
        };
    }

    private static string Glyph(TestOutcome outcome, Glyphs glyphs) => outcome switch
    {
        TestOutcome.Passed => $"[{Palette.Hex(Palette.Ok)}]{glyphs.CellPassed}[/]",
        TestOutcome.Failed => $"[{Palette.Hex(Palette.Danger)}]{glyphs.CellFailed}[/]",
        _ => $"[grey35]{glyphs.CellSkipped}[/]"
    };

    private static string Pending(Glyphs glyphs) => $"[grey35]{glyphs.CellPending}[/]";

    // Numbers beside every colour, as everywhere else in this tool. The cell glyphs are repeated
    // here rather than the tick and the cross, so the counters read as a key to the grid above.
    private static string Counters(TestRunTracker tracker, TimeSpan elapsed, Glyphs glyphs) =>
        $"[{Palette.Hex(Palette.Ok)}]{glyphs.CellPassed} {tracker.Passed} passed[/]   " +
        $"[{(tracker.Failed > 0 ? Palette.Hex(Palette.Danger) : "grey35")}]{glyphs.CellFailed} {tracker.Failed} failed[/]   " +
        $"[grey35]{glyphs.CellSkipped} {tracker.Skipped} skipped[/]   " +
        $"[grey]{Format.Duration(elapsed)}[/]";

    private static void Verdict(Session session, TestRunTracker tracker, CommandResult result, TimeSpan elapsed)
    {
        var glyphs = session.Glyphs;
        session.Console.WriteLine();

        if (result.Ok)
        {
            session.Console.MarkupLine(
                $"[{Palette.Hex(Palette.Ok)}]{glyphs.Ok} all green[/] [grey]- {tracker.Total} tests in {Format.Duration(elapsed)}[/]");
        }
        else
        {
            session.Console.MarkupLine(
                $"[{Palette.Hex(Palette.Danger)}]{glyphs.Bad} the run failed[/] [grey]- exit code {result.ExitCode}[/]");
            // Said out loud, because a green-looking grid above a failed run is exactly the
            // confusion this note exists to prevent.
            if (tracker.Failed == 0)
                session.Note("no individual test was reported as failed - the failure is in the script or a step that does not name tests.");
        }

        foreach (var phase in tracker.Phases)
        {
            var counts = phase.Events.Count == 0 ? "no results parsed" : $"{phase.Events.Count} results";
            session.Console.MarkupLine($"  [grey]{Markup.Escape(phase.Name),-42}[/] [grey35]{counts}[/]");
        }
    }

    private static string Truncate(string text, int max) =>
        max <= 1 || text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "…";
}
