using System.ComponentModel;
using CedarClerk.Cli.Pipelines;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Commands;

public sealed class BuildSettings : CedarSettings
{
    [CommandOption("--no-desktop")]
    [Description("Skip the Electron shell (much faster - no Electron download).")]
    public bool NoDesktop { get; init; }

    [CommandOption("--desktop-only")]
    [Description("Rebuild the shell against the Angular output already there.")]
    public bool DesktopOnly { get; init; }

    [CommandOption("--installer")]
    [Description("Also produce CedarClerk-Setup-<version>.exe.")]
    public bool Installer { get; init; }

    [CommandOption("--run")]
    [Description("Open the shell when it is built.")]
    public bool Run { get; init; }
}

// What Scripts/build.ps1 used to be, and the only way to do it since ADR-119.
//
// It asks first, which looks excessive for a build until you notice what it removes on the way:
// publish/ and CedarClerk.Desktop/server/ are deleted outright, and the shell's package.json version
// is rewritten. Small, local and recoverable - hence a prompt rather than the full treatment the
// rules reserve for the server.
public sealed class BuildCommand : AsyncCommand<BuildSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, BuildSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(BuildSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        var version = GitGuard.VersionOnDisk(session.Config.RepoRoot);
        var options = new BuildOptions(settings.NoDesktop, settings.DesktopOnly, settings.Installer, settings.Run);

        if (!session.DryRun && !session.Confirm(
                "This deletes publish/ and CedarClerk.Desktop/server/, and syncs the shell's version. Continue?"))
        {
            session.Note("nothing was done.");
            return 1;
        }

        var board = new StageBoard(session.Console, session.Glyphs, "build")
        {
            Header = $"[grey]build[/]  [{Palette.Hex(Palette.Accent)}]v{version}[/]  " +
                     $"[grey35]{Markup.Escape(session.Config.RepoRoot)}[/]"
        };
        foreach (var (name, detail) in BuildPipeline.Plan(options)) board.Plan(name, detail);

        try
        {
            await board.RunAsync(async _ =>
            {
                await new BuildPipeline(session.Runner, session.Config, session.Files)
                    .RunAsync(board, options, version, cancellationToken);
                return 0;
            }, cancellationToken);
        }
        catch (PipelineStop stop)
        {
            session.Console.WriteLine();
            session.Problem(stop.Message);
            foreach (var line in stop.State) session.Console.MarkupLine($"      [grey]{Markup.Escape(line)}[/]");
            foreach (var hint in stop.Hints)
                session.Console.MarkupLine($"        [{Palette.Hex(Palette.Warn)}]{Markup.Escape(hint)}[/]");
            return 1;
        }

        Summary(session, board, version, options);

        if (!settings.Run) return 0;

        session.Console.WriteLine();
        return await OpenCommand.RunAsync(new OpenSettings
        {
            DryRun = settings.DryRun, Json = settings.Json, NoUnicode = settings.NoUnicode,
            AssumeYes = settings.AssumeYes, NoLogo = settings.NoLogo, Where = "desktop"
        }, cancellationToken);
    }

    private static void Summary(Session session, StageBoard board, string version, BuildOptions options)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[grey35]version [/] [{Palette.Hex(Palette.Accent)}]v{version}[/]"),
            new Markup($"[grey35]took    [/] [{Palette.Hex(Palette.Text)}]{Format.Duration(board.Total)}[/]")
        };

        if (!options.DesktopOnly)
            rows.Add(new Markup($"[grey35]server  [/] [grey]{Markup.Escape(session.Config.PublishDir)}[/]"));

        if (!options.NoDesktop)
            rows.Add(new Markup($"[grey35]shell   [/] [grey]{CliConsts.BinaryName} open desktop[/]"));

        if (options.Installer)
        {
            var installer = Path.Combine(session.Config.DesktopDistDir, $"CedarClerk-Setup-{version}.exe");
            rows.Add(new Markup($"[grey35]installer[/] [grey]{Markup.Escape(File.Exists(installer) ? installer : "not produced")}[/]"));
        }

        session.Console.WriteLine();
        session.Console.Write(Ui.Panel(session.Glyphs, $"{session.Glyphs.Ok} built", new Rows(rows), Palette.Ok));
    }
}
