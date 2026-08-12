using CedarClerk.Cli.Server;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// The status dashboard, built as one Renderable so `status` and `watch` draw the identical thing —
// watch simply hands it to Live. Two screens that drift apart is the usual way a "live" view starts
// telling a different story from the static one.
public sealed class StatusView
{
    private readonly Glyphs _glyphs;
    private readonly string _host;

    public StatusView(Glyphs glyphs, string host)
    {
        _glyphs = glyphs;
        _host = host;
    }

    public IRenderable Render(ServerSnapshot snapshot, DateTimeOffset now, int width = 80)
    {
        var rows = new List<IRenderable> { Header(snapshot, width) };

        if (!snapshot.Reachable)
        {
            rows.Add(Ui.Panel(_glyphs, "unreachable",
                $"[{Palette.Hex(Palette.Danger)}]{_glyphs.Bad} the server did not answer[/]\n" +
                $"[grey]{Markup.Escape(Truncate(snapshot.Error, 300))}[/]",
                Palette.Danger));
            return new Rows(rows);
        }

        rows.Add(new Grid()
            .AddColumn().AddColumn().AddColumn().AddColumn()
            .AddRow(ServicePanel(snapshot, now), DiskPanel(snapshot), MemoryPanel(snapshot), DataPanel(snapshot, now)));

        rows.Add(ChartsPanel(snapshot));
        rows.Add(BreakdownPanel(snapshot, width));
        rows.Add(BackupLine(snapshot, now));

        return new Rows(rows);
    }

    private IRenderable Header(ServerSnapshot snapshot, int width)
    {
        var live = snapshot.Health.Answered ? $"v{snapshot.Health.Version}" : "down";
        var local = snapshot.LocalVersion;

        // The version pair sits in the title bar of every screen so a deploy cannot be aimed at the
        // wrong place without it being on screen at the moment of the decision.
        var versions = snapshot.VersionMatches
            ? $"[{Palette.Hex(Palette.Ok)}]{live}[/] [grey]= working copy[/]"
            : $"[{Palette.Hex(Palette.Warn)}]{live}[/] [grey]{_glyphs.Arrow}[/] [{Palette.Hex(Palette.Accent)}]v{local}[/] [grey]working copy[/]";

        return Ui.Rule(_glyphs, $"[{Palette.Hex(Palette.Text)}]{Markup.Escape(_host)}[/]  {versions}", width);
    }

    private IRenderable ServicePanel(ServerSnapshot snapshot, DateTimeOffset now)
    {
        var service = snapshot.Service;
        var mark = service.IsActive ? _glyphs.Ok : _glyphs.Bad;
        var colour = service.IsActive ? Palette.Ok : Palette.Danger;

        var lines = new List<string>
        {
            $"[{Palette.Hex(colour)}]{mark} {Markup.Escape(service.ActiveState)}[/] [grey]{Markup.Escape(service.SubState)}[/]",
            $"[grey]up[/]  {Markup.Escape(Format.CoarseAge(service.SinceUtc, now).Replace(" ago", ""))}",
            $"[grey]rss[/] {Format.Size(service.MemoryBytes)}",
            $"[grey]pid[/] {service.MainPid}"
        };

        if (service.Restarts > 0)
            lines.Add($"[{Palette.Hex(Palette.Warn)}]{_glyphs.Warn} {service.Restarts} restart(s)[/]");

        // T-143: the unit is disabled, so a DigitalOcean maintenance reboot leaves the site down
        // until somebody notices. Nothing else on this machine reports that, so it is on the tile.
        if (!service.SurvivesReboot)
            lines.Add($"[{Palette.Hex(Palette.Warn)}]{_glyphs.Warn} no autostart[/]");

        var tunnelMark = snapshot.TunnelActive ? _glyphs.Ok : _glyphs.Bad;
        var tunnelColour = snapshot.TunnelActive ? Palette.Ok : Palette.Danger;
        lines.Add($"[{Palette.Hex(tunnelColour)}]{tunnelMark} tunnel[/] [grey]{(snapshot.TunnelActive ? "active" : "down")}[/]");

        return Tile("service", string.Join('\n', lines), colour);
    }

