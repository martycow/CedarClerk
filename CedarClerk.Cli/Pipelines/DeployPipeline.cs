using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using CedarClerk.Cli.Server;

namespace CedarClerk.Cli.Pipelines;

public sealed record DeployOptions(
    bool SkipBuild = false,
    bool Desktop = false,
    bool Rollback = false,
    bool Force = false,
    int Retries = 5);

public sealed record DeployOutcome(
    string Version,
    string LiveBefore,
    string Running,
    int DownMs,
    GitGuard.LiveMove? Live,
    int Files,
    long PublishBytes,
    long TarBytes,
    string? DesktopNote,
    bool DesktopOk);

public sealed record RollbackOutcome(string Running, string? LiveTag);

// Scripts/deploy.ps1, moved into C# (ADR-119). Every branch in here is scar tissue and the comments
// naming the incident are the point of it, so they moved with the code rather than being summarised.
//
// The shape, which is the safety argument: everything slow happens while the old version is still
// serving. One tarball, uploaded as a resumable stream, checksummed on the far side, unpacked into
// app.new — and only then is the service stopped, for two renames and about a second. The previous
// release stays as app.prev, so a rollback is instant (ADR-113).
//
// The order is not a style choice. The old pipeline stopped the service first and copied 174 loose
// files over scp, so every dropped connection left production down with a half-copied app directory.
// That happened twice.
public sealed class DeployPipeline
{
    public const string StagePreflight = "Preflight";
    public const string StageBundle = "Bundle";
    public const string StagePack = "Pack";
    public const string StageUpload = "Upload";
    public const string StageVerify = "Verify";
    public const string StageSwap = "Swap";
    public const string StageHealth = "Health";
    public const string StagePublishInstaller = "Publish installer";
    public const string StageRollback = "Rollback";

    private readonly ICommandRunner _runner;
    private readonly IHealthProbe _health;
    private readonly CliConfig _config;
    private readonly IFileWriter _files;
    private readonly GitGuard _git;
    private readonly RemoteFiles _remote;

    public DeployPipeline(ICommandRunner runner, IHealthProbe health, CliConfig config, IFileWriter files)
    {
        _runner = runner;
        _health = health;
        _config = config;
        _files = files;
        _git = new GitGuard(runner, config.RepoRoot);
        _remote = new RemoteFiles(runner);
    }

    public static IEnumerable<(string Name, string Detail)> Plan(DeployOptions options)
    {
        if (options.Rollback)
        {
            yield return (StageRollback, "put app.prev back");
            yield return (StageHealth, "wait for the old version");
            yield break;
        }

        yield return (StagePreflight, "git, local tools, server state");

        foreach (var stage in BuildPipeline.Plan(new BuildOptions(NoDesktop: true)))
            yield return stage;

        yield return (StageBundle, "check what is about to ship");
        yield return (StagePack, "one resumable tarball");
        yield return (StageUpload, "the old version keeps serving");
        yield return (StageVerify, "checksum, unpack into app.new");
        yield return (StageSwap, "the only moment the service is down");
        yield return (StageHealth, "wait for the new version to answer");

        if (!options.Desktop) yield break;

        foreach (var stage in BuildPipeline.Plan(new BuildOptions(DesktopOnly: true, Installer: true)))
            yield return stage;
        yield return (StagePublishInstaller, "into data/downloads, manifest last");
    }

    // ------------------------------------------------------------------ the deploy

