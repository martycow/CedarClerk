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

    // The test grid needs each result as it happens, not the whole stdout at the end (ADR-118).
    Task<CommandResult> RunLocalStreamingAsync(
        string exe, string args, string? workingDirectory, Action<string> onLine, CancellationToken ct);

    // `logs --follow` needs this: journalctl -f never returns, so a buffered call prints nothing at
    // all rather than late.
    Task<CommandResult> RunRemoteStreamingAsync(string command, Action<string> onLine, CancellationToken ct);

    // How the release tarball crosses: `cat >> file` on the far side from the byte already there,
    // because scp cannot resume and this transfer has died at 90% before. onSent includes the offset,
    // so an upload that took three attempts draws one bar rather than three starting at zero.
    Task<CommandResult> StreamFileToRemoteAsync(
        string localPath, long offset, string remoteCommand, Action<long> onSent, CancellationToken ct);

    // A window the tool opens and stops owning. Its own method because RunLocalAsync captures stdout
    // and blocks until exit — the two things an interactive session must not do. Still through the
    // interface, so --dry-run keeps meaning "touch nothing", including "open no windows".
    Task<CommandResult> LaunchDetachedAsync(string exe, string args, string? workingDirectory, CancellationToken ct);
}