    private IRenderable DiskPanel(ServerSnapshot snapshot)
    {
        var disk = snapshot.Disk;
        var body =
            Meter(disk.UsedFraction) + "\n" +
            $"[grey]free[/] {Format.Size(disk.FreeBytes)}\n" +
            $"[grey]size[/] {Format.Size(disk.TotalBytes)}\n" +
            $"[grey]used[/] {Format.Percent(disk.UsedFraction)}";
        return Tile("disk", body, Palette.ForLoad(disk.UsedFraction));
    }

    private IRenderable MemoryPanel(ServerSnapshot snapshot)
    {
        var memory = snapshot.Memory;
        var lines = new List<string>
        {
            Meter(memory.UsedFraction),
            $"[grey]avail[/] {Format.Size(memory.AvailableBytes)}",
            $"[grey]total[/] {Format.Size(memory.TotalBytes)}"
        };

        // No swap means an out-of-memory event kills the service outright rather than slowing it
        // down, on a box with 2 GB (.claude/rules/production-environment.md).
        lines.Add(memory.SwapTotalBytes == 0
            ? $"[{Palette.Hex(Palette.Warn)}]{_glyphs.Warn} no swap[/]"
            : $"[grey]swap[/] {Format.Size(memory.SwapTotalBytes)}");

        return Tile("memory", string.Join('\n', lines), Palette.ForLoad(memory.UsedFraction));
    }

    private IRenderable DataPanel(ServerSnapshot snapshot, DateTimeOffset now)
    {
        // A WAL that stops shrinking is the early sign of a checkpoint that never runs, and it is
        // invisible everywhere else.
        var walShare = snapshot.DatabaseBytes <= 0 ? 0 : (double)snapshot.WalBytes / snapshot.DatabaseBytes;
        var walColour = walShare > 0.5 ? Palette.Warn : Palette.Muted;

        var body =
            $"[grey]db[/]   {Format.Size(snapshot.DatabaseBytes)}\n" +
            $"[{Palette.Hex(walColour)}]wal[/]  {Format.Size(snapshot.WalBytes)}\n" +
            $"[grey]all[/]  {Format.Size(snapshot.DataTotalBytes)}\n" +
            $"[grey]dep[/]  {Markup.Escape(Format.CoarseAge(snapshot.LastDeployUtc, now))}";

        return Tile("data", body, Palette.Accent);
    }

    private IRenderable ChartsPanel(ServerSnapshot snapshot)
    {
        var style = _glyphs.GraphStyle;
        var hours = CliConsts.DefaultHistoryHours;

        // Zero-based but auto-topped. A fixed 0–100 axis is defensible and useless here: this
        // droplet lives between 5% and 9% CPU, so a full-scale chart is a flat strip along the
        // bottom and a spike to 20% looks identical to no spike at all. The top tick carries the
        // real number, so the scale is stated rather than assumed.
        var cpu = new BrailleChart
        {
            Values = snapshot.CpuHistory,
            Rows = 4,
            Min = 0,
            Unit = "%",
            LeftLabel = $"{hours}h ago",
            RightLabel = "now",
            Line = Palette.Accent,
            Style = style
        };

        var memory = new BrailleChart
        {
            Values = snapshot.MemoryHistory,
            Rows = 4,
            Min = 0,
            Unit = "%",
            LeftLabel = $"{hours}h ago",
            RightLabel = "now",
            Line = Palette.Cedar[3],
            Style = style
        };

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow(
            Tile($"cpu   {Last(snapshot.CpuHistory)}", cpu, Palette.Accent),
            Tile($"memory   {Last(snapshot.MemoryHistory)}", memory, Palette.Cedar[3]));
        return grid;
    }

    private IRenderable BreakdownPanel(ServerSnapshot snapshot, int width)
    {
        var slices = snapshot.DataSizes
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .Select(entry => (entry.Key, entry.Value))
            .ToList();

        if (snapshot.DatabaseBytes > 0)
            slices.Add(("database", snapshot.DatabaseBytes + snapshot.WalBytes));

        // Four for the panel's own border and padding, so the bar meets the frame instead of
        // stopping short of it.
        return Tile($"data/   {Format.Size(snapshot.DataTotalBytes)}",
            Breakdown(slices, Math.Max(10, width - 4)), Palette.Muted);
    }

