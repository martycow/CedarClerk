using System.Diagnostics;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Pipelines;

public sealed record TestOptions(bool Smoke = false, bool Backend = false, bool Frontend = false);

public sealed record TestPhaseResult(string Name, int ExitCode, TimeSpan Elapsed)
{
    public bool Ok => ExitCode == 0;
}

// Scripts/test.ps1, moved into C# (ADR-119). The four phases, in the same order, with the same
// meaning of "green": every phase's own exit code, and nothing else, decides.
//
// One thing got better in the move rather than only moving. test.ps1 printed "=== Name ===" and the
// tracker recognised phases by parsing that back out, which meant check-contrast.mjs printing
// "=== light ===" in the same shape had to be defended against by name. The phases are now announced
// by the code that starts them, so the grid's idea of where it is cannot be desynchronised by
// something a runner prints.
//
// -Detailed is gone as a flag and became the only behaviour: the CLI is the only caller now, and the
// reason it existed was to give this grid a line per test.
public sealed class TestPipeline
{
    public const string PhaseBackend = "Backend (dotnet test)";
    public const string PhaseFrontend = "Frontend units (vitest)";
    public const string PhaseContrast = "Contrast contract";
    public const string PhaseSmoke = "Smoke (Playwright, isolated database)";

    private readonly ICommandRunner _runner;
    private readonly CliConfig _config;

    public TestPipeline(ICommandRunner runner, CliConfig config)
    {
        _runner = runner;
        _config = config;
    }

    public static IReadOnlyList<string> Phases(TestOptions options)
    {
        // No selector means everything except the slow suite — the same default test.ps1 had.
        var backend = options.Backend || !(options.Backend || options.Frontend);
        var frontend = options.Frontend || !(options.Backend || options.Frontend);

        var phases = new List<string>();
        if (backend) phases.Add(PhaseBackend);
        if (frontend) { phases.Add(PhaseFrontend); phases.Add(PhaseContrast); }
        if (options.Smoke) phases.Add(PhaseSmoke);
        return phases;
    }

    public async Task<IReadOnlyList<TestPhaseResult>> RunAsync(
        TestOptions options, Action<string> onLine, CancellationToken ct)
    {
        var results = new List<TestPhaseResult>();

        foreach (var phase in Phases(options))
        {
            // The tracker opens a phase on this line, exactly as it did when test.ps1 printed it.
            onLine($"=== {phase} ===");

            var watch = Stopwatch.StartNew();
            var (exe, args, cwd) = Command(phase);
            var result = await _runner.RunLocalStreamingAsync(exe, args, cwd, onLine, ct);
            watch.Stop();

            results.Add(new TestPhaseResult(phase, result.ExitCode, watch.Elapsed));
        }

        return results;
    }

    private (string Exe, string Args, string Cwd) Command(string phase) => phase switch
    {
        // SchemaDriftGuardTests lives in here: it fails when Entities.cs has moved without a
        // migration, which is the one mistake that breaks the app at startup rather than at build.
        PhaseBackend => ("dotnet", $"test \"{_config.RepoRoot}\" --nologo --logger \"console;verbosity=normal\"", _config.RepoRoot),

        PhaseFrontend => (Shell.Npm(), "run test", _config.WebDir),

        // Reads the tokens straight out of styles.scss and scores every pair the app renders, in both
        // themes. Cheap, and the only check that catches a colour choice going unreadable.
        PhaseContrast => (Shell.Npm(), "run check:contrast", _config.WebDir),

        // Wipes a scratch CEDAR_DATA_DIR and runs with no bot token, so it can never touch real data
        // or knock the production bot off its token (.claude/rules/telegram-bot.md). It stays a
        // script because it manages a server process and an environment, not a sequence of steps.
        PhaseSmoke => (Shell.PowerShell(), Shell.ScriptArgs(_config.E2eScript), _config.RepoRoot),

        _ => throw new InvalidOperationException($"no command for phase '{phase}'")
    };
}
