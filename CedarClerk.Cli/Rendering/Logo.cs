using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// The splash: a cedar in three tiers beside the wordmark, and an animation that grows the tree from
// the ground up before wiping the letters in and sweeping a highlight across them.
//
// It is decoration, and decoration must never be the reason a tool misbehaves — so every part of it
// degrades on its own:
//   * no Unicode  -> the same art transliterated to ASCII by one mapping, not a second asset kept
//                    in sync by hand
//   * narrow      -> the tree is dropped first, then the art entirely, before anything wraps
//   * redirected  -> nothing animates; a log file must not fill with frames
//   * a keypress  -> the animation ends immediately and the finished logo stays
public static class Logo
{
    private const int TreeWidth = 15;
    private const int Gap = 2;

    private static readonly string[] Tree =
    {
        "       ▲       ",
        "      ▟█▙      ",
        "     ▟███▙     ",
        "    ▟█████▙    ",
        "      ▐█▌      ",
        "     ▟███▙     ",
        "    ▟█████▙    ",
        "   ▟███████▙   ",
        "      ▐█▌      ",
        "   ▟███████▙   ",
        "  ▟█████████▙  ",
        "      ▐█▌      ",
        "     ▀▀▀▀▀     "
    };

    private static readonly string[] Wordmark =
    {
        " ██████╗███████╗██████╗  █████╗ ██████╗ ",
        "██╔════╝██╔════╝██╔══██╗██╔══██╗██╔══██╗",
        "██║     █████╗  ██║  ██║███████║██████╔╝",
        "██║     ██╔══╝  ██║  ██║██╔══██║██╔══██╗",
        "╚██████╗███████╗██████╔╝██║  ██║██║  ██║",
        " ╚═════╝╚══════╝╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝",
        "                                        ",
        " ██████╗██╗     ███████╗██████╗ ██╗  ██╗",
        "██╔════╝██║     ██╔════╝██╔══██╗██║ ██╔╝",
        "██║     ██║     █████╗  ██████╔╝█████╔╝ ",
        "██║     ██║     ██╔══╝  ██╔══██╗██╔═██╗ ",
        "╚██████╗███████╗███████╗██║  ██║██║  ██╗",
        " ╚═════╝╚══════╝╚══════╝╚═╝  ╚═╝╚═╝  ╚═╝"
    };

    private static readonly int WordWidth = Wordmark[0].Length;

    // One mapping instead of a parallel ASCII asset: a second copy would drift the first time the
    // art is touched, and nobody would notice until they ran in cmd.exe.
    private static readonly Dictionary<char, char> Transliteration = new()
    {
        ['█'] = '#', ['▓'] = '#', ['▒'] = '+', ['░'] = '.',
        ['╗'] = '+', ['╔'] = '+', ['╝'] = '+', ['╚'] = '+',
        ['═'] = '=', ['║'] = '|', ['╣'] = '+', ['╠'] = '+',
        ['▟'] = '/', ['▙'] = '\\', ['▐'] = '|', ['▌'] = '|',
        ['▀'] = '_', ['▄'] = '_', ['▲'] = '^'
    };

    public static void Show(IAnsiConsole console, Glyphs glyphs, string subtitle, bool animate)
    {
        var width = console.Profile.Width;
        var withTree = glyphs.IsUnicode && width >= TreeWidth + Gap + WordWidth + 2;
        var withArt = width >= WordWidth + 2;

        if (!withArt)
        {
            console.MarkupLine($"[{Palette.Hex(Palette.Accent)} bold]{CliConsts.DisplayName}[/] [grey]{Markup.Escape(subtitle)}[/]");
            console.WriteLine();
            return;
        }

        // Live needs a terminal it can rewind. Redirected output gets the finished frame once.
        var canAnimate = animate
                         && !Console.IsOutputRedirected
                         && console.Profile.Capabilities.Interactive
                         && console.Profile.Capabilities.Ansi;

        if (!canAnimate)
        {
            console.Write(Frame(Tree.Length, WordWidth, -1, withTree, glyphs));
            Footer(console, glyphs, subtitle);
            return;
        }

        try
        {
            Animate(console, glyphs, withTree);
        }
        catch (Exception)
        {
            // A terminal that cannot do live redraws still deserves a logo, not a stack trace.
            console.Write(Frame(Tree.Length, WordWidth, -1, withTree, glyphs));
        }

        Footer(console, glyphs, subtitle);
    }