    // Hand-drawn rather than Spectre's BreakdownChart, for one reason: that widget draws its bar and
    // its legend swatch with fixed Unicode characters whatever the profile says, so it was the last
    // thing on the screen still printing box glyphs under --no-unicode.
    //
    // Megabytes are shown as real sizes. A percentage would repeat what the bar already says and
    // drop the one number a person came here for.
    private IRenderable Breakdown(IReadOnlyList<(string Name, long Bytes)> slices, int width)
    {
        if (slices.Count == 0) return new Markup("[grey]nothing measured[/]");

        var total = (double)slices.Sum(slice => slice.Bytes);
        var bar = new System.Text.StringBuilder();
        var legend = new Grid();

        // Legend entries go in a grid rather than a joined line, so a wrap breaks between items
        // instead of through the middle of "dataprotection-keys".
        var columns = Math.Clamp(width / 26, 1, 4);
        for (var i = 0; i < columns; i++) legend.AddColumn();

        var cells = new List<string>();
        var drawn = 0;

        for (var i = 0; i < slices.Count; i++)
        {
            var colour = Palette.Series[i % Palette.Series.Length];
            // The last slice takes whatever the rounding left over, so the bar is always exactly full.
            var span = i == slices.Count - 1
                ? Math.Max(0, width - drawn)
                : (int)Math.Round(width * slices[i].Bytes / total);
            drawn += span;

            if (span > 0)
                bar.Append($"[{Palette.Hex(colour)}]{Repeat(_glyphs.Full, span)}[/]");

            cells.Add($"[{Palette.Hex(colour)}]{_glyphs.Swatch}[/] [grey]{Markup.Escape(slices[i].Name)}[/] " +
                      $"{Format.Size(slices[i].Bytes)}");
        }

        for (var row = 0; row < cells.Count; row += columns)
            legend.AddRow(cells.Skip(row).Take(columns)
                .Concat(Enumerable.Repeat("", Math.Max(0, columns - (cells.Count - row))))
                .ToArray());

        return new Rows(new Markup(bar.ToString()), legend);
    }

    private IRenderable BackupLine(ServerSnapshot snapshot, DateTimeOffset now)
    {
        // ADR-118 decision 6: there is no nightly copy on this machine, and the honest answer is to
        // say which kind of backup exists rather than to leave the row looking satisfied.
        if (snapshot.LastBackupUtc is null)
            return new Markup(
                $"[{Palette.Hex(Palette.Danger)}]{_glyphs.Bad} backup[/]  " +
                "[grey]no local copy on the droplet - the only one is DigitalOcean's weekly whole-machine image " +
                $"(T-071)[/]{System.Environment.NewLine}");

        var age = now - snapshot.LastBackupUtc.Value;
        var (colour, mark) = age.TotalHours switch
        {
            < 24 => (Palette.Ok, _glyphs.Ok),
            < 48 => (Palette.Warn, _glyphs.Warn),
            _ => (Palette.Danger, _glyphs.Bad)
        };
        var schedule = snapshot.HasBackupSchedule ? "scheduled" : "no cron entry";

        return new Markup(
            $"[{Palette.Hex(colour)}]{mark} backup[/]  " +
            $"[grey]{Markup.Escape(Format.Age(snapshot.LastBackupUtc, now))} · {schedule}[/]{System.Environment.NewLine}");
    }

    // A bar is never alone: the percentage sits beside it, because the bar's colour is the only
    // other signal and colour is not always available (ADR-118 decision 7).
    private string Meter(double fraction, int width = 12)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * width);
        var colour = Palette.Hex(Palette.ForLoad(fraction));
        return $"[{colour}]{Repeat(_glyphs.Full, filled)}[/][grey35]{Repeat(_glyphs.Empty, width - filled)}[/] " +
               $"[{colour}]{Format.Percent(fraction)}[/]";
    }

    private static string Repeat(string glyph, int count) =>
        count <= 0 ? "" : string.Concat(Enumerable.Repeat(glyph, count));

    private static string Last(IReadOnlyList<double> series) =>
        series.Count == 0 ? "[grey]no data[/]" : $"[grey]{series[^1]:0.#}%[/]";

    private IRenderable Tile(string title, string markup, Color colour) =>
        Ui.Panel(_glyphs, title, markup, colour);

    private IRenderable Tile(string title, IRenderable body, Color colour) =>
        Ui.Panel(_glyphs, title, body, colour);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
