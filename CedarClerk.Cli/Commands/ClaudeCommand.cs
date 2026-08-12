using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// Opens a second terminal sitting in the repository with `claude /remote-control` already running
// (Marty, 12.08.2026).
//
// A second window rather than a child of this one, and that is the only interesting decision here:
// both programs read the keyboard, so sharing a console would mean two readers fighting over every
// keystroke. The new session is launched and then forgotten — no pipes, no exit code worth waiting
// for, nothing this tool has to keep alive.
//
// It is a shortcut for something you could type yourself, so when it cannot run it prints the
// command instead of failing silently: on a machine that is not Windows there is no
// "open a terminal" that means anything portable, and printing beats guessing.
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

        var (exe, args) = Launch(shell, repo, Shell.OnPath("wt"));

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

    // Windows Terminal when it is there, because a bare pwsh started this way gets the old console
    // host and looks nothing like the shell Marty actually works in. `wt` splits its own arguments
    // on ';', which is why the fallback carries the Set-Location and this one does not — -d already
    // puts the tab in the right directory, and the command left over has no semicolon in it.
    //
    // Whether Windows Terminal is present is passed in rather than looked up here, so that both
    // branches can be tested on a machine that has only one of them.
    internal static (string Exe, string Args) Launch(string shell, string repo, bool windowsTerminal) =>
        windowsTerminal
            ? ("wt", $"-d \"{repo}\" {shell} -NoExit -Command \"claude {Prompt}\"")
            : (shell, $"-NoExit -Command \"Set-Location '{repo.Replace("'", "''")}'; claude {Prompt}\"");
}