    public async Task<DeployOutcome> RunAsync(
        StageBoard board, DeployOptions options, string version, string liveBefore, CancellationToken ct)
    {
        await PreflightAsync(board, options, version, liveBefore, ct);

        if (options.SkipBuild)
        {
            foreach (var stage in BuildPipeline.Plan(new BuildOptions(NoDesktop: true)))
                board.Skip(stage.Name, "--skip-build");
        }
        else
        {
            await new BuildPipeline(_runner, _config, _files).RunAsync(board, new BuildOptions(NoDesktop: true), version, ct);
        }

        var (files, publishBytes) = await board.StepAsync(StageBundle, step =>
        {
            if (!File.Exists(Path.Combine(_config.PublishDir, "CedarClerk.Server.dll")))
                throw new PipelineStop("publish/ holds no build.",
                    hints: new[] { "Run without --skip-build." });

            // A build with no wwwroot serves an API and no application, and it would pass every
            // other check on the way there.
            if (!File.Exists(Path.Combine(_config.PublishDir, "wwwroot", "index.html")))
                throw new PipelineStop("publish/wwwroot/index.html is missing - that build would serve an API with no app.",
                    hints: new[] { "Run without --skip-build so the Angular output is bundled in." });

            var contents = Tarball.Contents(_config.PublishDir);
            var bytes = contents.Sum(f => f.Length);
            step.Done($"{contents.Count} files, {Format.Size(bytes)}");
            return Task.FromResult((contents.Count, bytes));
        });

        var packed = await board.StepAsync(StagePack, async step =>
        {
            var result = await Tarball.PackAsync(_runner, _config.PublishDir, _config.PackCacheDir, version, step.Note, ct);
            var saved = 100 - (int)(100.0 * result.Bytes / Math.Max(1, publishBytes));
            step.Done($"{Format.Size(result.Bytes)} on the wire ({saved}% smaller){(result.Reused ? ", reused" : "")}");
            return result;
        });

        var remoteTar = $"{_config.RemoteStagingDir}/cedar-{version}.tar.gz";

        await board.StepAsync(StageUpload, async step =>
        {
            // Older tarballs in staging/ are dead weight on a 48 GB disk that has to hold two copies
            // of the app during the swap.
            await _runner.RunRemoteAsync(
                $"find '{_config.RemoteStagingDir}' -maxdepth 1 -name 'cedar-*.tar.gz' ! -name 'cedar-{version}.tar.gz' -delete 2>/dev/null; echo done", ct);

            var transfer = new Transfer();
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var report = await _remote.SendAsync(packed.Path, remoteTar, options.Retries, fresh: false,
                onProgress: (sent, total) =>
                {
                    transfer.Observe(sent, clock.Elapsed);
                    step.Show(transfer.Render(board.Glyphs, sent, total, clock.Elapsed, 60));
                },
                onNote: step.Note,
                onWarn: step.Warn,
                ct);

            if (!report.Ok)
                throw new PipelineStop(
                    $"The upload could not finish after {options.Retries} attempts - production is untouched and still running.",
                    state: new[] { $"{Format.Size(report.Sent)} of {Format.Size(report.Total)} made it across." },
                    hints: new[] { $"{CliConsts.BinaryName} deploy --skip-build    # continues from that byte, does not rebuild" });

            var average = report.Total / Math.Max(0.001, clock.Elapsed.TotalSeconds);
            step.Done($"{Format.Size(average)}/s average" + (report.Attempts > 1 ? $", {report.Attempts} attempts" : ""));
        });

        await board.StepAsync(StageVerify, async step =>
        {
            var remoteHash = await _remote.Sha256Async(remoteTar, ct);
            if (remoteHash != packed.Sha256)
            {
                // Deleted rather than kept: a corrupt prefix would be resumed onto next time, and the
                // result would be a tarball that checksums differently every attempt.
                await _remote.DeleteAsync(remoteTar, ct);
                throw new PipelineStop("The uploaded file does not match the local one - it was deleted, production is untouched.",
                    state: new[] { $"local  {packed.Sha256}", $"server {remoteHash}" },
                    hints: new[] { $"{CliConsts.BinaryName} deploy --skip-build    # sends it again from scratch" });
            }
            step.Note($"checksum matches ({packed.Sha256[..16]}...)");

            var unpack = await _runner.RunRemoteAsync($@"set -e
rm -rf '{_config.RemoteNewDir}'
mkdir -p '{_config.RemoteNewDir}'
tar -xzf '{remoteTar}' -C '{_config.RemoteNewDir}'
echo ""FILES=$(find '{_config.RemoteNewDir}' -type f | wc -l)""
test -f '{_config.RemoteNewDir}/CedarClerk.Server.dll' || {{ echo MISSING_DLL; exit 3; }}
test -f '{_config.RemoteNewDir}/wwwroot/index.html' || {{ echo MISSING_WWWROOT; exit 4; }}
echo UNPACKED", ct);

            if (!unpack.Ok || !unpack.StdOut.Contains("UNPACKED"))
                throw new PipelineStop("Unpacking on the server failed - production is untouched and still running.",
                    state: unpack.Lines.Where(l => l.Trim().Length > 0),
                    hints: new[] { $"ssh {_config.Host} 'ls -la {_config.RemoteNewDir}'" });

            var unpacked = (int)RemoteFiles.Number(unpack.StdOut, "FILES");
            if (unpacked != files)
                throw new PipelineStop(
                    $"The server unpacked {unpacked} files, but the build has {files} - production is untouched.",
                    hints: new[] { $"{CliConsts.BinaryName} deploy --skip-build" });

            step.Done($"{unpacked} files staged in app.new, both sides agree");
        });

        var downMs = await board.StepAsync(StageSwap, async step =>
        {
            // Everything here is renames, so the window is about a second. It is one ssh call on
            // purpose: a connection lost between two calls is what left production stopped before.
            var swap = await _runner.RunRemoteAsync($@"set -e
T0=$(date +%s%3N)
sudo systemctl stop {CliConsts.ServiceName}
rm -rf '{_config.RemotePrevDir}'
mv '{_config.RemoteAppDir}' '{_config.RemotePrevDir}'
mv '{_config.RemoteNewDir}' '{_config.RemoteAppDir}'
sudo systemctl start {CliConsts.ServiceName}
echo ""DOWNMS=$(($(date +%s%3N) - $T0))""
echo SWAPPED", ct);

            if (!swap.Ok || !swap.StdOut.Contains("SWAPPED"))
            {
                var state = await _runner.RunRemoteAsync(
                    $"systemctl is-active {CliConsts.ServiceName}; ls -d {_config.RemoteRoot}/app* 2>/dev/null", ct);

                if (state.StdOut.TrimStart().StartsWith("inactive", StringComparison.Ordinal))
                {
                    step.Warn("the swap broke halfway and the service is down - starting it back up");
                    await _runner.RunRemoteAsync($"sudo systemctl start {CliConsts.ServiceName}", ct);
                }

                throw new PipelineStop("The swap failed.",
                    state: (swap.StdOut + "\n" + state.StdOut).Split('\n').Where(l => l.Trim().Length > 0),
                    hints: new[]
                    {
                        $"ssh {_config.Host} 'ls -la {_config.RemoteRoot}'",
                        $"{CliConsts.BinaryName} deploy --rollback    # if app.prev is the good one"
                    });
            }

            var ms = (int)RemoteFiles.Number(swap.StdOut, "DOWNMS");
            step.Done($"down for {ms}ms; the old release is kept as app.prev");
            return ms;
        });

        var running = await board.StepAsync(StageHealth, async step =>
        {
            var answer = await WaitForHealthAsync(step, ct);

            if (answer is null)
                throw new PipelineStop("The new version never answered the health check.",
                    state: new[] { $"Tried {_config.HealthUrl}." },
                    hints: new[]
                    {
                        $"ssh {_config.Host} 'systemctl status {CliConsts.ServiceName}'",
                        $"{CliConsts.BinaryName} deploy --rollback    # puts the previous release back in seconds"
                    });

            if (answer != version)
                throw new PipelineStop($"The server answers v{answer}, but v{version} was shipped.",
                    state: new[] { "Something other than this build is serving that URL." },
                    hints: new[] { $"ssh {_config.Host} 'ls -la {_config.RemoteAppDir} | head'" });

            step.Done($"v{answer} is answering");
            return answer;
        });

        // Only now: production has answered with the version just shipped, which is the first moment
        // "this commit is live" is a fact rather than an intention (ADR-118 decision 12).
        var live = await _git.SetLiveAsync(ct);

        var (desktopNote, desktopOk) = options.Desktop
            ? await DesktopAsync(board, version, ct)
            : (null, true);

        return new DeployOutcome(version, liveBefore, running, downMs, live, files, publishBytes,
            packed.Bytes, desktopNote, desktopOk);
    }

    // ------------------------------------------------------------------ preflight

    public async Task<string> PreflightAsync(
        StageBoard board, DeployOptions options, string version, string liveBefore, CancellationToken ct) =>
        await board.StepAsync(StagePreflight, async step =>
        {
            var branch = await _git.BranchAsync(ct);

            // Under --dry-run git is not run either, so the answer is empty. Saying that beats
            // printing "you are on ''", which reads like a repository in a strange state.
            if (branch.Length == 0)
                throw new PipelineStop("The branch could not be read, so nothing can be checked against it.",
                    state: new[] { "Under --dry-run nothing is executed, git included - which is why this stops here." },
                    hints: new[] { $"{CliConsts.BinaryName} deploy --preflight    # the same checks, for real" });

            if (branch == "HEAD")
            {
                // A detached HEAD is never right for a deploy: there is no branch to say what shipped.
                if (!options.Force)
                    throw new PipelineStop("Deploy refused: HEAD is detached, so there is no branch to attribute this to.",
                        hints: new[] { "git switch master", "--force overrides, and says what it is overriding" });
                step.Warn("--force: deploying from a detached HEAD");
            }
            else if (branch != "master")
            {
                if (!options.Force)
                    throw new PipelineStop($"Deploy refused: you are on '{branch}'.",
                        state: new[] { "master holds the latest stable version and is the only branch that ships (CLAUDE.md)." },
                        hints: new[] { "git switch master", "--force overrides if you genuinely mean to" });
                step.Warn($"--force: deploying from '{branch}', which is not master");
            }

            var dirty = await _git.DirtyAsync(ct);
            if (dirty.Count > 0)
            {
                if (!options.Force)
                    throw new PipelineStop("Deploy refused: the working tree has uncommitted changes.",
                        state: dirty.Take(10).Append(dirty.Count > 10 ? "..." : "")
                            .Where(l => l.Length > 0)
                            .Append("What ships would match no commit, so nothing could say what is running."),
                        hints: new[] { "git commit them, or pass --force" });
                step.Warn($"--force: continuing with {dirty.Count} uncommitted change(s)");
            }

            // Forgetting the tag is bookkeeping, not a broken release, and a hard stop here would
            // refuse a perfectly good hotfix at the worst possible moment.
            var tags = await _git.TagsAtHeadAsync(ct);
            if (!tags.Contains(version))
                step.Note($"HEAD carries no '{version}' tag - every commit on master is meant to: git tag {version}");

            await NoteLiveTagAsync(step, liveBefore, ct);

            if (!Shell.OnPath("tar"))
                throw new PipelineStop("tar is not on PATH - the whole transfer is one tarball, so it is required.",
                    hints: new[] { "Windows 10/11 ships it at C:\\Windows\\System32\\tar.exe; Git for Windows also has one." });

            if (options.SkipBuild && !File.Exists(Path.Combine(_config.PublishDir, "CedarClerk.Server.dll")))
                throw new PipelineStop("--skip-build was given, but publish/ holds no build.",
                    hints: new[] { "Run without --skip-build." });

            if (options.Desktop) DesktopPreflight(version);

            // One round trip for everything that decides whether it is worth starting.
            var probe = await _runner.RunRemoteAsync($@"mkdir -p '{_config.RemoteStagingDir}'
echo ""ACTIVE=$(systemctl is-active {CliConsts.ServiceName} 2>/dev/null)""
echo ""FREEKB=$(df -Pk '{_config.RemoteRoot}' | awk 'NR==2{{print $4}}')""
echo ""APPFILES=$(find '{_config.RemoteAppDir}' -type f 2>/dev/null | wc -l)""
echo ""PREV=$(test -d '{_config.RemotePrevDir}' && echo yes || echo no)""", ct);

            if (!probe.Ok)
                throw new PipelineStop($"Cannot reach {_config.Host} - nothing has been touched.",
                    state: probe.Lines.Where(l => l.Trim().Length > 0),
                    hints: new[] { $"ssh {_config.Host} 'echo ok'   # check the connection first" });

            var active = RemoteFiles.Field(probe.StdOut, "ACTIVE");
            var freeKb = RemoteFiles.Number(probe.StdOut, "FREEKB");
            var appFiles = RemoteFiles.Number(probe.StdOut, "APPFILES");
            var hasPrev = RemoteFiles.Field(probe.StdOut, "PREV") == "yes";

            step.Note($"service {active}, {appFiles} files in app/, {Format.Size(freeKb * 1024)} free" +
                      (hasPrev ? ", app.prev present" : ""));

            if (freeKb < 400_000) step.Warn("less than 400 MB free on the server - the swap keeps two copies of the app");
            if (active != "active") step.Warn($"the service is {active} right now, so production is already down - this deploy brings it back");

            step.Done($"{branch}, {(dirty.Count == 0 ? "clean" : dirty.Count + " dirty")}, server {active}");
            return probe.StdOut;
        });

    private async Task NoteLiveTagAsync(StageStep step, string liveVersion, CancellationToken ct)
    {
        var live = await _git.TagCommitAsync("LIVE", ct);
        if (live.Length == 0)
        {
            step.Note("no LIVE tag yet - a successful deploy will create it");
            return;
        }

        var head = await _git.HeadAsync(ct);
        var tagged = await _git.VersionAtAsync(live, ct);
        var shortSha = live[..Math.Min(7, live.Length)];

        if (live == head)
        {
            step.Note($"LIVE is already on HEAD ({shortSha}) - this deploy leaves it there");
            return;
        }

        // A stale tag is bookkeeping, and refusing to deploy over one would leave production on the
        // older code AND the tag still wrong. The deploy such a stop would block is the fix.
        if (liveVersion.Length > 0 && tagged.Length > 0 && tagged != liveVersion)
            step.Warn($"LIVE points at {shortSha} (v{tagged}), production answers v{liveVersion} - stale, this deploy corrects it");
        else
            step.Note($"LIVE: {shortSha}" + (tagged.Length > 0 ? $" (v{tagged})" : ""));
    }

    // The version an installed copy reports comes from package.json, and the tag on this commit says
    // which source it was built from. Letting the build sync them mid-deploy would ship an installer
    // whose version exists in no commit — so the mismatch is corrected here and the run stops, rather
    // than leaving a modified file behind after production has already moved.
    private void DesktopPreflight(string version)
    {
        if (!Shell.OnPath("npm"))
            throw new PipelineStop("--desktop needs npm on PATH - electron-builder is what makes the installer.");

        if (!Directory.Exists(_config.BrowserDist))
            throw new PipelineStop("The installer bundles the Angular output, and cedarclerk-web/dist is empty.",
                hints: new[] { "Run without --skip-build, or build the frontend first." });

        var path = Path.Combine(_config.DesktopDir, "package.json");
        var text = File.ReadAllText(path);
        var current = BuildPipeline.ReadVersion(text);
        if (current == version) return;

        _files.WriteText(path,
            System.Text.RegularExpressions.Regex.Replace(text, "\"version\":\\s*\"[^\"]*\"", $"\"version\": \"{version}\""));

        throw new PipelineStop($"The desktop shell says {current}, this build is {version} - nothing has been shipped.",
            state: new[] { "package.json has just been corrected; it needs to be part of the tagged commit." },
            hints: new[]
            {
                "git add CedarClerk.Desktop/package.json; git commit -m \"Sync shell version\"",
                "then re-run the same deploy"
            });
    }

    // ------------------------------------------------------------------ health

    // 120 seconds, because a start that applies an EF migration has taken ~40s before now.
    private async Task<string?> WaitForHealthAsync(StageStep step, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 1; attempt <= 40; attempt++)
        {
            step.Show(new Spectre.Console.Markup(
                $"[grey35]waiting {Format.Duration(clock.Elapsed)} for {Spectre.Console.Markup.Escape(_config.HealthUrl)}[/]"));

            await Task.Delay(TimeSpan.FromSeconds(3), ct);

            var report = await _health.GetAsync(_config.HealthUrl, ct);
            if (report.Answered)
            {
                step.Show(null);
                return report.Version;
            }
        }

        step.Show(null);
        return null;
    }

    // ------------------------------------------------------------------ rollback

    public async Task<RollbackOutcome> RollbackAsync(StageBoard board, DeployOptions options, CancellationToken ct)
    {
        await board.StepAsync(StageRollback, async step =>
        {
            // app.prev is whatever was in app/ at the last swap, and that is not automatically a good
            // release: the first run after a failed deploy files a half-copied one away. Rolling back
            // onto that would be a step backwards dressed as a recovery.
            var probe = await _runner.RunRemoteAsync($@"test -d '{_config.RemotePrevDir}' || {{ echo NOPREV; exit 0; }}
test -f '{_config.RemotePrevDir}/CedarClerk.Server.dll' || echo NODLL
test -f '{_config.RemotePrevDir}/wwwroot/index.html' || echo NOWWWROOT
echo ""FILES=$(find '{_config.RemotePrevDir}' -type f | wc -l)""", ct);

            if (probe.StdOut.Contains("NOPREV"))
                throw new PipelineStop("There is no previous release to roll back to.",
                    state: new[] { $"{_config.RemotePrevDir} does not exist on the server." },
                    hints: new[] { $"git checkout <tag>; {CliConsts.BinaryName} deploy" });

            var missing = new List<string>();
            if (probe.StdOut.Contains("NODLL")) missing.Add("CedarClerk.Server.dll");
            if (probe.StdOut.Contains("NOWWWROOT")) missing.Add("wwwroot/index.html");

            if (missing.Count > 0)
            {
                if (!options.Force)
                    throw new PipelineStop("The previous release is incomplete - rolling back to it would break the site.",
                        state: new[]
                        {
                            $"{_config.RemotePrevDir} is missing: {string.Join(", ", missing)}",
                            "It is most likely the half-copied directory a failed deploy left behind."
                        },
                        hints: new[]
                        {
                            $"git checkout <a tag that worked>; {CliConsts.BinaryName} deploy",
                            $"{CliConsts.BinaryName} deploy --rollback --force    # if you know better"
                        });
                step.Warn($"--force: rolling back onto app.prev even though it is missing {string.Join(", ", missing)}");
            }

            step.Note($"app.prev holds {RemoteFiles.Number(probe.StdOut, "FILES")} files and looks complete");

            var swap = await _runner.RunRemoteAsync($@"set -e
sudo systemctl stop {CliConsts.ServiceName}
rm -rf '{_config.RemoteRoot}/app.broken'
mv '{_config.RemoteAppDir}' '{_config.RemoteRoot}/app.broken'
mv '{_config.RemotePrevDir}' '{_config.RemoteAppDir}'
sudo systemctl start {CliConsts.ServiceName}
echo SWAPPED", ct);

            if (!swap.Ok || !swap.StdOut.Contains("SWAPPED"))
                throw new PipelineStop("Rollback failed halfway.",
                    state: swap.Lines.Where(l => l.Trim().Length > 0),
                    hints: new[]
                    {
                        $"ssh {_config.Host} 'ls -la {_config.RemoteRoot}'",
                        $"ssh {_config.Host} 'sudo systemctl start {CliConsts.ServiceName}'"
                    });

            step.Done("previous release is back; the bad one is kept as app.broken");
        });

        return await board.StepAsync(StageHealth, async step =>
        {
            var answer = await WaitForHealthAsync(step, ct);
            if (answer is null)
                throw new PipelineStop("Rolled back, but the server still does not answer.",
                    hints: new[] { $"ssh {_config.Host} 'systemctl status {CliConsts.ServiceName}'" });

            // The tags follow the same swap the server just performed: LIVE-PREV becomes LIVE and
            // nothing is kept behind it, because the server kept nothing behind app.prev either.
            var restored = await _git.RestoreLiveAsync(ct);
            step.Done($"server answers v{answer}");
            return new RollbackOutcome(answer, restored);
        });
    }

    // ------------------------------------------------------------------ desktop (ADR-116)

    // Everything here runs with the site already live, so nothing can take production down. A failure
    // costs the desktop update, not the deploy — which is why it reports instead of aborting.
    private async Task<(string? Note, bool Ok)> DesktopAsync(StageBoard board, string version, CancellationToken ct)
    {
        try
        {
            await new BuildPipeline(_runner, _config, _files)
                .RunAsync(board, new BuildOptions(DesktopOnly: true, Installer: true), version, ct);
        }
        catch (PipelineStop stop)
        {
            return ($"not published - {stop.Message}", false);
        }

        var installer = Path.Combine(_config.DesktopDistDir, $"CedarClerk-Setup-{version}.exe");
        var manifest = Path.Combine(_config.DesktopDistDir, "latest.yml");
        var blockmap = installer + ".blockmap";

        return await board.StepAsync(StagePublishInstaller, async step =>
        {
            var missing = new[] { installer, manifest }.Where(p => !File.Exists(p)).ToArray();
            if (missing.Length > 0)
            {
                step.Warn("the site is deployed and running; only the desktop update did not happen");
                foreach (var path in missing) step.Note($"missing: {Path.GetFileName(path)}");
                return ("not published - the installer was not built", false);
            }

            // Staged first, moved second, manifest third. latest.yml is the only file an installed
            // copy reads, so as long as it appears after the file it names, a half-finished publish is
            // invisible rather than broken: clients keep seeing the previous version.
            var staging = $"{_config.RemoteDownloadsDir}/.staging";
            await _runner.RunRemoteAsync($"mkdir -p '{staging}'", ct);

            var name = Path.GetFileName(installer);
            var localHash = await Tarball.Sha256Async(installer, ct);
            var published = false;

            for (var pass = 1; pass <= 2 && !published; pass++)
            {
                var transfer = new Transfer();
                var clock = System.Diagnostics.Stopwatch.StartNew();

                var sent = await _remote.SendAsync(installer, $"{staging}/{name}", 5, fresh: false,
                    onProgress: (done, total) =>
                    {
                        transfer.Observe(done, clock.Elapsed);
                        step.Show(transfer.Render(board.Glyphs, done, total, clock.Elapsed, 60));
                    },
                    onNote: step.Note, onWarn: step.Warn, ct);
                step.Show(null);

                if (!sent.Ok) break;

                if (await _remote.Sha256Async($"{staging}/{name}", ct) == localHash) { published = true; break; }

                // Same file name, different bytes: a partial upload of an earlier build of this same
                // version. Resuming spliced two builds together, so the only cure is starting over.
                step.Warn($"checksum mismatch on pass {pass} - discarding the staged file and sending it again");
                await _remote.DeleteAsync($"{staging}/{name}", ct);
            }

            if (!published)
            {
                step.Warn("the installer did not verify on the server");
                return ("not published - the upload did not verify", false);
            }

            step.Note($"checksum matches ({localHash[..16]}...)");

            // Both are small, but a truncated one is worse than a missing one: a half-written manifest
            // is still a manifest as far as a client is concerned, and a half-written blockmap breaks
            // the differential download it exists to enable.
            var blockmapOk = File.Exists(blockmap) &&
                             (await _remote.SendAsync(blockmap, $"{staging}/{name}.blockmap", 5, fresh: true,
                                 (_, _) => { }, step.Note, step.Warn, ct)).Ok;

            if (File.Exists(blockmap) && !blockmapOk)
            {
                step.Warn("the blockmap did not upload - updates will download the whole installer");
                await _remote.DeleteAsync($"{staging}/{name}.blockmap", ct);
            }

            var manifestOk = (await _remote.SendAsync(manifest, $"{staging}/latest.yml", 5, fresh: true,
                (_, _) => { }, step.Note, step.Warn, ct)).Ok;

            if (!manifestOk)
            {
                step.Warn("latest.yml did not upload - nothing was moved into place");
                return ("uploaded, but the manifest did not", false);
            }

            // One call: move the payload in, then the manifest, then drop everything older than the
            // last two releases. Keeping one previous installer is what makes a bad build recoverable
            // by hand without a rebuild.
            var place = await _runner.RunRemoteAsync($@"set -e
mv -f '{staging}/{name}' '{_config.RemoteDownloadsDir}/{name}'
if [ -f '{staging}/{name}.blockmap' ]; then mv -f '{staging}/{name}.blockmap' '{_config.RemoteDownloadsDir}/{name}.blockmap'; fi
mv -f '{staging}/latest.yml' '{_config.RemoteDownloadsDir}/latest.yml'
ls -1t '{_config.RemoteDownloadsDir}'/CedarClerk-Setup-*.exe 2>/dev/null | tail -n +3 | xargs -r -I{{}} rm -f {{}} {{}}.blockmap
echo ""KEPT=$(ls -1 '{_config.RemoteDownloadsDir}'/CedarClerk-Setup-*.exe 2>/dev/null | wc -l)""
echo PLACED", ct);

            if (!place.StdOut.Contains("PLACED"))
            {
                step.Warn("uploaded, but moving it into place failed");
                foreach (var line in place.Lines.Where(l => l.Trim().Length > 0)) step.Note(line);
                return ("uploaded, but moving it into place failed", false);
            }

            // The one thing a file on disk cannot prove: that the route serves it. A manifest nobody
            // can read is exactly as useless as no manifest.
            var served = await _runner.RunLocalAsync("curl", $"-fsS --max-time 15 \"{_config.DownloadUrl}/latest.yml\"", ct);
            var kept = RemoteFiles.Number(place.StdOut, "KEPT");

            if (!served.Ok || !served.StdOut.Contains($"version: {version}"))
            {
                step.Warn($"{_config.DownloadUrl}/latest.yml did not confirm v{version}");
                step.Note("the files are on the server; it is the route serving them that did not answer");
                return ("published, manifest not verified", false);
            }

            step.Done($"{kept} installer(s) kept; {_config.DownloadUrl}/latest.yml answers v{version}");
            return ($"v{version}   {_config.DownloadUrl}/latest", true);
        });
    }
}