    private static void Animate(IAnsiConsole console, Glyphs glyphs, bool withTree)
    {
        var skipped = false;

        console.Live(Frame(0, 0, -1, withTree, glyphs)).Start(ctx =>
        {
            void Draw(int treeRows, int columns, int shimmer, int delayMs)
            {
                if (skipped) return;
                ctx.UpdateTarget(Frame(treeRows, columns, shimmer, withTree, glyphs));
                ctx.Refresh();
                if (Skipped()) { skipped = true; return; }
                Thread.Sleep(delayMs);
            }

            // 1. The tree grows out of the ground.
            if (withTree)
                for (var grown = 1; grown <= Tree.Length; grown++)
                    Draw(grown, 0, -1, 38);

            // 2. The letters wipe in from the left, four columns at a time.
            for (var columns = 0; columns <= WordWidth; columns += 4)
                Draw(Tree.Length, columns, columns, 22);

            // 3. A highlight sweeps across the finished wordmark.
            for (var centre = -6; centre <= WordWidth + 6; centre += 3)
                Draw(Tree.Length, WordWidth, centre, 16);

            ctx.UpdateTarget(Frame(Tree.Length, WordWidth, -1, withTree, glyphs));
            ctx.Refresh();
        });
    }

    private static bool Skipped()
    {
        try
        {
            if (!Console.KeyAvailable) return false;
            Console.ReadKey(intercept: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            // No console input attached (a pipe, a CI agent) — nothing to skip with.
            return false;
        }
    }

    private static IRenderable Frame(int treeRows, int wordColumns, int shimmer, bool withTree, Glyphs glyphs)
    {
        var lines = new List<IRenderable>();

        for (var row = 0; row < Wordmark.Length; row++)
        {
            var markup = new StringBuilder();

            if (withTree)
            {
                // Grown from the ground up: rows below the waterline are drawn, rows above are not.
                var visible = row >= Tree.Length - treeRows;
                markup.Append(visible
                    ? Colorise(Text(Tree[row], glyphs), _ => TreeColour(row))
                    : new string(' ', TreeWidth));
                markup.Append(new string(' ', Gap));
            }

            var word = Text(Wordmark[row], glyphs);
            var revealed = word[..Math.Clamp(wordColumns, 0, word.Length)];
            markup.Append(Colorise(revealed, column => WordColour(column, wordColumns, shimmer)));

            lines.Add(new Markup(markup.ToString()));
        }

        return new Rows(lines);
    }

    private static string Text(string art, Glyphs glyphs)
    {
        if (glyphs.IsUnicode) return art;
        var builder = new StringBuilder(art.Length);
        foreach (var character in art)
            builder.Append(Transliteration.TryGetValue(character, out var plain) ? plain : character);
        return builder.ToString();
    }

    private static Color TreeColour(int row)
    {
        // Light at the crown, dark at the base — the way a conifer actually reads.
        var index = Palette.Cedar.Length - 1 - row * Palette.Cedar.Length / Tree.Length;
        return Palette.Cedar[Math.Clamp(index, 0, Palette.Cedar.Length - 1)];
    }

    private static Color WordColour(int column, int revealedTo, int shimmer)
    {
        // The leading edge of the wipe glows, then settles as the wipe moves past it.
        if (revealedTo < WordWidth && column >= revealedTo - 4)
            return Palette.Mix(Palette.Accent, Palette.Text, 0.85);

        if (shimmer >= 0)
        {
            var distance = Math.Abs(column - shimmer);
            if (distance <= 5) return Palette.Mix(Palette.Accent, Palette.Text, 0.8 - distance * 0.16);
        }
        return Palette.Accent;
    }

    // Groups runs of identical colour into single markup tags — one tag per character would be
    // correct and unreadably slow at 40 columns times 13 rows times 60 frames.
    private static string Colorise(string text, Func<int, Color> colourOf)
    {
        var builder = new StringBuilder();
        var run = new StringBuilder();
        Color? current = null;

        void Flush()
        {
            if (run.Length == 0 || current is null) return;
            builder.Append('[').Append(Palette.Hex(current.Value)).Append(']')
                   .Append(Markup.Escape(run.ToString()))
                   .Append("[/]");
            run.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var colour = colourOf(i);
            if (current is null || !colour.Equals(current.Value)) { Flush(); current = colour; }
            run.Append(text[i]);
        }
        Flush();
        return builder.ToString();
    }

    private static void Footer(IAnsiConsole console, Glyphs glyphs, string subtitle)
    {
        console.WriteLine();
        console.Write(Ui.Rule(glyphs, $"[{Palette.Hex(Palette.Muted)}]{Markup.Escape(subtitle)}[/]",
            console.Profile.Width));
        console.WriteLine();
    }
}
