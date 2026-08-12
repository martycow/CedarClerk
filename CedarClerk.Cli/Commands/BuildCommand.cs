using System.ComponentModel;
using System.Diagnostics;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

public sealed class BuildSettings : CedarSettings
{
    [CommandOption("--no-desktop")]
    [Description("Skip the Electron shell (much faster).")]
    public bool NoDesktop { get; init; }

    [CommandOption("--installer")]
    [Description("Also produce the installer.")]
    public bool Installer { get; init; }
}

// Scripts/build.ps1, with its output kept to one live line instead of several thousand.
//
// It asks first, which looks excessive for a build until you notice what build.ps1 removes on the
// way: publish/ and CedarClerk.Desktop/server/ are deleted outright, and it rewrites the shell's
// package.json version. That is small, local and recoverable — hence a prompt rather than the full
// destructive treatment the rules reserve for the server.
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

        var arguments = new List<string>();
        if (settings.NoDesktop) arguments.Add("-NoDesktop");
        if (settings.Installer) arguments.Add("-Installer");

        if (!session.DryRun && !session.Confirm(
                "build.ps1 deletes publish/ and CedarClerk.Desktop/server/, and syncs the shell's version. Continue?"))
        {
            session.Note("nothing was done.");
            return 1;
        }

        var watch = Stopwatch.StartNew();
        var lastLine = "";
        CommandResult result = CommandResult.Empty();

        await session.Console
            .Status()
            .Spinner(session.Glyphs.Spinner)
            .SpinnerStyle(new Style(Palette.Accent))
            .StartAsync("building…", async ctx =>
            {
                result = await session.Runner.RunLocalStreamingAsync(
                    Shell.PowerShell(),
                    Shell.ScriptArgs(session.Config.BuildScript, arguments.ToArray()),
                    session.Config.RepoRoot,
                    line =>
                    {
                        var text = line.Trim();
                        if (text.Length == 0) return;
                        lastLine = text.Length > 90 ? text[..90] + "…" : text;
                        ctx.Status(Markup.Escape(lastLine));
                    },
                    cancellationToken);
            });

        if (session.Json)
        {
            session.WriteJson(new { exitCode = result.ExitCode, seconds = watch.Elapsed.TotalSeconds, lastLine });
            return result.ExitCode;
        }

        if (result.Ok)
            session.Console.MarkupLine(
                $"[{Palette.Hex(Palette.Ok)}]{session.Glyphs.Ok} built[/] [grey]- v{CedarClerk.Core.Consts.CurrentVersion} in {Format.Duration(watch.Elapsed)}[/]");
        else
        {
            session.Problem($"the build failed (exit code {result.ExitCode}).");
            foreach (var line in result.Lines.Where(l => l.Trim().Length > 0).TakeLast(12))
                session.Note(line);
        }

        return result.ExitCode;
    }
}
