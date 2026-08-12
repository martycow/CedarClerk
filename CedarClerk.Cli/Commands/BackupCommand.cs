using CedarClerk.Cli.Rendering;
using CedarClerk.Cli.Server;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// Reports what backup actually exists (ADR-118 decision 6).
//
// A nightly sqlite3 copy came back on 12.08.2026 (T-071): Marty's `~/bin/backup.sh` writes fourteen
// dated copies into {RemoteDataDir}/backups. This command reads that directory — so the path here and
// the path in the script are one fact in two places, and moving either alone makes the tool report an
// absence over a full directory. What it still names is what those copies are not: off the droplet,
// and never more than the database. `backup now` is deliberately not built: making a copy is server
// behaviour, and the tool wraps behaviour that exists rather than adding its own.
public sealed class BackupVerifyCommand : AsyncCommand<CedarSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        var data = session.Config.RemoteDataDir;
        var now = DateTimeOffset.UtcNow;

        var script = string.Join('\n', new[]
        {
            "echo '=== files ==='",
            $"ls -1t {data}/backups/* 2>/dev/null | head -5",
            "echo '=== newest ==='",
            $"stat -c '%Y %s %n' $(ls -1t {data}/backups/* 2>/dev/null | head -1) 2>/dev/null",
            "echo '=== cron ==='",
            "crontab -l 2>/dev/null | grep -v '^#' | grep -c . || true",
            "echo '=== scripts ==='",
            "ls -1 $HOME/bin/*.sh 2>/dev/null | head -5",
            "echo '=== live ==='",
            $"stat -c '%s' {data}/cedar.db 2>/dev/null",
            "echo '=== end ==='"
        });

        var result = await session.Runner.RunRemoteAsync(script, cancellationToken);
        var sections = ServerProbe.Split(result.StdOut);

        if (!sections.ContainsKey("end") && !session.DryRun)
        {
            session.Problem($"cannot reach {session.Config.Host}");
            return 1;
        }

        var copies = Section(sections, "files").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var newest = Section(sections, "newest").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cronEntries = int.TryParse(Section(sections, "cron").Trim(), out var cron) ? cron : 0;
        var scripts = Section(sections, "scripts").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var liveBytes = long.TryParse(Section(sections, "live").Trim(), out var live) ? live : 0;

        DateTimeOffset? newestAt = newest.Length > 0 && long.TryParse(newest[0], out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;

        if (session.Json)
        {
            session.WriteJson(new
            {
                localCopies = copies.Length,
                newestAt,
                scheduled = cronEntries > 0,
                scripts,
                liveDatabaseBytes = liveBytes,
                offBoxCopy = "DigitalOcean weekly droplet image (same account)"
            });
            return copies.Length > 0 ? 0 : 1;
        }

        var rows = new List<string>();

        if (copies.Length == 0)
        {
            rows.Add($"[{Palette.Hex(Palette.Danger)}]{session.Glyphs.Bad} no local copy[/] [grey]- {data}/backups is empty or absent[/]");
        }
        else
        {
            var age = newestAt is null ? TimeSpan.MaxValue : now - newestAt.Value;
            var (colour, mark) = age.TotalHours switch
            {
                < 24 => (Palette.Ok, session.Glyphs.Ok),
                < 48 => (Palette.Warn, session.Glyphs.Warn),
                _ => (Palette.Danger, session.Glyphs.Bad)
            };
            var size = newest.Length > 1 && long.TryParse(newest[1], out var bytes) ? Format.Size(bytes) : "?";
            rows.Add($"[{Palette.Hex(colour)}]{mark} newest[/] [grey]{Markup.Escape(Format.Age(newestAt, now))} · {size} · {copies.Length} kept[/]");
        }

        rows.Add(cronEntries > 0
            ? $"[{Palette.Hex(Palette.Ok)}]{session.Glyphs.Ok} schedule[/] [grey]{cronEntries} crontab entr(ies)[/]"
            : $"[{Palette.Hex(Palette.Danger)}]{session.Glyphs.Bad} schedule[/] [grey]no crontab - nothing makes a copy on its own[/]");

        rows.Add(scripts.Length > 0
            ? $"[grey]scripts[/]  {Markup.Escape(string.Join(", ", scripts))}"
            : $"[grey]scripts[/]  none in ~/bin");

        rows.Add($"[grey]live db[/]  {Format.Size(liveBytes)}");
        rows.Add("");
        rows.Add($"[{Palette.Hex(Palette.Warn)}]{session.Glyphs.Warn} what these copies are not:[/] [grey]off the droplet.[/]");
        rows.Add("[grey]  · they sit on the same disk as the database they copy[/]");
        rows.Add("[grey]  · they hold the database only - media/ is in the weekly image alone[/]");
        rows.Add("[grey]  · the weekly image lives in the same account as the droplet[/]");
        rows.Add($"[grey]  · T-147 closes this; {CliConsts.BinaryName} does not make backups (ADR-118)[/]");

        session.Console.Write(new Panel(new Markup(string.Join('\n', rows)))
        {
            Border = session.Glyphs.Border,
            BorderStyle = new Style(copies.Length == 0 ? Palette.Danger : Palette.Faint),
            Header = new PanelHeader($" [{Palette.Hex(Palette.Accent)}]backup[/] "),
            Padding = new Padding(1, 0, 1, 0)
        });

        return copies.Length > 0 ? 0 : 1;
    }

    private static string Section(Dictionary<string, string> sections, string name) =>
        sections.TryGetValue(name, out var body) ? body : "";
}
