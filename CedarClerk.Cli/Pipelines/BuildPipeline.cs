using System.Text.Json;
using System.Text.RegularExpressions;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;

namespace CedarClerk.Cli.Pipelines;

public sealed record BuildOptions(
    bool NoDesktop = false,
    bool DesktopOnly = false,
    bool Installer = false,
    bool RunDesktop = false);

// Scripts/build.ps1, moved into C# (ADR-119). The two publishes differ for the reasons they always
// did: the shipped server is framework-dependent with NO runtime identifier, portable IL, which is
// what made the armhf Pi → x86_64 droplet move a copy rather than a port; the server inside the
// desktop shell is self-contained win-x64, because that machine may have no .NET at all.
//
// No branch check on purpose — building a feature branch is the normal case, and the branch rule
// belongs to the deploy, where shipping happens.
public sealed class BuildPipeline
{
    private readonly ICommandRunner _runner;
    private readonly CliConfig _config;
    private readonly IFileWriter _files;

    public BuildPipeline(ICommandRunner runner, CliConfig config, IFileWriter files)
    {
        _runner = runner;
        _config = config;
        _files = files;
    }

    public const string StageAngular = "Angular";
    public const string StageServer = "Server";
    public const string StageDesktopServer = "Desktop server";
    public const string StageElectron = "Electron";
    public const string StageInstaller = "Installer";

    // The one production Angular build. `cedar test` runs the same invocation as its build phase, so
    // what the test run proves is what the deploy ships, not a second command that could drift.
    public static (string Exe, string Args, string Cwd) AngularBuild(CliConfig config) =>
        (Shell.Npm(), "run build", config.WebDir);

    public static IEnumerable<(string Name, string Detail)> Plan(BuildOptions options)
    {
        if (!options.DesktopOnly)
        {
            yield return (StageAngular, "npm run build");
            yield return (StageServer, "dotnet publish -c Release, portable");
        }
        if (options.NoDesktop) yield break;

        yield return (StageDesktopServer, "self-contained win-x64");
        yield return (StageElectron, "node_modules, shell");
        if (options.Installer) yield return (StageInstaller, "electron-builder");
    }

    public async Task RunAsync(StageBoard board, BuildOptions options, string version, CancellationToken ct)
    {
        if (!options.DesktopOnly)
        {
            await board.StepAsync(StageAngular, async step =>
            {
                var (exe, args, cwd) = AngularBuild(_config);
                await LocalRun.RunOrStopAsync(_runner, step, exe, args, cwd,
                    "The Angular build failed", null, ct);
                step.Done("dist/cedarclerk-web/browser");
            });

            await board.StepAsync(StageServer, async step =>
            {
                ClearOrExplain(_files, _config.PublishDir,
                    $"a server is usually still running from publish/ - stop `{CliConsts.BinaryName} run` (Ctrl+C) and retry");

                await LocalRun.RunOrStopAsync(_runner, step, "dotnet",
                    $"publish \"{_config.ServerProject}\" -c Release -o \"{_config.PublishDir}\"",
                    _config.RepoRoot, "The .NET publish failed", null, ct);

                _files.CopyTree(_config.BrowserDist, Path.Combine(_config.PublishDir, "wwwroot"));
                step.Done(Path.GetFileName(_config.PublishDir) + "/ with wwwroot");
            });
        }

        if (options.NoDesktop) return;

        await board.StepAsync(StageDesktopServer, async step =>
        {
            SyncShellVersion(version, step);
            ClearOrExplain(_files, _config.DesktopServerDir,
                "the desktop shell may still be running against this tree - close it and retry");

            await LocalRun.RunOrStopAsync(_runner, step, "dotnet",
                $"publish \"{_config.ServerProject}\" -c Release -r win-x64 --self-contained true " +
                $"-p:PublishSingleFile=false -o \"{_config.DesktopServerDir}\"",
                _config.RepoRoot, "The desktop server publish failed", null, ct);

            if (!Directory.Exists(_config.BrowserDist))
                throw new PipelineStop("The Angular output is missing, and the shell bundles it.",
                    hints: new[] { "Run without --desktop-only first." });

            _files.CopyTree(_config.BrowserDist, Path.Combine(_config.DesktopServerDir, "wwwroot"));
            step.Done("server/ with wwwroot");
        });

        await board.StepAsync(StageElectron, async step =>
        {
            if (!Directory.Exists(Path.Combine(_config.DesktopDir, "node_modules")))
            {
                step.Note("installing Electron - first run only, about 200 MB");
                await LocalRun.RunOrStopAsync(_runner, step, Shell.Npm(), "install", _config.DesktopDir,
                    "npm install failed in CedarClerk.Desktop", null, ct);
            }
            step.Done("node_modules present");
        });

        if (!options.Installer) return;

        await board.StepAsync(StageInstaller, async step =>
        {
            await LocalRun.RunOrStopAsync(_runner, step, Shell.Npm(), "run dist", _config.DesktopDir,
                "electron-builder failed", null, ct);

            var installer = Path.Combine(_config.DesktopDistDir, $"CedarClerk-Setup-{version}.exe");
            step.Done(File.Exists(installer)
                ? $"{Rendering.Format.Size(new FileInfo(installer).Length)}  dist/"
                : "dist/ - the named installer was not found");
        });
    }

    // A locked publish/ has one usual owner: a server still running from it. Directory.Delete's raw
    // "Access to the path 'Anthropic.dll' is denied" names the dll and not the cause, which turned
    // a missing Ctrl+C into a support question (18.08.2026).
    internal static void ClearOrExplain(Execution.IFileWriter files, string path, string probableCause)
    {
        try
        {
            files.DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PipelineStop(
                $"{Path.GetFileName(path)}/ could not be cleared - a file in it is locked.",
                new[] { ex.Message },
                new[] { probableCause });
        }
    }

    // The shell's version is checked against /api/health at startup, so a stale one surfaces as a
    // dialog rather than as confusing behaviour. Kept in step with Consts.cs automatically here —
    // the deploy is stricter about it, and for a different reason (see DeployPipeline).
    private void SyncShellVersion(string version, StageStep step)
    {
        var path = Path.Combine(_config.DesktopDir, "package.json");
        if (!File.Exists(path)) return;

        var text = File.ReadAllText(path);
        var current = ReadVersion(text);
        if (current == version) return;

        _files.WriteText(path, Regex.Replace(text, "\"version\":\\s*\"[^\"]*\"", $"\"version\": \"{version}\""));
        step.Note($"shell version {current} -> {version}");
    }

    public static string ReadVersion(string packageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(packageJson);
            return document.RootElement.TryGetProperty("version", out var value) ? value.GetString() ?? "?" : "?";
        }
        catch (JsonException)
        {
            return "unreadable";
        }
    }

}
