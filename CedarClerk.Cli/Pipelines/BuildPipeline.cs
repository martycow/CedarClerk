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

// Scripts/build.ps1, moved into C# (ADR-119). Same three parts in the same order, and the two
// publishes still differ from each other for the reasons they always did:
//
//   * the server that ships is framework-dependent with NO runtime identifier — portable IL, because
//     the droplet has the runtime and never builds anything. That is what made the move from an
//     armhf Pi to an x86_64 droplet a copy rather than a port
//     (.claude/rules/production-environment.md).
//   * the server inside the desktop shell is self-contained win-x64, because the machine running it
//     is not expected to have .NET at all (docs/DESKTOP.md).
//
// It does not check the branch, and that is deliberate: building a feature branch to look at it is
// the normal case. The branch rule lives in the deploy, where shipping happens.
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
                await LocalRun.RunOrStopAsync(_runner, step, Shell.Npm(), "run build", _config.WebDir,
                    "The Angular build failed", null, ct);
                step.Done("dist/cedarclerk-web/browser");
            });

            await board.StepAsync(StageServer, async step =>
            {
                _files.DeleteDirectory(_config.PublishDir);

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
            _files.DeleteDirectory(_config.DesktopServerDir);

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
