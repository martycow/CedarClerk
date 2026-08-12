using CedarClerk.Cli.Rendering;
using CedarClerk.Cli.Server;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CedarClerk.Cli.Commands;

// A read-only look at the live database.
//
// Every query goes through `sqlite3 -readonly`, which is not a convention but an enforcement: the
// process cannot write even if a future edit here got a statement wrong. PRAGMA quick_check rather
// than integrity_check because the latter walks the whole file, and this runs against a database
// serving live traffic.
public sealed class DbCommand : AsyncCommand<CedarSettings>
{
    private static readonly string[] Tables =
    {
        "AspNetUsers", "Drafts", "Projects", "GameTasks", "Assets",
        "Comments", "ScheduledPosts", "Channels", "__EFMigrationsHistory"
    };

    protected override Task<int> ExecuteAsync(CommandContext context, CedarSettings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    // The body lives in a public method so the interactive menu can call it directly: Spectre makes
    // ExecuteAsync protected, and a menu is just another caller.
    public static async Task<int> RunAsync(CedarSettings settings, CancellationToken cancellationToken)
    {
        var session = Session.From(settings);
        var database = $"{session.Config.RemoteDataDir}/cedar.db";

        var script = string.Join('\n', new[]
        {
            "echo '=== check ==='",
            $"sqlite3 -readonly {database} 'PRAGMA quick_check;' 2>&1 | head -3",
            "echo '=== pages ==='",
            $"sqlite3 -readonly {database} 'PRAGMA page_count; PRAGMA page_size; PRAGMA journal_mode;' 2>/dev/null",
            "echo '=== migration ==='",
            $"sqlite3 -readonly {database} 'select MigrationId from __EFMigrationsHistory order by MigrationId desc limit 1;' 2>/dev/null",
            "echo '=== counts ==='",
            string.Join('\n', Tables.Select(table =>
                $"n=$(sqlite3 -readonly {database} 'select count(*) from {table};' 2>/dev/null) && echo '{table}='$n")),
            "echo '=== files ==='",
            $"stat -c '%s %n' {database} {database}-wal 2>/dev/null",
            "echo '=== end ==='"
        });

        var result = await session.Runner.RunRemoteAsync(script, cancellationToken);
        var sections = ServerProbe.Split(result.StdOut);

        if (!sections.ContainsKey("end") && !session.DryRun)
        {
            session.Problem($"cannot read the database on {session.Config.Host}");
            session.Note(result.StdErr.Trim());
            return 1;
        }

        var check = Section(sections, "check").Trim();
        var counts = ParseCounts(Section(sections, "counts"));
        var files = Parsing.SystemFacts.ParseStatSizes(Section(sections, "files"));
        var pages = Section(sections, "pages").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var migration = Section(sections, "migration").Trim();

        if (session.Json)
        {
            session.WriteJson(new { integrity = check, migration, counts, files });
            return check == "ok" ? 0 : 1;
        }

        var healthy = check.Equals("ok", StringComparison.OrdinalIgnoreCase);
        session.Console.MarkupLine(
            $"[{Palette.Hex(healthy ? Palette.Ok : Palette.Danger)}]{(healthy ? session.Glyphs.Ok : session.Glyphs.Bad)} quick_check[/] " +
            $"[grey]{Markup.Escape(check.Length == 0 ? "no answer" : check)}[/]");

        var db = files.FirstOrDefault(f => f.Key.EndsWith("cedar.db", StringComparison.Ordinal)).Value;
        var wal = files.FirstOrDefault(f => f.Key.EndsWith("-wal", StringComparison.Ordinal)).Value;

        session.Console.MarkupLine(
            $"[grey]file[/] {Format.Size(db)}   [grey]wal[/] {Format.Size(wal)}   " +
            $"[grey]mode[/] {Markup.Escape(pages.Length > 2 ? pages[2] : "?")}   " +
            $"[grey]migration[/] {Markup.Escape(migration.Length > 0 ? migration : "?")}");
        session.Console.WriteLine();

        var table = new Table { Border = session.Glyphs.TableBorder, BorderStyle = new Style(Palette.Faint) };
        table.AddColumn(new TableColumn($"[{Palette.Hex(Palette.Accent)}]table[/]"));
        table.AddColumn(new TableColumn($"[{Palette.Hex(Palette.Accent)}]rows[/]").RightAligned());

        foreach (var name in Tables)
        {
            // A table that does not exist on this deploy is simply not listed — the schema moves,
            // and a hardcoded list going stale must not read as a missing table.
            if (!counts.TryGetValue(name, out var rows)) continue;
            table.AddRow($"[grey]{name}[/]", rows.ToString("N0"));
        }
        session.Console.Write(table);

        return healthy ? 0 : 1;
    }

    private static Dictionary<string, long> ParseCounts(string text)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            if (long.TryParse(line[(split + 1)..].Trim(), out var value))
                counts[line[..split].Trim()] = value;
        }
        return counts;
    }

    private static string Section(Dictionary<string, string> sections, string name) =>
        sections.TryGetValue(name, out var body) ? body : "";
}
