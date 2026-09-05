using System.Diagnostics;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Pipelines;

public sealed record TestOptions(bool Smoke = false, bool Backend = false, bool Frontend = false);

public sealed record TestPhaseResult(string Name, int ExitCode, TimeSpan Elapsed)
{
    public bool Ok => ExitCode == 0;
}

// Scripts/test.ps1, moved into C# (ADR-119). Green means every phase's own exit code, nothing else.
//
// The phases are announced by the code that starts them rather than parsed back out of "=== Name ==="
// as test.ps1 did, so a runner printing that shape itself (check-contrast.mjs prints "=== light ===")
// can no longer desynchronise the grid.
public sealed class TestPipeline
{
    public const string PhaseBackend = "Backend (dotnet test)";
    public const string PhaseFrontend = "Frontend units (vitest)";
    public const string PhaseIcons = "Icon inventory";
    public const string PhaseContrast = "Contrast contract";
    public const string PhaseDensity = "Density contract";
    public const string PhaseBuild = "Production build (ng build)";
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

        // Source-reading phases first, the production build after them, the browser run last: a
        // template the dev build tolerates fails in a minute here rather than after the smoke suite
        // has spent five. The smoke phase compiles its own dev build through `ng serve`, which is
        // exactly the build that tolerates it, so --smoke does not make this phase redundant (ADR-265).
        var phases = new List<string>();
        if (backend) phases.Add(PhaseBackend);
        if (frontend)
        {
            phases.Add(PhaseFrontend); phases.Add(PhaseIcons); phases.Add(PhaseContrast); phases.Add(PhaseDensity);
            phases.Add(PhaseBuild);
        }
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

        // icon-usage.generated.ts is committed and describes our own call sites, so a repaint that
        // moves them leaves it lying with nothing to notice — it shipped stale twice in one port
        // before this phase existed (ADR-173).
        PhaseIcons => (Shell.Npm(), "run check:icons", _config.WebDir),

        // Reads the tokens straight out of styles.scss and scores every pair the app renders, in both
        // themes. Cheap, and the only check that catches a colour choice going unreadable.
        PhaseContrast => (Shell.Npm(), "run check:contrast", _config.WebDir),

        // The surface split (ADR-138) fails quietly: chrome that loses its attribute inherits paper's
        // numbers and merely looks loose, so a static read of the CSS is the only thing that goes red.
        PhaseDensity => (Shell.Npm(), "run check:density", _config.WebDir),

        // The same invocation the deploy runs, so a budget breach or a template error that only the
        // production compiler rejects goes red here instead of halfway through `cedar deploy`.
        PhaseBuild => BuildPipeline.AngularBuild(_config),

        // Wipes a scratch CEDAR_DATA_DIR and runs with no bot token, so it can never touch real data
        // or knock the production bot off its token (.claude/rules/telegram-bot.md). It stays a
        // script because it manages a server process and an environment, not a sequence of steps.
        PhaseSmoke => (Shell.PowerShell(), Shell.ScriptArgs(_config.E2eScript), _config.RepoRoot),

        _ => throw new InvalidOperationException($"no command for phase '{phase}'")
    };
}
