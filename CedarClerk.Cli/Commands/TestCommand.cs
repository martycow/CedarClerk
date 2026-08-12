using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Parsing;
using CedarClerk.Cli.Pipelines;
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

// The test run, with every result drawn as it lands (Marty's ask; ADR-118 decision 8).
//
// Since ADR-119 the phases are started by TestPipeline rather than by a script printing headers this
// then parsed back out, so the grid can no longer be desynchronised by a runner that happens to
// print "=== something ===". What has not changed, and must not: the verdict is each phase's exit
// code. The grid can be short - a runner that changes its output format quietly stops being parsed -
// but it can never turn a red run green, because it is not what decides.
public sealed class TestCommand : AsyncCommand<TestSettings>
{
    private const int MaxGlyphs = 4000;

    protected override Task<int> ExecuteAsync(CommandContext context, TestSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(TestSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        var options = new TestOptions(settings.Smoke, settings.Backend, settings.Frontend);
        var pipeline = new TestPipeline(session.Runner, session.Config);

        var tracker = new TestRunTracker(TestPipeline.Phases(options));
        var watch = Stopwatch.StartNew();
        var gate = new object();

        void Feed(string line) { lock (gate) tracker.Feed(line); }

        // Keyed by the selection, because -Backend and -Smoke are not the same suite and one
        // remembered number for all of them would be wrong for every run but one.
        var memoryKey = MemoryKey(options);
        var expected = RunMemory.Expected(memoryKey);

        IReadOnlyList<TestPhaseResult> results;

        if (session.DryRun || session.Json)
        {
            results = await pipeline.RunAsync(options, Feed, cancellationToken);
            tracker.Finish();
            if (session.Json)
                session.WriteJson(new
                {
                    exitCode = ExitCode(results),
                    passed = tracker.Passed,
                    failed = tracker.Failed,
                    skipped = tracker.Skipped,
                    phases = results.Select(p => new { p.Name, p.ExitCode, seconds = p.Elapsed.TotalSeconds })
                });
            return ExitCode(results);
        }

        // Live hides the cursor to take over the screen, and on Windows that throws outright when
        // output is redirected ("The handle is invalid") - so `cedar test` from a script used to die
        // before running a single test. There is nothing to animate into a pipe anyway: run it
        // plainly and write the finished grid once.
        if (Console.IsOutputRedirected || !session.Console.Profile.Capabilities.Interactive)
        {
            results = await pipeline.RunAsync(options, Feed, cancellationToken);
            tracker.Finish();
            session.Console.Write(Frame(session, tracker, watch.Elapsed, running: false, expected));
        }
        else
        {
            IReadOnlyList<TestPhaseResult> collected = Array.Empty<TestPhaseResult>();

            await session.Console.Live(new Markup("[grey]starting…[/]")).StartAsync(async ctx =>
            {
                var run = pipeline.RunAsync(options, Feed, cancellationToken);

                while (!run.IsCompleted)
                {
                    lock (gate) ctx.UpdateTarget(Frame(session, tracker, watch.Elapsed, running: true, expected));
                    ctx.Refresh();
                    // ~12 frames a second: fast enough to look live, slow enough that redrawing
                    // several hundred glyphs does not become the reason the run feels slow.
                    await Task.Delay(80);
                }

                collected = await run;
                tracker.Finish();
                lock (gate) ctx.UpdateTarget(Frame(session, tracker, watch.Elapsed, running: false, expected));
                ctx.Refresh();
            });

            results = collected;
        }

        RunMemory.Remember(memoryKey, tracker.Total);
        Verdict(session, tracker, results, watch.Elapsed);
        return ExitCode(results);
    }

    // Any phase failing fails the run, and the code reported is that phase's own — a caller that
    // pipes this into something else gets the runner's verdict rather than a flattened 1.
    private static int ExitCode(IReadOnlyList<TestPhaseResult> results) =>
        results.FirstOrDefault(r => !r.Ok)?.ExitCode ?? 0;

    private static string MemoryKey(TestOptions options)
    {
        var parts = new List<string>();
        if (options.Smoke) parts.Add("smoke");
        if (options.Backend) parts.Add("backend");
        if (options.Frontend) parts.Add("frontend");
        return parts.Count == 0 ? "full" : string.Join('+', parts);
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

        // A suite this size fits; a runaway one gets counted instead of drawn, so the frame can never
        // grow past the screen and start scrolling the live region away.
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

    // Numbers beside every colour, as everywhere else in this tool. The cell glyphs are repeated here
    // rather than the tick and the cross, so the counters read as a key to the grid above.
    private static string Counters(TestRunTracker tracker, TimeSpan elapsed, Glyphs glyphs) =>
        $"[{Palette.Hex(Palette.Ok)}]{glyphs.CellPassed} {tracker.Passed} passed[/]   " +
        $"[{(tracker.Failed > 0 ? Palette.Hex(Palette.Danger) : "grey35")}]{glyphs.CellFailed} {tracker.Failed} failed[/]   " +
        $"[grey35]{glyphs.CellSkipped} {tracker.Skipped} skipped[/]   " +
        $"[grey]{Format.Duration(elapsed)}[/]";

    private static void Verdict(
        Session session, TestRunTracker tracker, IReadOnlyList<TestPhaseResult> results, TimeSpan elapsed)
    {
        var glyphs = session.Glyphs;
        session.Console.WriteLine();

        var broken = results.Where(r => !r.Ok).ToList();

        if (broken.Count == 0)
        {
            session.Console.MarkupLine(
                $"[{Palette.Hex(Palette.Ok)}]{glyphs.Ok} all green[/] [grey]- {tracker.Total} tests in {Format.Duration(elapsed)}[/]");
        }
        else
        {
            session.Console.MarkupLine(
                $"[{Palette.Hex(Palette.Danger)}]{glyphs.Bad} {broken.Count} phase(s) failed[/] [grey]- in {Format.Duration(elapsed)}[/]");
            // Said out loud, because a green-looking grid above a failed run is exactly the confusion
            // this note exists to prevent.
            if (tracker.Failed == 0)
                session.Note("no individual test was reported as failed - the failure is in a step that does not name tests.");
        }

        // One row per phase, with its own time and its own verdict: "the frontend is fine and the
        // smoke suite is not" is the answer, and a single total cannot give it.
        foreach (var phase in results)
        {
            var counted = tracker.Phases.FirstOrDefault(p => p.Name == phase.Name)?.Events.Count ?? 0;
            var mark = phase.Ok ? glyphs.Ok : glyphs.Bad;
            var colour = phase.Ok ? Palette.Ok : Palette.Danger;

            session.Console.MarkupLine(
                $"  [{Palette.Hex(colour)}]{mark}[/] [grey]{Markup.Escape(phase.Name),-42}[/] " +
                $"[grey35]{(counted == 0 ? "no results parsed" : counted + " results"),-18} {Format.Duration(phase.Elapsed)}[/]");
        }
    }

    private static string Truncate(string text, int max) =>
        max <= 1 || text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "…";
}
