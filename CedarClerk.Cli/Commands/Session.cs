using System.Text.Json;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Rendering;
using CedarClerk.Cli.Server;
using Spectre.Console;

namespace CedarClerk.Cli.Commands;

// What every command needs, assembled from the global flags in one place.
//
// The important line is the runner: --dry-run swaps the implementation here and nowhere else, so no
// command has to remember the flag exists (ADR-118 decision 4).
public sealed class Session
{
    public IAnsiConsole Console { get; }
    public CliConfig Config { get; }
    public ICommandRunner Runner { get; }
    public IHealthProbe Health { get; }
    public Glyphs Glyphs { get; }
    public ServerProbe Probe { get; }
    public bool Json { get; }
    public bool AssumeYes { get; }
    public bool DryRun { get; }

    private Session(IAnsiConsole console, CliConfig config, ICommandRunner runner, IHealthProbe health,
                    Glyphs glyphs, bool json, bool assumeYes, bool dryRun)
    {
        Console = console;
        Config = config;
        Runner = runner;
        Health = health;
        Glyphs = glyphs;
        Json = json;
        AssumeYes = assumeYes;
        DryRun = dryRun;
        Probe = new ServerProbe(runner, health, config);
    }

    public static Session From(CedarSettings settings, IAnsiConsole? console = null, CliConfig? config = null)
    {
        console ??= AnsiConsole.Console;
        config ??= ConfigStore.Load();

        ICommandRunner runner = settings.DryRun
            ? new DryRunCommandRunner(console)
            : new ProcessCommandRunner(config);

        // --dry-run means "touch nothing", and an HTTP GET to production is still reaching out.
        IHealthProbe health = settings.DryRun ? new OfflineHealthProbe() : new HttpHealthProbe();

        return new Session(console, config, runner, health,
            Glyphs.For(console, settings.NoUnicode), settings.Json, settings.AssumeYes, settings.DryRun);
    }

    // Destructive commands ask before acting, default no (ADR-118, and
    // .claude/rules/destructive-operations.md). --yes says out loud what it is skipping rather than
    // making the prompt vanish silently.
    public bool Confirm(string question)
    {
        if (AssumeYes)
        {
            Console.MarkupLine($"[{Palette.Hex(Palette.Warn)}]{Glyphs.Warn} --yes:[/] [grey]{Markup.Escape(question)}[/]");
            return true;
        }
        if (Json)
        {
            // A machine-readable run that stops on an unanswerable prompt would hang a script.
            Console.MarkupLine("[red]refused: --json needs --yes for a destructive command[/]");
            return false;
        }
        return Console.Prompt(new ConfirmationPrompt(Markup.Escape(question)) { DefaultValue = false });
    }

    public void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    public void Problem(string message) =>
        Console.MarkupLine($"[{Palette.Hex(Palette.Danger)}]{Glyphs.Bad}[/] {Markup.Escape(message)}");

    public void Note(string message) =>
        Console.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    // Every command that needs the repository checks the same way, so a broken config reports the
    // same fix everywhere instead of a different error per command.
    public bool RequireRepo()
    {
        if (Config.LooksComplete) return true;
        Problem($"the repository is not configured - run '{CliConsts.BinaryName} config'");
        Note($"looked for: {Config.DeployScript}");
        return false;
    }
}
