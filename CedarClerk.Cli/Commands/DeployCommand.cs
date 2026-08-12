using System.ComponentModel;
using CedarClerk.Cli.Pipelines;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Commands;

public sealed class DeploySettings : CedarSettings
{
    [CommandOption("--desktop")]
    [Description("Also build the installer and publish it for self-update.")]
    public bool Desktop { get; init; }

    [CommandOption("--skip-build")]
    [Description("Ship what is already in publish/ - continues an interrupted upload.")]
    public bool SkipBuild { get; init; }

    [CommandOption("--rollback")]
    [Description("Put the previous release back and start it.")]
    public bool Rollback { get; init; }

    [CommandOption("--force")]
    [Description("Turn the branch and clean-tree refusals into warnings.")]
    public bool Force { get; init; }

    [CommandOption("--preflight")]
    [Description("Run the checks and stop, without deploying anything.")]
    public bool PreflightOnly { get; init; }

    [CommandOption("--retries")]
    [Description("How many times a dropped upload is resumed. Default 5.")]
    public int Retries { get; init; } = 5;
}

// The deploy, run from here (ADR-119 decision 2).
//
// ADR-118 refused to let this command deploy, and the reason was specific: deploy.ps1 had no safe
// stopping point before the swap, so taking it to the last step would have meant cutting the script
// open in the same session that changed the interface calling it. That reason is spent — the logic
// is C# now, so the interface and the thing it drives change together, which was the condition.
//
// What survives from that decision is the part that was never about implementation: a person starts
// the deploy. The checks run, the version pair is printed, and then it asks, with the default set to
// no (.claude/rules/destructive-operations.md). --preflight keeps the old behaviour exactly.
public sealed class DeployCommand : AsyncCommand<DeploySettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, DeploySettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(DeploySettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        var version = GitGuard.VersionOnDisk(session.Config.RepoRoot);
        var options = new DeployOptions(settings.SkipBuild, settings.Desktop, settings.Rollback,
            settings.Force, Math.Clamp(settings.Retries, 1, 20));

        var live = await session.Console
            .Status()
            .Spinner(session.Glyphs.Spinner)
            .SpinnerStyle(new Style(Palette.Accent))
            .StartAsync("asking production what it is running…",
                _ => session.Health.GetAsync(session.Config.HealthUrl, cancellationToken));

        var liveVersion = live.Answered ? live.Version : "";
        var pipeline = new DeployPipeline(session.Runner, session.Health, session.Config, session.Files);

        session.Console.Write(Header(session, version, liveVersion, options));

        if (settings.PreflightOnly)
            return await PreflightOnlyAsync(session, pipeline, options, version, liveVersion, cancellationToken);

        // The one question this command asks. Everything after it stops production for a second.
        var question = options.Rollback
            ? "Roll production back to the previous release? The site goes down for the swap."
            : $"Deploy v{version} to {session.Config.Host}? The service stops for about a second, and the blog with it.";

        if (!session.DryRun && !session.Confirm(question))
        {
            session.Note("nothing was done.");
            return 1;
        }

        var board = new StageBoard(session.Console, session.Glyphs, options.Rollback ? "rollback" : "deploy")
        {
            Header = HeaderLine(session, version, liveVersion, options)
        };
        foreach (var (name, detail) in DeployPipeline.Plan(options)) board.Plan(name, detail);

        DeployOutcome? outcome = null;
        RollbackOutcome? rolled = null;

        try
        {
            await board.RunAsync(async _ =>
            {
                if (options.Rollback) rolled = await pipeline.RollbackAsync(board, options, cancellationToken);
                else outcome = await pipeline.RunAsync(board, options, version, liveVersion, cancellationToken);
                return 0;
            }, cancellationToken);
        }
        catch (PipelineStop stop)
        {
            Failure(session, stop);
            return 1;
        }

        if (rolled is not null) RolledBack(session, rolled);
        if (outcome is not null) Summary(session, board, outcome);
        return 0;
    }

    private static async Task<int> PreflightOnlyAsync(
        Session session, DeployPipeline pipeline, DeployOptions options, string version, string liveVersion,
        CancellationToken cancellationToken)
    {
        var board = new StageBoard(session.Console, session.Glyphs, "preflight")
        {
            Header = HeaderLine(session, version, liveVersion, options)
        };
        board.Plan(DeployPipeline.StagePreflight, "git, local tools, server state");

        try
        {
            await board.RunAsync(async _ =>
            {
                await pipeline.PreflightAsync(board, options, version, liveVersion, cancellationToken);
                return 0;
            }, cancellationToken);
        }
        catch (PipelineStop stop)
        {
            Failure(session, stop);
            return 1;
        }

        session.Console.WriteLine();
        session.Console.Write(new Panel(new Markup(
                $"[{Palette.Hex(Palette.Text)}]{CliConsts.BinaryName} deploy{Flags(options)}[/]"))
        {
            Border = session.Glyphs.Border,
            BorderStyle = new Style(Palette.Accent),
            Header = new PanelHeader($" [{Palette.Hex(Palette.Accent)}]ready - this is the command[/] "),
            Padding = new Padding(1, 0, 1, 0)
        });
        return 0;
    }

    private static string Flags(DeployOptions options)
    {
        var parts = new List<string>();
        if (options.SkipBuild) parts.Add(" --skip-build");
        if (options.Desktop) parts.Add(" --desktop");
        if (options.Force) parts.Add(" --force");
        return string.Concat(parts);
    }

    // "v0.11.0 → v0.11.0" is not a deploy, it is a rebuild, and reading it as an upgrade is exactly
    // the confusion a version pair in the header exists to prevent.
    private static string HeaderLine(Session session, string version, string liveVersion, DeployOptions options)
    {
        var pair = liveVersion == version
            ? $"[{Palette.Hex(Palette.Ok)}]v{version}[/] [grey]already live - this ships the same version[/]"
            : $"[{Palette.Hex(Palette.Warn)}]{(liveVersion.Length > 0 ? "v" + liveVersion : "down")}[/] " +
              $"[grey]{session.Glyphs.Arrow}[/] [{Palette.Hex(Palette.Accent)}]v{version}[/]";

        var what = options.Rollback ? "rollback" : options.SkipBuild ? "deploy (no build)" : "deploy";
        return $"[grey]{what}[/]  {pair}  [grey35]{Markup.Escape(session.Config.Host)}[/]";
    }

    private static IRenderable Header(Session session, string version, string liveVersion, DeployOptions options)
    {
        var rows = new List<IRenderable>
        {
            new Markup(HeaderLine(session, version, liveVersion, options)),
            new Markup($"[grey35]target[/]  [grey]{Markup.Escape(session.Config.Host)}:{Markup.Escape(session.Config.RemoteAppDir)}[/]"),
            new Markup($"[grey35]health[/]  [grey]{Markup.Escape(session.Config.HealthUrl)}[/]")
        };

        if (liveVersion.Length == 0 && !options.Rollback)
            rows.Add(new Markup(
                $"[{Palette.Hex(Palette.Warn)}]{session.Glyphs.Warn} production is not answering right now - this run brings it back[/]"));

        return Ui.Panel(session.Glyphs, CliConsts.DisplayName.ToLowerInvariant(), new Rows(rows), Palette.Accent);
    }

    // Stop-Deploy's three parts, kept: what happened, what is true now, what to type next. The middle
    // one is the reason this is not just an error message.
    private static void Failure(Session session, PipelineStop stop)
    {
        session.Console.WriteLine();
        session.Problem(stop.Message);

        foreach (var line in stop.State)
            session.Console.MarkupLine($"      [grey]{Markup.Escape(line)}[/]");

        if (stop.Hints.Count == 0) return;

        session.Console.WriteLine();
        session.Console.MarkupLine("      [grey35]what to do:[/]");
        foreach (var hint in stop.Hints)
            session.Console.MarkupLine($"        [{Palette.Hex(Palette.Warn)}]{Markup.Escape(hint)}[/]");
    }

    private static void RolledBack(Session session, RollbackOutcome rolled)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[grey35]running [/] [{Palette.Hex(Palette.Ok)}]v{rolled.Running}[/]"),
            new Markup($"[grey35]LIVE    [/] " + (rolled.LiveTag is null
                ? $"[{Palette.Hex(Palette.Warn)}]removed - nothing here knows what is running now[/]"
                : $"[{Palette.Hex(Palette.Ok)}]{rolled.LiveTag}[/]"))
        };

        session.Console.WriteLine();
        session.Console.Write(Ui.Panel(session.Glyphs, $"{session.Glyphs.Ok} rolled back", new Rows(rows), Palette.Ok));
    }

    // The flight recorder: where the time actually went, proportionally, plus the four facts worth
    // reading afterwards. The bar is drawn from each step's share of the total, so a deploy that felt
    // slow can be answered with "the Angular build was 70% of it" rather than a shrug.
    private static void Summary(Session session, StageBoard board, DeployOutcome outcome)
    {
        var glyphs = session.Glyphs;
        var timed = board.Stages.Where(s => s.Elapsed > TimeSpan.Zero).ToList();
        var total = timed.Aggregate(TimeSpan.Zero, (sum, stage) => sum + stage.Elapsed);
        var column = timed.Count == 0 ? 8 : timed.Max(s => s.Name.Length);

        var rows = new List<IRenderable>();

        foreach (var stage in timed)
        {
            var share = total.TotalSeconds <= 0 ? 0 : stage.Elapsed.TotalSeconds / total.TotalSeconds;
            var bar = string.Concat(Enumerable.Repeat(glyphs.Full, (int)Math.Round(18 * share)));
            rows.Add(new Markup(
                $"[grey]{Markup.Escape(stage.Name.PadRight(column))}[/] " +
                $"[{Palette.Hex(Palette.Text)}]{Format.Duration(stage.Elapsed),8}[/]  " +
                $"[{Palette.Hex(Palette.AccentSoft)}]{bar}[/] [grey35]{share * 100:0}%[/]"));
        }

        rows.Add(new Text(" "));
        rows.Add(new Markup($"[grey35]total    [/] [{Palette.Hex(Palette.Text)}]{Format.Duration(total)}[/]"));
        rows.Add(new Markup($"[grey35]downtime [/] [{Palette.Hex(Palette.Ok)}]{outcome.DownMs}ms[/] [grey]- renaming two directories[/]"));
        rows.Add(new Markup($"[grey35]shipped  [/] [grey]{outcome.Files} files, {Format.Size(outcome.PublishBytes)} packed to {Format.Size(outcome.TarBytes)}[/]"));
        rows.Add(new Markup($"[grey35]running  [/] [{Palette.Hex(Palette.Ok)}]v{outcome.Running}[/] [grey]{Markup.Escape(session.Config.PublicBaseUrl)}[/]"));

        if (outcome.Live is not null)
            rows.Add(new Markup($"[grey35]LIVE     [/] [{Palette.Hex(Palette.Ok)}]{outcome.Live.Commit}[/]" +
                                (outcome.Live.Previous is null ? "" : $" [grey](was {outcome.Live.Previous}, kept as LIVE-PREV)[/]")));

        if (outcome.DesktopNote is not null)
            rows.Add(new Markup($"[grey35]desktop  [/] [{Palette.Hex(outcome.DesktopOk ? Palette.Ok : Palette.Warn)}]{Markup.Escape(outcome.DesktopNote)}[/]"));

        rows.Add(new Markup($"[grey35]rollback [/] [grey]{CliConsts.BinaryName} deploy --rollback[/]"));

        session.Console.WriteLine();
        session.Console.Write(Ui.Panel(session.Glyphs,
            $"{glyphs.Ok} deployed   {(outcome.LiveBefore.Length > 0 ? "v" + outcome.LiveBefore : "down")} {glyphs.Arrow} v{outcome.Version}",
            new Rows(rows), Palette.Ok));
    }
}
