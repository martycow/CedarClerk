using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// The menu as one live frame containing the logo, which is why this exists instead of Spectre's
// SelectionPrompt: that prompt owns the bottom of the screen and redraws only its own list, so a
// logo above it can never move (ADR-118 decision 10). Owning the frame costs the arrow-key handling
// below and buys an animating logo, a header the previous command cannot scroll away, and submenus
// that replace the screen instead of stacking.
//
// Degrades to SelectionPrompt whenever it cannot have the whole screen — a menu that draws half a
// logo and eats the keyboard is worse than a plain list.
public static class Chooser
{
    public readonly record struct Entry(string Name, string Hint);

    // Every frame is redrawn at this rate at most; between frames the loop sleeps. The redraw is
    // skipped entirely when nothing changed, so an idle menu is a sleeping thread and not a
    // 25-times-a-second repaint of a terminal that may be on the other end of an ssh session.
    private const int FrameMs = 40;

    // Returns the index of the chosen entry, or null when the user backed out with the last entry,
    // Escape or Ctrl+C.
    public static int? Pick(
        IAnsiConsole console, Glyphs glyphs, string header, string? title, IReadOnlyList<Entry> entries)
    {
        var width = console.Profile.Width;
        var withArt = Logo.FitsArt(width);
        var withTree = Logo.FitsTree(width, glyphs);
        var rowsNeeded = (withArt ? Logo.Height + 1 : 0) + (title is null ? 0 : 1) + entries.Count + 5;

        return CanAnimate(console, rowsNeeded)
            ? Animated(console, glyphs, header, title, entries, withArt, withTree, width)
            : Plain(console, glyphs, header, title, entries, width);
    }

    private static int? Animated(
        IAnsiConsole console, Glyphs glyphs, string header, string? title, IReadOnlyList<Entry> entries,
        bool withArt, bool withTree, int width)
    {
        // The frame is the screen for as long as it is up; leaving the previous one behind would
        // stack two logos the moment anyone opened a submenu.
        console.Clear();

        var clock = Stopwatch.StartNew();
        var selected = 0;
        int? chosen = null;

        console.Live(new Text(""))
            .AutoClear(false)
            .Start(ctx =>
            {
                var drawn = (Idle: new Logo.Idle(int.MinValue, -1), Selected: -1);

                while (true)
                {
                    var idle = withArt ? Logo.IdleAt(clock.Elapsed) : Logo.Idle.Still;
                    if ((idle, selected) != drawn)
                    {
                        ctx.UpdateTarget(Compose(glyphs, header, title, entries, selected, idle, withArt, withTree, width));
                        ctx.Refresh();
                        drawn = (idle, selected);
                    }

                    if (!KeyWaiting()) { Thread.Sleep(FrameMs); continue; }

                    var key = Console.ReadKey(intercept: true);
                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow or ConsoleKey.K:
                            selected = (selected - 1 + entries.Count) % entries.Count;
                            break;
                        case ConsoleKey.DownArrow or ConsoleKey.J or ConsoleKey.Tab:
                            selected = (selected + 1) % entries.Count;
                            break;
                        case ConsoleKey.Home:
                            selected = 0;
                            break;
                        case ConsoleKey.End:
                            selected = entries.Count - 1;
                            break;
                        case ConsoleKey.Enter or ConsoleKey.Spacebar:
                            chosen = selected;
                            return;
                        case ConsoleKey.Escape:
                            // Escape means the same as picking the last row, which is always the way
                            // out — one behaviour rather than a second kind of exit.
                            chosen = entries.Count - 1;
                            return;
                    }
                }
            });

        return chosen;
    }

    private static IRenderable Compose(
        Glyphs glyphs, string header, string? title, IReadOnlyList<Entry> entries, int selected,
        Logo.Idle idle, bool withArt, bool withTree, int width)
    {
        var rows = new List<IRenderable>();

        if (withArt)
        {
            rows.Add(Logo.Still(glyphs, withTree, idle));
            rows.Add(new Text(" "));
        }

        rows.Add(Ui.Rule(glyphs, header, width));
        rows.Add(new Text(" "));

        if (title is not null)
            rows.Add(new Markup($"[grey]{Markup.Escape(title)}[/]"));

        var column = entries.Max(entry => entry.Name.Length);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var name = Markup.Escape(entry.Name.PadRight(column));
            var hint = entry.Hint.Length == 0 ? "" : $"   [grey35]{Markup.Escape(entry.Hint)}[/]";

            rows.Add(new Markup(index == selected
                // The cursor glyph carries the selection as well as the colour does, because one of
                // them survives a monochrome terminal and the other does not (ADR-118 decision 7).
                ? $"[{Palette.Hex(Palette.Accent)}]{glyphs.Arrow} {name}[/]{hint}"
                : $"{new string(' ', glyphs.Arrow.Length)} [{Palette.Hex(Palette.Text)}]{name}[/]{hint}"));
        }

        rows.Add(new Text(" "));
        rows.Add(new Markup($"[grey35]{(glyphs.IsUnicode ? "↑↓" : "up/down")} move  ·  enter choose  ·  esc back[/]"));

        return new Rows(rows);
    }

    // The fallback: the header printed, then Spectre's own list. No animation, no screen ownership,
    // and no keyboard handling of ours.
    private static int? Plain(
        IAnsiConsole console, Glyphs glyphs, string header, string? title, IReadOnlyList<Entry> entries, int width)
    {
        console.Write(Ui.Rule(glyphs, header, width));

        var labels = entries
            .Select(entry => entry.Hint.Length == 0
                ? entry.Name
                : $"{entry.Name}   [grey35]{Markup.Escape(entry.Hint)}[/]")
            .ToList();

        var prompt = new SelectionPrompt<string>()
            .PageSize(14)
            .HighlightStyle(new Style(Palette.Accent))
            .AddChoices(labels);

        if (title is not null) prompt.Title($"[grey]{Markup.Escape(title)}[/]");

        var picked = console.Prompt(prompt);
        var index = labels.IndexOf(picked);
        return index < 0 ? null : index;
    }

    private static bool CanAnimate(IAnsiConsole console, int rowsNeeded)
    {
        if (Console.IsOutputRedirected || Console.IsInputRedirected) return false;
        if (!console.Profile.Capabilities.Interactive || !console.Profile.Capabilities.Ansi) return false;

        try
        {
            // Live truncates what does not fit, and a menu with its last two options cut off is a
            // menu you cannot use. Short windows get the plain list, which scrolls.
            return Console.WindowHeight >= rowsNeeded;
        }
        catch (Exception)
        {
            // No real console attached — nothing to measure, so nothing to animate.
            return false;
        }
    }

    private static bool KeyWaiting()
    {
        try
        {
            return Console.KeyAvailable;
        }
        catch (InvalidOperationException)
        {
            // Input redirected after the capability check; treat it as "no key", never as a crash.
            return false;
        }
    }
}
