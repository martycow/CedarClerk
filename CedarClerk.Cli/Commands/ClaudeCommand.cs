using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// Opens a second terminal in the repository with `claude /remote-control` running. A separate window
// because both programs read the keyboard; it is launched and forgotten. When it cannot run it
// prints the command instead — off Windows there is no portable "open a terminal".
//
// Never through `wt`: Windows Terminal re-splits the arguments and loses the quotes, then looks for
// an executable literally named `pwsh -NoExit -Command claude` (0x80070002). Nothing is lost —
// Windows 11 hosts the plain console in Windows Terminal anyway.
public sealed class ClaudeCommand : AsyncCommand<CedarSettings>
{
    private const string Prompt = "/remote-control";

    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    public static async Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        var repo = session.Config.RepoRoot;
        var shell = Shell.PowerShell();

        // Not fatal: the window is opened either way, and its own error message about a missing
        // `claude` is clearer than anything guessed from here. Saying it early only saves the
        // second where you wonder whether the window opened at all.
        if (!Shell.OnPath("claude"))
            session.Note("'claude' is not on PATH - the new window will say so too.");

        var (exe, args) = Launch(shell, repo);

        if (!OperatingSystem.IsWindows())
        {
            session.Problem("opening a terminal is Windows-only here - run this yourself:");
            session.Note($"cd \"{repo}\" && claude {Prompt}");
            return 2;
        }

        session.Console.MarkupLine(
            $"[{Palette.Hex(Palette.Accent)}]{session.Glyphs.Arrow}[/] claude {Prompt} " +
            $"[grey]in[/] [{Palette.Hex(Palette.Muted)}]{Markup.Escape(repo)}[/]");

        var result = await session.Runner.LaunchDetachedAsync(exe, args, repo, cancellationToken);
        if (result.Ok) return 0;

        session.Problem($"could not open a terminal: {result.StdErr.Trim()}");
        return 1;
    }

    // Set-Location rather than trusting the launched process to inherit a working directory: the
    // runner does set one, but a shell profile is free to move the session somewhere else before
    // the -Command runs, and this is the last thing to run.
    internal static (string Exe, string Args) Launch(string shell, string repo) =>
        (shell, $"-NoExit -Command \"Set-Location '{repo.Replace("'", "''")}'; claude {Prompt}\"");
}
