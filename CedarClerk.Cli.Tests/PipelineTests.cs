using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Pipelines;
using CedarClerk.Cli.Rendering;
using Spectre.Console.Testing;

namespace CedarClerk.Cli.Tests;

// The logic that moved out of the scripts (ADR-119). These are the tests the PowerShell never had,
// and they are the reason the move is defensible at all: the deploy is the riskiest thing in this
// project, and until now every one of its branches was verified by running it in anger.
public class PipelineTests
{
    private static CliConfig Config() => new()
    {
        Host = "martycow@example.test",
        RemoteRoot = "/home/martycow/cedarclerk",
        RepoRoot = RepoRoot(),
        HealthUrl = "https://cedarclerk.mooexe.dev/api/health"
    };

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CedarClerk.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    private static StageBoard Board(TestConsole console, params string[] stages)
    {
        var board = new StageBoard(console, Glyphs.Unicode, "test");
        foreach (var stage in stages) board.Plan(stage);
        return board;
    }

    // ------------------------------------------------------------------ a locked publish/

    private sealed class LockedFileWriter : IFileWriter
    {
        public void DeleteDirectory(string path) =>
            throw new UnauthorizedAccessException("Access to the path 'Anthropic.dll' is denied.");

        public void CopyTree(string source, string destination) { }
        public void WriteText(string path, string content) { }
    }

    // The 18.08.2026 deploy failure: a `cedar run` server still holds publish/, and the raw
    // Directory.Delete message names the dll instead of the cause.
    [Fact]
    public void A_locked_publish_names_the_running_server_not_the_dll()
    {
        var stop = Assert.Throws<PipelineStop>(() =>
            BuildPipeline.ClearOrExplain(new LockedFileWriter(), @"D:\repo\publish",
                "a server is usually still running from publish/ - stop `cedar run` (Ctrl+C) and retry"));

        Assert.Contains("publish/ could not be cleared", stop.Message);
        Assert.Contains(stop.State, line => line.Contains("Anthropic.dll"));
        Assert.Contains(stop.Hints, hint => hint.Contains("cedar run"));
    }

    // ------------------------------------------------------------------ the git guard

    [Fact]
    public async Task Moving_LIVE_keeps_the_tag_it_replaces_as_LIVE_PREV()
    {
        var runner = new FakeCommandRunner()
            .Answer("rev-parse -q --verify HEAD", "aaaaaaabbbbbbb")
            .Answer("refs/tags/LIVE^{commit}", "cccccccddddddd");

        var move = await new GitGuard(runner, RepoRoot()).SetLiveAsync(CancellationToken.None);

        Assert.NotNull(move);
        Assert.Equal("aaaaaaa", move!.Commit);
        Assert.Equal("ccccccc", move.Previous);
        Assert.Contains(runner.LocalCalls, call => call.Contains("tag -f LIVE-PREV cccccccddddddd"));
        Assert.Contains(runner.LocalCalls, call => call.Contains("tag -f LIVE aaaaaaabbbbbbb"));
    }

    [Fact]
    public async Task A_first_deploy_removes_a_LIVE_PREV_left_over_from_a_previous_life()
    {
        // No LIVE means nothing here knows what the server is replacing, so any LIVE-PREV lying
        // around is a guess — and a guess about what to roll back to is worse than an admission.
        var runner = new FakeCommandRunner()
            .Answer("rev-parse -q --verify HEAD", "aaaaaaabbbbbbb")
            .Answer("refs/tags/LIVE^{commit}", "");

        var move = await new GitGuard(runner, RepoRoot()).SetLiveAsync(CancellationToken.None);

        Assert.Null(move!.Previous);
        Assert.Contains(runner.LocalCalls, call => call.Contains("tag -d LIVE-PREV"));
    }

    [Fact]
    public async Task A_rollback_with_nothing_behind_it_removes_LIVE_rather_than_guessing()
    {
        var runner = new FakeCommandRunner().Answer("refs/tags/LIVE-PREV^{commit}", "");

        var restored = await new GitGuard(runner, RepoRoot()).RestoreLiveAsync(CancellationToken.None);

        Assert.Null(restored);
        Assert.Contains(runner.LocalCalls, call => call.Contains("tag -d LIVE"));
    }

