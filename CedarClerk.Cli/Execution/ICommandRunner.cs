namespace CedarClerk.Cli.Execution;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, TimeSpan Duration)
{
    public bool Ok => ExitCode == 0;

    public static CommandResult Empty(int exitCode = 0) =>
        new(exitCode, "", "", TimeSpan.Zero);

    public IEnumerable<string> Lines =>
        StdOut.Split('\n').Select(l => l.TrimEnd('\r'));
}

// Every external call the tool makes goes through here — local processes and ssh alike.
//
// The point is not tidiness: --dry-run is implemented by swapping the implementation (ADR-118), so
// a command that reached the outside world any other way would quietly ignore the flag.
public interface ICommandRunner
{
    Task<CommandResult> RunLocalAsync(string exe, string args, CancellationToken ct);

    Task<CommandResult> RunRemoteAsync(string command, CancellationToken ct);

    // Added by ADR-118 decision 8: the test grid needs each result as it happens, not the whole
    // stdout at the end. Separate from RunLocalAsync so the ordinary path stays the simple one.
    Task<CommandResult> RunLocalStreamingAsync(
        string exe, string args, string? workingDirectory, Action<string> onLine, CancellationToken ct);

    // The same for the far side, which `logs --follow` needs: journalctl -f never returns, so a
    // buffered call would print nothing at all rather than late.
    Task<CommandResult> RunRemoteStreamingAsync(string command, Action<string> onLine, CancellationToken ct);

    // A window the tool opens and then stops owning: nothing is redirected, nothing is waited for,
    // and the exit code reported is only whether the launch itself succeeded.
    //
    // It has to be its own method rather than a flag on RunLocalAsync, because that one captures
    // stdout and blocks until exit — precisely the two things an interactive session the user is
    // about to type into must not do. It goes through the interface anyway so that --dry-run keeps
    // meaning "touch nothing", including "open no windows".
    Task<CommandResult> LaunchDetachedAsync(string exe, string args, string? workingDirectory, CancellationToken ct);
}
