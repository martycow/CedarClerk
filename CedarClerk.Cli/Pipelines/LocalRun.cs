using System.Diagnostics;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using Spectre.Console;

namespace CedarClerk.Cli.Pipelines;

// Running one long local program as one step of a board.
//
// The interesting part is what happens to its output. `npm run build` and `dotnet publish` print
// thousands of lines, and the old scripts let all of them scroll past — which buried the plan, the
// timings and, on a bad day, the error. Here the last line is shown in place, throttled, and the tail
// is kept back for the failure report. Nothing is hidden that a failure would want; nothing is shown
// that a success does not need.
internal static class LocalRun
{
    // Ten frames a second. `dotnet publish` can emit output faster than a terminal can draw it, and
    // redrawing per line is how a build ends up slower for having been watched.
    private static readonly TimeSpan ShowEvery = TimeSpan.FromMilliseconds(100);

    public static async Task<CommandResult> StreamAsync(
        ICommandRunner runner, StageStep step, string exe, string args, string? workingDirectory, CancellationToken ct)
    {
        var since = Stopwatch.StartNew();

        var result = await runner.RunLocalStreamingAsync(exe, args, workingDirectory, line =>
        {
            var text = line.Trim();
            if (text.Length == 0 || since.Elapsed < ShowEvery) return;
            since.Restart();
            step.Show(new Markup($"[grey35]{Markup.Escape(Truncate(text, 100))}[/]"));
        }, ct);

        step.Show(null);
        return result;
    }

    // A failed build stops the pipeline with its own last words attached: the exit code alone sends
    // you back to a terminal that has already scrolled.
    public static async Task RunOrStopAsync(
        ICommandRunner runner, StageStep step, string exe, string args, string? workingDirectory,
        string failure, IEnumerable<string>? hints, CancellationToken ct)
    {
        var result = await StreamAsync(runner, step, exe, args, workingDirectory, ct);
        if (result.Ok) return;

        var tail = result.Lines
            .Concat(result.StdErr.Split('\n'))
            .Select(l => l.TrimEnd())
            .Where(l => l.Trim().Length > 0)
            .TakeLast(10)
            .ToArray();

        throw new PipelineStop($"{failure} (exit code {result.ExitCode}).", tail, hints);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..Math.Max(1, max - 1)] + "…";
}
