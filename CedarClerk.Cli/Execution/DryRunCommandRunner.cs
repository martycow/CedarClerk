using Spectre.Console;

namespace CedarClerk.Cli.Execution;

// --dry-run: prints what would have run and returns success without touching anything.
//
// Everything a command does passes through ICommandRunner, so substituting this is the whole of the
// flag (ADR-118). No command contains an "if dry run" branch, which is the failure mode this
// arrangement exists to prevent: such a branch is eventually forgotten on exactly one path.
public sealed class DryRunCommandRunner : ICommandRunner
{
    private readonly IAnsiConsole _console;

    public DryRunCommandRunner(IAnsiConsole console) => _console = console;

    public Task<CommandResult> RunLocalAsync(string exe, string args, CancellationToken ct)
    {
        Print("local", $"{exe} {args}");
        return Task.FromResult(CommandResult.Empty());
    }

    public Task<CommandResult> RunRemoteAsync(string command, CancellationToken ct)
    {
        Print("ssh", command);
        return Task.FromResult(CommandResult.Empty());
    }

    public Task<CommandResult> RunLocalStreamingAsync(
        string exe, string args, string? workingDirectory, Action<string> onLine, CancellationToken ct)
    {
        Print("local", $"{exe} {args}");
        return Task.FromResult(CommandResult.Empty());
    }

    public Task<CommandResult> RunRemoteStreamingAsync(string command, Action<string> onLine, CancellationToken ct)
    {
        Print("ssh", command);
        return Task.FromResult(CommandResult.Empty());
    }

    public Task<CommandResult> StreamFileToRemoteAsync(
        string localPath, long offset, string remoteCommand, Action<long> onSent, CancellationToken ct)
    {
        Print("send", $"{localPath} (from byte {offset}) | ssh {remoteCommand}");
        return Task.FromResult(CommandResult.Empty());
    }

    public Task<CommandResult> LaunchDetachedAsync(string exe, string args, string? workingDirectory, CancellationToken ct)
    {
        Print("open", $"{exe} {args}");
        return Task.FromResult(CommandResult.Empty());
    }

    private void Print(string kind, string command)
    {
        // A multi-line remote script is one call but many lines; indenting the continuation keeps
        // the printed plan readable as a plan rather than as a wall.
        var lines = command.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        _console.MarkupLine($"[grey35]{kind,-5}[/] [grey]{Markup.Escape(lines[0])}[/]");
        foreach (var line in lines.Skip(1))
            _console.MarkupLine($"      [grey35]{Markup.Escape(line)}[/]");
    }
}