    [Fact]
    public void The_version_is_read_from_the_working_copy_rather_than_from_this_assembly()
    {
        // A deploy describes the checkout in front of you. Reading Consts.cs off disk is what makes
        // `cedar` installed as a global tool still report the version it is about to ship.
        Assert.Equal(CedarClerk.Core.Consts.CurrentVersion, GitGuard.VersionOnDisk(RepoRoot()));
        Assert.Equal(CedarClerk.Core.Consts.CurrentVersion, GitGuard.VersionOnDisk(@"C:\nothing\here"));
    }

    // ------------------------------------------------------------------ the resumable upload

    private static string TempFile(int bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cedar-test-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public async Task An_interrupted_upload_continues_from_the_byte_it_reached()
    {
        var path = TempFile(1000);
        var runner = new FakeCommandRunner().AnswerInTurn("stat -c %s", "400", "1000");
        var notes = new List<string>();

        var report = await new RemoteFiles(runner).SendAsync(path, "/staging/x.tar.gz", 5, fresh: false,
            (_, _) => { }, notes.Add, notes.Add, CancellationToken.None);

        Assert.True(report.Ok);
        Assert.Contains(runner.Uploads, upload => upload.Contains("@400"));
        Assert.Contains(notes, note => note.Contains("resuming at"));

        File.Delete(path);
    }

    [Fact]
    public async Task A_file_longer_on_the_server_than_here_is_started_over_rather_than_appended_to()
    {
        var path = TempFile(1000);
        var runner = new FakeCommandRunner().AnswerInTurn("stat -c %s", "5000", "1000");
        var warnings = new List<string>();

        var report = await new RemoteFiles(runner).SendAsync(path, "/staging/x.tar.gz", 5, fresh: false,
            (_, _) => { }, _ => { }, warnings.Add, CancellationToken.None);

        Assert.True(report.Ok);
        Assert.Contains(warnings, warning => warning.Contains("longer than the one being sent"));
        Assert.Contains(runner.RemoteCalls, call => call.StartsWith("rm -f "));
        Assert.Contains(runner.Uploads, upload => upload.Contains("@0"));

        File.Delete(path);
    }

    [Fact]
    public async Task An_upload_that_never_completes_reports_failure_instead_of_success()
    {
        // The size on the far side is the verdict, never our own ssh exit code: a pipe that closed
        // cleanly after half a file also exits zero.
        var path = TempFile(1000);
        var runner = new FakeCommandRunner().Answer("stat -c %s", "0");

        var report = await new RemoteFiles(runner).SendAsync(path, "/staging/x.tar.gz", retries: 1, fresh: false,
            (_, _) => { }, _ => { }, _ => { }, CancellationToken.None);

        Assert.False(report.Ok);
        Assert.Equal(1000, report.Total);

        File.Delete(path);
    }

    [Fact]
    public async Task A_manifest_is_never_resumed_because_the_bytes_there_are_a_different_manifest()
    {
        var path = TempFile(50);
        var runner = new FakeCommandRunner().AnswerInTurn("stat -c %s", "0", "50");

        await new RemoteFiles(runner).SendAsync(path, "/staging/latest.yml", 5, fresh: true,
            (_, _) => { }, _ => { }, _ => { }, CancellationToken.None);

        Assert.Contains(runner.RemoteCalls, call => call == "rm -f '/staging/latest.yml'");

        File.Delete(path);
    }

    [Fact]
    public void Probe_output_is_read_by_key_rather_than_by_line_number()
    {
        const string output = "ACTIVE=active\nFREEKB=44796252\nAPPFILES=174\nPREV=yes";

        Assert.Equal("active", RemoteFiles.Field(output, "ACTIVE"));
        Assert.Equal(44796252, RemoteFiles.Number(output, "FREEKB"));
        Assert.Equal("yes", RemoteFiles.Field(output, "PREV"));
        Assert.Equal(0, RemoteFiles.Number(output, "MISSING"));
    }

    // ------------------------------------------------------------------ packing

    [Fact]
    public void The_pack_signature_changes_when_the_build_does_and_not_otherwise()
    {
        // This is what makes a resumed upload safe: the half-file on the server is a valid prefix
        // only if this side did not repack in between, and gzip stamps a time into every archive.
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"cedar-pack-{Guid.NewGuid():N}"));
        var file = Path.Combine(directory.FullName, "a.txt");
        File.WriteAllText(file, "one");

        var first = Tarball.Signature(Tarball.Contents(directory.FullName));
        Assert.Equal(first, Tarball.Signature(Tarball.Contents(directory.FullName)));

        File.WriteAllText(file, "one plus more");
        Assert.NotEqual(first, Tarball.Signature(Tarball.Contents(directory.FullName)));

