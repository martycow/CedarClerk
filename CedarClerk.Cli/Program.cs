using System.Text;
using CedarClerk.Cli;
using CedarClerk.Cli.Commands;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

// Two ways in, one implementation behind both (session brief, block 1): no arguments opens the
// menu, arguments run the command directly so the same tool is usable from a script.
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // A redirected console can refuse the encoding change; the ASCII fallback covers it.
}

// The menu is the default command, which is what makes `cedar` with no arguments open it while
// `cedar status` still parses as a command rather than as an argument to the menu.
var app = new CommandApp<MenuCommand>();

app.Configure(config =>
{
    config.SetApplicationName(CliConsts.BinaryName);
    config.UseStrictParsing();

    config.AddCommand<StatusCommand>("status")
        .WithDescription("One dashboard: version, service, disk, memory, database, graphs, backup.")
        .WithExample("status")
        .WithExample("status", "--hours", "6");

    config.AddCommand<WatchCommand>("watch")
        .WithDescription("The status screen, refreshing in place. Ctrl+C to stop.");

    config.AddCommand<LogsCommand>("logs")
        .WithDescription("journalctl for the service, level-coloured and always bounded.")
        .WithExample("logs", "--errors", "--since", "-24h");

    config.AddCommand<DbCommand>("db")
        .WithDescription("Read-only look at the live SQLite database.");

    config.AddCommand<LegendCommand>("legend")
        .WithDescription("What the abbreviations on the status screen mean.");

    config.AddBranch("backup", branch =>
    {
        branch.SetDescription("Backup state.");
        branch.AddCommand<BackupVerifyCommand>("verify")
            .WithDescription("What backup exists, and what does not.");
    });

    config.AddCommand<TestCommand>("test")
        .WithDescription("Backend, frontend and contrast, with a tick per test as results arrive.")
        .WithExample("test", "--smoke");

    config.AddCommand<BuildCommand>("build")
        .WithDescription("Angular, the server publish and the desktop shell.")
        .WithExample("build", "--no-desktop");

    config.AddCommand<DeployCommand>("deploy")
        .WithDescription("Build, ship, swap, verify. Asks before it stops production (ADR-119).")
        .WithExample("deploy", "--preflight")
        .WithExample("deploy", "--rollback");

    config.AddCommand<OpenCommand>("open")
        .WithDescription("Open Cedar Clerk itself: browser, desktop, blog or local.")
        .WithExample("open", "desktop");

    config.AddCommand<RestartCommand>("restart")
        .WithDescription("Restart the service on the droplet. Asks first.");

    config.AddCommand<ClaudeCommand>("claude")
        .WithDescription("Open a terminal in the repository running claude /remote-control.");

    config.AddCommand<ConfigCommand>("config")
        .WithDescription("Host, ssh key and paths. No secrets are stored.");

    // An unhandled exception in a tool that talks to a server is usually a network fact, not a bug
    // worth a stack trace on Marty's screen. The trace stays available behind --json/-d for a real
    // diagnosis; the default is one readable line.
    config.SetExceptionHandler((exception, _) =>
    {
        AnsiConsole.MarkupLine(
            $"[{Palette.Hex(Palette.Danger)}]{Glyphs.For(AnsiConsole.Console, false).Bad}[/] {Markup.Escape(exception.Message)}");
        if (Environment.GetEnvironmentVariable("CEDAR_CLI_TRACE") == "1")
            AnsiConsole.WriteException(exception, ExceptionFormats.ShortenEverything);
        return 1;
    });
});

return await app.RunAsync(args);
