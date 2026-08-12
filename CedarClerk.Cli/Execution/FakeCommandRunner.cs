namespace CedarClerk.Cli.Execution;

// The runner the tests use. It ships in the CLI project rather than the test project so that both
// live off one definition of "what a canned answer looks like".
//
// Matching is by substring, longest pattern first: a probe script is one big command containing
// many little ones, and a test that wants to answer "df" should not have to reproduce the whole
// script verbatim to do it.
public sealed class FakeCommandRunner : ICommandRunner
{
    private readonly Dictionary<string, CommandResult> _answers = new();

    public List<string> RemoteCalls { get; } = new();
    public List<string> LocalCalls { get; } = new();
    public CommandResult Fallback { get; set; } = CommandResult.Empty();
    public IReadOnlyList<string> StreamLines { get; set; } = Array.Empty<string>();

    public FakeCommandRunner Answer(string containing, string stdout, int exitCode = 0)
    {
        _answers[containing] = new CommandResult(exitCode, stdout, "", TimeSpan.Zero);
        return this;
    }

    public Task<CommandResult> RunRemoteAsync(string command, CancellationToken ct)
    {
        RemoteCalls.Add(command);
        return Task.FromResult(Match(command));
    }

    public Task<CommandResult> RunLocalAsync(string exe, string args, CancellationToken ct)
    {
        LocalCalls.Add($"{exe} {args}");
        return Task.FromResult(Match($"{exe} {args}"));
    }

    public Task<CommandResult> RunLocalStreamingAsync(
        string exe, string args, string? workingDirectory, Action<string> onLine, CancellationToken ct)
    {
        LocalCalls.Add($"{exe} {args}");
        foreach (var line in StreamLines) onLine(line);
        return Task.FromResult(Match($"{exe} {args}"));
    }

    public Task<CommandResult> RunRemoteStreamingAsync(string command, Action<string> onLine, CancellationToken ct)
    {
        RemoteCalls.Add(command);
        foreach (var line in StreamLines) onLine(line);
        return Task.FromResult(Match(command));
    }

    private CommandResult Match(string command)
    {
        var key = _answers.Keys
            .Where(command.Contains)
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        return key is null ? Fallback : _answers[key];
    }
}