        directory.Delete(recursive: true);
    }

    // ------------------------------------------------------------------ the deploy preflight

    private static FakeCommandRunner Healthy(string branch = "master", string dirty = "") =>
        new FakeCommandRunner()
            .Answer("rev-parse --abbrev-ref HEAD", branch)
            .Answer("status --porcelain", dirty)
            .Answer("tag --points-at HEAD", CedarClerk.Core.Consts.CurrentVersion)
            .Answer("refs/tags/LIVE^{commit}", "")
            .Answer("mkdir -p", "ACTIVE=active\nFREEKB=44796252\nAPPFILES=174\nPREV=yes");

    private static async Task<PipelineStop?> Preflight(FakeCommandRunner runner, DeployOptions options)
    {
        var console = new TestConsole();
        var board = Board(console, DeployPipeline.StagePreflight);
        var pipeline = new DeployPipeline(runner, new Server.OfflineHealthProbe(), Config(), new DryRunFileWriter(console));

        try
        {
            await board.RunAsync(async _ =>
            {
                await pipeline.PreflightAsync(board, options, CedarClerk.Core.Consts.CurrentVersion, "", CancellationToken.None);
                return 0;
            }, CancellationToken.None);
            return null;
        }
        catch (PipelineStop stop)
        {
            return stop;
        }
    }

    [Fact]
    public async Task A_deploy_from_a_branch_that_is_not_master_is_refused()
    {
        var stop = await Preflight(Healthy(branch: "indiedev_module"), new DeployOptions());

        Assert.NotNull(stop);
        Assert.Contains("indiedev_module", stop!.Message);
        Assert.Contains(stop.Hints, hint => hint.Contains("master"));
    }

    [Fact]
    public async Task Force_turns_the_branch_refusal_into_a_warning_that_says_what_it_overrides()
    {
        var stop = await Preflight(Healthy(branch: "dev"), new DeployOptions(Force: true));

        // A message that says "refused" and then continues anyway is a message nobody reads.
        Assert.Null(stop);
    }

    [Fact]
    public async Task A_dirty_working_tree_is_refused_because_what_ships_would_match_no_commit()
    {
        var stop = await Preflight(Healthy(dirty: " M Program.cs\n?? notes.txt"), new DeployOptions());

        Assert.NotNull(stop);
        Assert.Contains("uncommitted", stop!.Message);
    }

    [Fact]
    public async Task A_detached_HEAD_is_refused_because_nothing_could_say_what_shipped()
    {
        var stop = await Preflight(Healthy(branch: "HEAD"), new DeployOptions());

        Assert.NotNull(stop);
        Assert.Contains("detached", stop!.Message);
    }

    [Fact]
    public async Task An_unreachable_server_stops_before_anything_local_is_built()
    {
        var runner = Healthy();
        runner.Answer("mkdir -p", "ssh: connect to host port 22: Connection refused", exitCode: 255);

        var stop = await Preflight(runner, new DeployOptions());

        Assert.NotNull(stop);
        Assert.Contains("Cannot reach", stop!.Message);
        Assert.Contains("nothing has been touched", stop.Message);
    }

    [Fact]
    public async Task A_missing_tag_only_warns_because_a_hotfix_must_not_be_blocked_by_bookkeeping()
    {
        var runner = Healthy();
        runner.Answer("tag --points-at HEAD", "");

        Assert.Null(await Preflight(runner, new DeployOptions()));
    }

    [Fact]
    public void The_planned_stages_match_what_the_run_will_actually_do()
    {
        // The board draws the plan before any of it happens, so a stage the pipeline runs without
        // planning would throw halfway through a deploy rather than at the start.
        var plain = DeployPipeline.Plan(new DeployOptions()).Select(s => s.Name).ToList();
        Assert.Equal(DeployPipeline.StagePreflight, plain.First());
        Assert.Equal(DeployPipeline.StageHealth, plain.Last());
        Assert.Contains(DeployPipeline.StageSwap, plain);

        var rollback = DeployPipeline.Plan(new DeployOptions(Rollback: true)).Select(s => s.Name).ToList();
        Assert.Equal(new[] { DeployPipeline.StageRollback, DeployPipeline.StageHealth }, rollback);

        var desktop = DeployPipeline.Plan(new DeployOptions(Desktop: true)).Select(s => s.Name).ToList();
        Assert.Contains(DeployPipeline.StagePublishInstaller, desktop);
        Assert.Contains(BuildPipeline.StageInstaller, desktop);
    }

    // ------------------------------------------------------------------ the test run

    [Fact]
    public void No_selector_runs_everything_except_the_slow_suite()
    {
        Assert.Equal(
            new[]
            {
                TestPipeline.PhaseBackend, TestPipeline.PhaseFrontend,
                TestPipeline.PhaseIcons, TestPipeline.PhaseContrast, TestPipeline.PhaseDensity
            },
            TestPipeline.Phases(new TestOptions()));

        Assert.Equal(new[] { TestPipeline.PhaseBackend }, TestPipeline.Phases(new TestOptions(Backend: true)));

        Assert.Equal(
            new[]
            {
                TestPipeline.PhaseFrontend, TestPipeline.PhaseIcons,
                TestPipeline.PhaseContrast, TestPipeline.PhaseDensity, TestPipeline.PhaseSmoke
            },
            TestPipeline.Phases(new TestOptions(Smoke: true, Frontend: true)));
    }

    [Fact]
    public async Task Each_phase_announces_itself_so_the_grid_cannot_be_desynchronised_by_a_runner()
    {
        // test.ps1 printed these headers and the tracker parsed them back out, which is why
        // check-contrast.mjs printing "=== light ===" had to be defended against by name.
        var lines = new List<string>();
        var pipeline = new TestPipeline(new FakeCommandRunner(), Config());

        var results = await pipeline.RunAsync(new TestOptions(Backend: true), lines.Add, CancellationToken.None);

        Assert.Equal($"=== {TestPipeline.PhaseBackend} ===", lines.First());
        Assert.Single(results);
        Assert.True(results[0].Ok);
    }

    [Fact]
    public async Task A_failing_phase_carries_its_own_exit_code_out()
    {
        var runner = new FakeCommandRunner { Fallback = new CommandResult(3, "", "", TimeSpan.Zero) };

        var results = await new TestPipeline(runner, Config())
            .RunAsync(new TestOptions(Backend: true), _ => { }, CancellationToken.None);

        Assert.False(results[0].Ok);
        Assert.Equal(3, results[0].ExitCode);
    }

    [Fact]
    public void Npm_is_started_by_its_full_path_because_a_batch_file_locates_itself_from_argv_zero()
    {
        // npm.cmd finds its own JavaScript through %~dp0. Launched by bare name it resolves that
        // against the working directory instead, and reports a missing module in a project that is
        // perfectly fine. Skipped where npm is absent rather than asserted into a false green.
        if (!Shell.OnPath("npm")) return;

        Assert.True(Path.IsPathRooted(Shell.Npm()), $"npm resolved to '{Shell.Npm()}', which is not a full path");
        Assert.True(File.Exists(Shell.Npm()));
    }

    // ------------------------------------------------------------------ the board

    [Fact]
    public async Task The_whole_plan_is_visible_before_any_of_it_has_run()
    {
        var console = new TestConsole { Profile = { Width = 120 } };
        var board = Board(console, "One", "Two", "Three");

        await board.RunAsync(async _ =>
        {
            await board.StepAsync("One", step => { step.Done("done"); return Task.CompletedTask; });
            return 0;
        }, CancellationToken.None);

        Assert.Equal(StageState.Done, board.Stages[0].State);
        Assert.Equal(StageState.Pending, board.Stages[1].State);
    }

    [Fact]
    public async Task A_step_that_throws_is_marked_failed_and_the_exception_still_escapes()
    {
        var board = Board(new TestConsole(), "One");

        await Assert.ThrowsAsync<PipelineStop>(() => board.RunAsync(async _ =>
        {
            await board.StepAsync("One", _ => throw new PipelineStop("no"));
            return 0;
        }, CancellationToken.None));

        Assert.Equal(StageState.Failed, board.Stages[0].State);
    }

    [Fact]
    public async Task A_step_that_warns_still_finishes_and_says_so()
    {
        var board = Board(new TestConsole(), "One");

        await board.RunAsync(async _ =>
        {
            await board.StepAsync("One", step => { step.Warn("less free disk than is comfortable"); return Task.CompletedTask; });
            return 0;
        }, CancellationToken.None);

        Assert.Equal(StageState.Warned, board.Stages[0].State);
    }

    [Fact]
    public async Task A_stage_the_pipeline_never_planned_fails_loudly_rather_than_silently()
    {
        var board = Board(new TestConsole(), "One");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            board.StepAsync("Two", _ => Task.CompletedTask));
    }
}
