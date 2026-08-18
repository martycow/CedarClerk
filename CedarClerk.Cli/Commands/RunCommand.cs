using System.ComponentModel;
using System.Diagnostics;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Parsing;
using CedarClerk.Cli.Pipelines;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Commands;

public sealed class RunSettings : CedarSettings
{
    [CommandOption("--no-build")]
    [Description("Serve what is already in publish/ instead of building first.")]
    public bool NoBuild { get; init; }
}

// The local check loop (ADR-121): build the Angular front and the server publish, serve the
// published output on localhost:8080 — the artifact a deploy would ship, not the dev servers —
// and open the browser on it. The server runs as a foreground child; Ctrl+C stops it.
public sealed class RunCommand : AsyncCommand<RunSettings>
{
    private const string LocalUrl = "http://localhost:8080";
    private const string LocalHealthUrl = LocalUrl + "/api/health";
    private static readonly TimeSpan HealthPatience = TimeSpan.FromSeconds(90);

    protected override Task<int> ExecuteAsync(CommandContext context, RunSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(RunSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        if (!session.RequireRepo()) return 2;

        // A port that answers belongs to someone — usually `dotnet run` or another `cedar run`.
        // Refusing is the whole policy: this tool never kills a process it did not start.
        var already = await session.Health.GetAsync(LocalHealthUrl, cancellationToken);
        if (already.Answered)
        {
            session.Problem($"localhost:8080 is already serving v{already.Version} ({already.Environment}).");
            session.Note("stop that server first, or just check it in the browser as it is.");
            return 1;
        }

        var version = GitGuard.VersionOnDisk(session.Config.RepoRoot);

        if (!settings.NoBuild)
        {
            if (!session.DryRun && !session.Confirm("This deletes publish/ and rebuilds it. Continue?"))
            {
                session.Note("nothing was done.");
                return 1;
            }

            var options = new BuildOptions(NoDesktop: true);
            var board = new StageBoard(session.Console, session.Glyphs, "run")
            {
                Header = $"[grey]run[/]  [{Palette.Hex(Palette.Accent)}]v{version}[/]  " +
                         $"[grey35]{Markup.Escape(session.Config.RepoRoot)}[/]"
            };
            foreach (var (name, detail) in BuildPipeline.Plan(options)) board.Plan(name, detail);

            try
            {
                await board.RunAsync(async _ =>
                {
                    await new BuildPipeline(session.Runner, session.Config, session.Files)
                        .RunAsync(board, options, version, cancellationToken);
                    return 0;
                }, cancellationToken);
            }
            catch (PipelineStop stop)
            {
                session.Console.WriteLine();
                session.Problem(stop.Message);
                foreach (var line in stop.State) session.Console.MarkupLine($"      [grey]{Markup.Escape(line)}[/]");
                foreach (var hint in stop.Hints)
                    session.Console.MarkupLine($"        [{Palette.Hex(Palette.Warn)}]{Markup.Escape(hint)}[/]");
                return 1;
            }
        }
        else if (!File.Exists(ServerDll(session.Config)))
        {
            session.Problem("publish/ has no server build to serve.");
            session.Note($"{CliConsts.BinaryName} run    # without --no-build");
            return 1;
        }

        return await ServeAsync(session, cancellationToken);
    }

    // What the served child runs with. CEDAR_DATA_DIR points the published server at the same
    // database `dotnet run` uses, so drafts survive rebuilds — left alone it would default to
    // publish/data, which every build deletes. Cedar__BotToken is a single space, not "": blank
    // enough for TelegramBotService's IsNullOrWhiteSpace check, while an empty string DELETES the
    // variable on Windows and would let a token exported in the user's environment through — the
    // exact 409 .claude/rules/telegram-bot.md exists to prevent (the 26.07.2026 incident).
    public static IReadOnlyList<KeyValuePair<string, string>> ServeEnvironment(CliConfig config) => new[]
    {
        KeyValuePair.Create(CedarClerk.Core.Consts.DataDirectoryKey, LocalDataDir(config)),
        KeyValuePair.Create("Cedar__BotToken", " ")
    };

    public static string ServerDll(CliConfig config) => Path.Combine(config.PublishDir, "CedarClerk.Server.dll");

    public static string LocalDataDir(CliConfig config) => Path.Combine(config.ServerProject, "data");

    private static async Task<int> ServeAsync(Session session, CancellationToken cancellationToken)
    {
        var config = session.Config;
        var dll = ServerDll(config);

        string? botLine = null;
        var tail = new Queue<string>();

        // The child inherits this process's environment, and ProcessCommandRunner starts the child
        // synchronously before its first await — so setting here and restoring right after the call
        // is enough, and the menu's next command does not run inside the server's environment.
        var pairs = ServeEnvironment(config);
        var saved = pairs.ToDictionary(p => p.Key, p => Environment.GetEnvironmentVariable(p.Key));
        foreach (var (key, value) in pairs) Environment.SetEnvironmentVariable(key, value);

        Task<Execution.CommandResult> server;
        try
        {
            server = session.Runner.RunLocalStreamingAsync("dotnet", $"\"{dll}\"", config.PublishDir, line =>
            {
                if (line.Contains("bot is disabled", StringComparison.OrdinalIgnoreCase)) botLine = line.Trim();
                tail.Enqueue(line.TrimEnd());
                while (tail.Count > 12) tail.Dequeue();
            }, cancellationToken);
        }
        finally
        {
            foreach (var (key, value) in saved) Environment.SetEnvironmentVariable(key, value);
        }

        session.Console.MarkupLine(
            $"[{Palette.Hex(Palette.Accent)}]{session.Glyphs.Arrow}[/] [grey]starting the published server on[/] " +
            $"[{Palette.Hex(Palette.Text)}]{LocalUrl}[/]");

        var clock = Stopwatch.StartNew();
        var report = HealthReport.Down;
        while (!report.Answered && !server.IsCompleted && clock.Elapsed < HealthPatience)
        {
            await Task.WhenAny(server, Task.Delay(500, cancellationToken));
            if (server.IsCompleted) break;
            report = await session.Health.GetAsync(LocalHealthUrl, cancellationToken);
        }

        if (server.IsCompleted)
        {
            var early = await server;
            if (early.Ok)
            {
                // The only way a healthy start ends up here is --dry-run, where the runner returns
                // at once — the command it printed is the answer.
                session.Note("the server process ended before /api/health answered (exit 0).");
                return 0;
            }
            session.Problem($"the server crashed on startup (exit code {early.ExitCode}).");
            foreach (var line in tail.Where(l => l.Trim().Length > 0))
                session.Console.MarkupLine($"      [grey]{Markup.Escape(line)}[/]");
            return 1;
        }

        if (!report.Answered)
        {
            session.Problem($"the server did not answer {LocalHealthUrl} within {HealthPatience.TotalSeconds:0}s.");
            return 1;
        }

        session.Console.WriteLine();
        session.Console.Write(Ui.Panel(session.Glyphs, $"{session.Glyphs.Ok} serving", new Rows(new IRenderable[]
        {
            new Markup($"[grey35]url     [/] [{Palette.Hex(Palette.Text)}]{LocalUrl}[/]"),
            new Markup($"[grey35]serving [/] [{Palette.Hex(Palette.Accent)}]v{report.Version}[/] [grey]({Markup.Escape(report.Environment)})[/]"),
            new Markup($"[grey35]data    [/] [grey]{Markup.Escape(LocalDataDir(config))}[/]"),
            new Markup($"[grey35]bot     [/] [grey]{Markup.Escape(botLine ?? "no 'bot is disabled' line seen yet — check the output")}[/]"),
            new Markup("[grey35]stop    [/] [grey]Ctrl+C[/]")
        }), Palette.Ok));
        session.Console.WriteLine();

        await session.Runner.LaunchDetachedAsync(LocalUrl, "", null, cancellationToken);

        try
        {
            var result = await server;
            if (!result.Ok)
            {
                session.Problem($"the server stopped by itself (exit code {result.ExitCode}).");
                foreach (var line in tail.Where(l => l.Trim().Length > 0))
                    session.Console.MarkupLine($"      [grey]{Markup.Escape(line)}[/]");
                return 1;
            }
            session.Note("the server stopped.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C: the runner has already killed the process tree.
            session.Console.WriteLine();
            session.Note("stopped.");
            return 0;
        }
    }
}
