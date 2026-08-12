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
    private readonly Dictionary<string, Queue<CommandResult>> _sequences = new();

    public List<string> RemoteCalls { get; } = new();
    public List<string> LocalCalls { get; } = new();
    public CommandResult Fallback { get; set; } = CommandResult.Empty();
    public IReadOnlyList<string> StreamLines { get; set; } = Array.Empty<string>();

    public FakeCommandRunner Answer(string containing, string stdout, int exitCode = 0)
    {
        _answers[containing] = new CommandResult(exitCode, stdout, "", TimeSpan.Zero);
        return this;
    }

    // A different answer each time the same question is asked. The resumable upload needs it: "how
    // many bytes are on the server" is asked before and after every attempt, and the whole loop is
    // about that number changing.
    public FakeCommandRunner AnswerInTurn(string containing, params string[] stdouts)
    {
        _sequences[containing] = new Queue<CommandResult>(
            stdouts.Select(text => new CommandResult(0, text, "", TimeSpan.Zero)));
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

    public List<string> Uploads { get; } = new();

    // The fake sends nothing and says it sent everything from the offset to the end of the file,
    // which is what lets a test drive the resume loop without a network or a real tarball.
    public Task<CommandResult> StreamFileToRemoteAsync(
        string localPath, long offset, string remoteCommand, Action<long> onSent, CancellationToken ct)
    {
        Uploads.Add($"{localPath}@{offset} -> {remoteCommand}");
        var total = File.Exists(localPath) ? new FileInfo(localPath).Length : offset;
        onSent(total);
        return Task.FromResult(Match(remoteCommand));
    }

    public List<string> LaunchedWindows { get; } = new();

    public Task<CommandResult> LaunchDetachedAsync(string exe, string args, string? workingDirectory, CancellationToken ct)
    {
        LaunchedWindows.Add($"{exe} {args}");
        return Task.FromResult(Match($"{exe} {args}"));
    }

    private CommandResult Match(string command)
    {
        var sequence = _sequences.Keys
            .Where(command.Contains)
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();

        // The last answer in a sequence repeats rather than running out: a test says how the state
        // changes and should not also have to count how many times it is looked at.
        if (sequence is not null)
        {
            var queue = _sequences[sequence];
            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }

        var key = _answers.Keys
            .Where(command.Contains)
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        return key is null ? Fallback : _answers[key];
    }
}
