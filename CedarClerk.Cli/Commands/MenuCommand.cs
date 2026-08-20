using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// What running the tool with no arguments does: the splash, then a menu that comes back after every
// action instead of exiting. Grouped into Monitor / Build / Server / Settings, each with a way back.
//
// The header carries the host and both version numbers at all times, so the question "which machine
// am I about to touch" is never more than a glance away — the mistake this menu could most easily
// help someone make.
public sealed class MenuCommand : AsyncCommand<CedarSettings>
{
    private sealed record Item(string Label, string Hint, Func<CedarSettings, CancellationToken, Task<int>> Run);

    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);

        // The logo says what this is now (the subtitle under the wordmark), so the footer rule is
        // left with the one thing it alone can say: which version of the working copy you are on.
        if (!settings.NoLogo)
            Logo.Show(session.Console, session.Glyphs, $"v{CedarClerk.Core.Consts.CurrentVersion}", animate: true);

        // A first run must open the wizard, not an exception. The config file is guessed from the
        // repository the executable sits in, so most of the answers are already filled in.
        if (!ConfigStore.Exists())
        {
            session.Note("no configuration yet - let us set it up.");
            await ConfigCommand.RunAsync(settings, cancellationToken);
            session = Session.From(settings);
        }

        while (true)
        {
            var choice = Choose(session, null, Root());
            if (choice is null) return 0;

            var code = await choice.Run(settings, cancellationToken);
            session.Console.WriteLine();

            if (code != 0)
                session.Note($"(exit code {code})");

            if (!session.Console.Confirm("[grey]Back to the menu?[/]", defaultValue: true)) return code;
            session.Console.Clear();
        }
    }

    // The way out is always the last row, at every level, so "one more down" is the answer to being
    // lost regardless of how deep you are.
    private static Item? Choose(Session session, string? title, IReadOnlyList<Item> items)
    {
        var entries = items
            .Select(item => new Chooser.Entry(item.Label, item.Hint))
            .Append(new Chooser.Entry(title is null ? "Exit" : "Back", ""))
            .ToList();

        var picked = Chooser.Pick(session.Console, session.Glyphs, Header(session), title, entries);
        return picked is null || picked >= items.Count ? null : items[picked.Value];
    }

    // Which machine, and which working copy — the two facts the menu exists to keep in front of you.
    private static string Header(Session session) =>
        $"[{Palette.Hex(Palette.Text)}]{Markup.Escape(session.Config.Host)}[/]  " +
        $"[grey]working copy[/] [{Palette.Hex(Palette.Accent)}]v{CedarClerk.Core.Consts.CurrentVersion}[/]";

    private static IReadOnlyList<Item> Root() => new[]
    {
        Group("Monitor", "status, graphs, logs, database", Monitor),
        Group("Build", "tests, builds, deploy", Build),
        Group("Server", "restart, backup", Server),
        Group("Open", "the product itself", Open),
        new Item("Claude", "a terminal here, running /remote-control", ClaudeCommand.RunAsync),
        new Item("Settings", "host, key, paths", ConfigCommand.RunAsync)
    };

    // Marty's ask, 12.08.2026. Everything else in this menu answers questions *about* Cedar Clerk;
    // the obvious next move — looking at it — meant leaving for a browser and typing a URL from
    // memory.
    private static IReadOnlyList<Item> Open() => new[]
    {
        new Item("Local check", "build front and back, serve, open the browser",
            (s, ct) => RunCommand.RunAsync(Copy<RunSettings>(s), ct)),
        new Item("In the browser", "production", (s, ct) => OpenCommand.RunAsync(Where(s, "browser"), ct)),
        new Item("Desktop app", "installed copy, or this working one", (s, ct) => OpenCommand.RunAsync(Where(s, "desktop"), ct)),
        new Item("The blog", "blog.mooexe.dev", (s, ct) => OpenCommand.RunAsync(Where(s, "blog"), ct)),
        new Item("Local dev server", "localhost:8080, if you have one running", (s, ct) => OpenCommand.RunAsync(Where(s, "local"), ct))
    };

    private static OpenSettings Where(CedarSettings from, string where) => new()
    {
        DryRun = from.DryRun, AssumeYes = from.AssumeYes, NoUnicode = from.NoUnicode,
        Json = from.Json, NoLogo = from.NoLogo, Where = where
    };

    private static IReadOnlyList<Item> Monitor() => new[]
    {
        new Item("Status", "one dashboard",
            (s, ct) => StatusCommand.RunAsync(Copy<StatusSettings>(s), ct)),
        new Item("Watch", "the same, refreshing",
            (s, ct) => WatchCommand.RunAsync(Copy<WatchSettings>(s), ct)),
        new Item("Logs", "last 80 lines",
            (s, ct) => LogsCommand.RunAsync(Copy<LogsSettings>(s), ct)),
        new Item("Problems", "warnings and worse, last 24h", (s, ct) =>
        {
            var logs = Copy<LogsSettings>(s);
            return LogsCommand.RunAsync(new LogsSettings
            {
                DryRun = logs.DryRun, Json = logs.Json, NoUnicode = logs.NoUnicode,
                AssumeYes = logs.AssumeYes, ErrorsOnly = true, Since = "-24h", Tail = 200
            }, ct);
        }),
        new Item("Database", "quick_check and row counts", DbCommand.RunAsync),
        new Item("Legend", "what rss, wal and dep mean", LegendCommand.RunAsync)
    };

    private static IReadOnlyList<Item> Build() => new[]
    {
        new Item("Tests", "backend + frontend + contrast + density",
            (s, ct) => TestCommand.RunAsync(Copy<TestSettings>(s), ct)),
        new Item("Tests + smoke", "…and Playwright", (s, ct) =>
        {
            var t = Copy<TestSettings>(s);
            return TestCommand.RunAsync(new TestSettings
            {
                DryRun = t.DryRun, Json = t.Json, NoUnicode = t.NoUnicode, AssumeYes = t.AssumeYes, Smoke = true
            }, ct);
        }),
        new Item("Build", "Angular + server + shell",
            (s, ct) => BuildCommand.RunAsync(Copy<BuildSettings>(s), ct)),
        new Item("Deploy preflight", "the checks, and stop", (s, ct) =>
        {
            var d = Copy<DeploySettings>(s);
            return DeployCommand.RunAsync(new DeploySettings
            {
                DryRun = d.DryRun, Json = d.Json, NoUnicode = d.NoUnicode, AssumeYes = d.AssumeYes,
                NoLogo = d.NoLogo, PreflightOnly = true
            }, ct);
        }),
        // The one entry in this menu that stops production. It asks, with the default set to no, and
        // the question names the machine (ADR-119 decision 2).
        new Item("Deploy", "the real thing - asks first",
            (s, ct) => DeployCommand.RunAsync(Copy<DeploySettings>(s), ct))
    };

    private static IReadOnlyList<Item> Server() => new[]
    {
        new Item("Backup check", "what exists, and what does not", BackupVerifyCommand.RunAsync),
        new Item("Restart service", "asks first - production goes down briefly", RestartCommand.RunAsync)
    };

    // A submenu is an item that opens a list and returns whatever the chosen item returns; "← Back"
    // returns 0 without doing anything, which is what makes every level escapable.
    private static Item Group(string label, string hint, Func<IReadOnlyList<Item>> children) =>
        new(label, hint, async (settings, ct) =>
        {
            var session = Session.From(settings);
            var picked = Choose(session, label, children());
            return picked is null ? 0 : await picked.Run(settings, ct);
        });

    // The global flags travel into every command the menu launches; forgetting them would mean
    // --no-unicode worked from the command line and silently stopped working inside the menu.
    private static T Copy<T>(CedarSettings from) where T : CedarSettings, new() => new()
    {
        DryRun = from.DryRun,
        AssumeYes = from.AssumeYes,
        NoUnicode = from.NoUnicode,
        Json = from.Json,
        NoLogo = from.NoLogo
    };
}
