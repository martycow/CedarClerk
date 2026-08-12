using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// What the short words on the status screen mean (Marty, 12.08.2026: "I sometimes do not know what
// the abbreviations stand for").
//
// The terms are grouped by the tile they appear on rather than alphabetically, so the glossary is
// read the way the screen is: find the box you are looking at, then the word in it. An A-to-Z list
// would be easier to build and would need you to already know which word you were confused by.
//
// Definitions are about *this* machine, not about the abbreviation in general. "rss - resident set
// size" is a translation, not an explanation; what makes it worth a line is that on a 2 GB droplet
// with no swap, this number is the one that gets the service killed.
public static class LegendView
{
    private sealed record Term(string Name, string Meaning);

    private sealed record Section(string Tile, Term[] Terms);

    private static readonly Section[] Sections =
    {
        new("service", new[]
        {
            new Term("up", "how long the unit has run without a restart. Resets on every deploy."),
            new Term("rss", "resident set size - RAM the process actually holds right now. On 2 GB with no swap, this is the number that gets it killed."),
            new Term("pid", "process id. A different one than last time means the service restarted."),
            new Term("tunnel", "cloudflared. Kestrel listens on loopback only, so the tunnel is the only way in from the internet."),
            new Term("no autostart", "the unit is disabled: a DigitalOcean maintenance reboot leaves the site down until somebody notices."),
        }),
        new("disk / memory", new[]
        {
            new Term("free", "unused disk. A deploy needs room for two copies of app/ at once."),
            new Term("avail", "memory obtainable without swapping, which is all of it - the droplet has no swap."),
            new Term("swap", "none configured. An out-of-memory event kills the service outright instead of slowing it down."),
        }),
        new("data", new[]
        {
            new Term("db", "cedar.db, the SQLite database itself."),
            new Term("wal", "write-ahead log. Turns amber past half the database size: a WAL that stops shrinking means checkpoints are not running."),
            new Term("all", "everything under data/ - database, media, thumbnails, key ring."),
            new Term("dep", "how long ago the current release was put in place."),
        }),
        new("graphs", new[]
        {
            new Term("cpu / memory", "real measurements from sysstat, sampled every 10 minutes. The default window is 24 hours because an hour is six points and does not draw."),
            new Term("scale", "zero-based but auto-topped. This droplet lives between 5% and 9%, so a fixed 0-100 axis would be a flat strip."),
        }),
        new("backup", new[]
        {
            new Term("no local copy", "there is no nightly copy on the droplet. The only backup is DigitalOcean's weekly whole-machine image, in the same account (T-071)."),
        }),
        new("tests", new[]
        {
            new Term("cells", "one square per test as the result arrives: empty not run yet, filled passed, crossed failed, dotted skipped."),
            new Term("verdict", "always the script's exit code, never the grid. A runner that changes its output format stops being parsed, and that must not turn a red run green."),
        })
    };

    public static IRenderable Render(Glyphs glyphs, int width)
    {
        var rows = new List<IRenderable>();

        // Under about 70 columns the two-column grid would wrap every definition mid-word, which is
        // worse than a plain indented list.
        var wide = width >= 70;

        foreach (var section in Sections)
        {
            rows.Add(new Markup(
                $"[{Palette.Hex(Palette.Accent)}]{glyphs.Swatch}[/] [{Palette.Hex(Palette.Text)}]{Markup.Escape(section.Tile)}[/]"));

            if (wide)
            {
                var grid = new Grid();
                grid.AddColumn(new GridColumn { NoWrap = true, Width = 14, Padding = new Padding(2, 0, 1, 0) });
                grid.AddColumn(new GridColumn { Padding = new Padding(0, 0, 0, 0) });

                foreach (var term in section.Terms)
                    grid.AddRow(
                        new Markup($"[{Palette.Hex(Palette.Accent)}]{Markup.Escape(term.Name)}[/]"),
                        new Markup($"[grey]{Markup.Escape(term.Meaning)}[/]"));

                rows.Add(grid);
            }
            else
            {
                foreach (var term in section.Terms)
                    rows.Add(new Markup(
                        $"  [{Palette.Hex(Palette.Accent)}]{Markup.Escape(term.Name)}[/]  [grey]{Markup.Escape(term.Meaning)}[/]"));
            }

            rows.Add(new Text(" "));
        }

        // The last spacer would put a blank line against the bottom border.
        rows.RemoveAt(rows.Count - 1);

        return Ui.Panel(glyphs, "legend", new Rows(rows), Palette.Accent);
    }

    // The pointer that makes the glossary findable from the screen it explains. Without it the
    // legend is a command you have to already know about to discover.
    public static IRenderable Hint(Glyphs glyphs) =>
        new Markup($"[grey35]{glyphs.Dot} rss, wal, dep? [/][grey]{CliConsts.BinaryName} legend[/][grey35] spells them out.[/]");
}
