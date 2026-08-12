using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// The splash: a cedar in three tiers beside the wordmark, and an animation that grows the tree from
// the ground up before wiping the letters in and sweeping a highlight across them.
//
// It also keeps moving while the menu is on screen (Marty, 12.08.2026): a highlight crosses the
// letters twice a cycle, and once every ten seconds each letter hops a single row, one after the
// next. The whole cycle is a pure function of elapsed time (IdleAt) rather than a running
// coroutine, so the menu can ask "what should the logo look like right now" and redraw only when
// that answer changes — an idle menu costs nothing, which matters over ssh.
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

    // Both words are five glyphs of exactly eight columns, which is what lets a "letter" be a column
    // range rather than a parsed shape. Asserted by a test, because the art is edited by hand.
    private const int LetterWidth = 8;

    // The idle cycle, in seconds. The hop is late in the cycle so it reads as its own event rather
    // than as something the second shimmer did.
    private const double CycleSeconds = 10.0;
    private const double ShimmerSeconds = 1.4;
    private const double HopStart = 8.0;
    private const double HopStagger = 0.13;
    private const double HopHold = 0.22;

    private static readonly double[] ShimmerStarts = { 0.0, 5.0 };

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

    public static int LetterCount => WordWidth / LetterWidth;

    // One row taller than the art. That extra blank row at the top is the headroom the hop jumps
    // into: without it the top row of CEDAR would be clipped by the top of the block instead of
    // rising, and the "jump" would read as the letter losing its lid.
    public static int ArtRows => Wordmark.Length + 1;

    // …plus a blank row and the subtitle beneath it (Marty, 12.08.2026).
    public static int Height => ArtRows + 2;

    // What the subtitle says, and why it says it at all: the wordmark is the product's, and a
    // console that opens with the product's wordmark and nothing else claims to be the product. This
    // line is the correction — smaller type, letter-spaced name, plain words for what it is.
    //
    // It is kept inside the wordmark's own width so the block never becomes wider than the art, which
    // is what every "does the logo fit" check measures.
    private const string SubtitleWords = "operations console";

    // Where the animation is in its cycle, as data rather than as a frame — so a caller can compare
    // two of these and skip a redraw when nothing moved.
    public readonly record struct Idle(int Shimmer, int HopMask)
    {
        public static Idle Still => new(-1, 0);
    }

    public static Idle IdleAt(TimeSpan elapsed)
    {
        var time = elapsed.TotalSeconds % CycleSeconds;

        var shimmer = -1;
        foreach (var start in ShimmerStarts)
        {
            var into = time - start;
            if (into < 0 || into >= ShimmerSeconds) continue;
            // From just off the left edge to just off the right, so the highlight enters and leaves
            // rather than appearing in the middle of the first letter.
            shimmer = (int)Math.Round(-6 + into / ShimmerSeconds * (WordWidth + 12));
        }

        return new Idle(shimmer, HopMaskAt(time - HopStart));
    }

    // Letter i is up for HopHold, starting HopStagger later than the letter before it: the wave, in
    // one line. Outside the wave every bit is clear, which is the resting state.
    private static int HopMaskAt(double intoWave)
    {
        var mask = 0;
        for (var letter = 0; letter < LetterCount; letter++)
        {
            var into = intoWave - letter * HopStagger;
            if (into >= 0 && into < HopHold) mask |= 1 << letter;
        }
        return mask;
    }

    private static double HopWaveSeconds => HopStagger * (LetterCount - 1) + HopHold;

    public static bool FitsArt(int width) => width >= WordWidth + 2;

    public static bool FitsTree(int width, Glyphs glyphs) =>
        glyphs.IsUnicode && width >= TreeWidth + Gap + WordWidth + 2;

    // The finished logo at a given point in the idle cycle. The menu draws this every frame.
    public static IRenderable Still(Glyphs glyphs, bool withTree, Idle idle) =>
        Frame(Tree.Length, WordWidth, idle.Shimmer, idle.HopMask, withTree, glyphs, int.MaxValue);

    // "c e d a r  ·  operations console" — the name letter-spaced so it reads as a mark rather than a
    // word, the description in ordinary lowercase. The separator is the middle dot, which survives
    // CP437 and CP1252; ASCII mode gets a hyphen, like every other em-dash-shaped thing here.
    internal static string SubtitleText(Glyphs glyphs) =>
        $"{SubtitleWords}  {(glyphs.IsUnicode ? "·" : "-")}  (c) Moo.exe 2026";

    private static string Spaced(string word) => string.Join(' ', word.ToCharArray());

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
        var withTree = FitsTree(width, glyphs);

        if (!FitsArt(width))
        {
            // Too narrow for the art, so the one line that survives has to carry what the art and the
            // subtitle carried together: whose console this is, and that it is a console.
            console.MarkupLine(
                $"[{Palette.Hex(Palette.Accent)} bold]{CliConsts.DisplayName}[/] " +
                $"[{Palette.Hex(Palette.Muted)}]{CliConsts.Tagline}[/] [grey]{Markup.Escape(subtitle)}[/]");
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
            console.Write(Still(glyphs, withTree, Idle.Still));
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
            console.Write(Still(glyphs, withTree, Idle.Still));
        }

        Footer(console, glyphs, subtitle);
    }

    private static void Animate(IAnsiConsole console, Glyphs glyphs, bool withTree)
    {
        var skipped = false;

        var subtitleWidth = SubtitleText(glyphs).Length;

        console.Live(Frame(0, 0, -1, 0, withTree, glyphs, 0)).Start(ctx =>
        {
            void Draw(int treeRows, int columns, int shimmer, int hopMask, int subtitle, int delayMs)
            {
                if (skipped) return;
                ctx.UpdateTarget(Frame(treeRows, columns, shimmer, hopMask, withTree, glyphs, subtitle));
                ctx.Refresh();
                if (Skipped()) { skipped = true; return; }
                Thread.Sleep(delayMs);
            }

            // 1. The tree grows out of the ground.
            if (withTree)
                for (var grown = 1; grown <= Tree.Length; grown++)
                    Draw(grown, 0, -1, 0, 0, 38);

            // 2. The letters wipe in from the left, four columns at a time.
            for (var columns = 0; columns <= WordWidth; columns += 4)
                Draw(Tree.Length, columns, columns, 0, 0, 22);

            // 3. A highlight sweeps across the finished wordmark.
            for (var centre = -6; centre <= WordWidth + 6; centre += 3)
                Draw(Tree.Length, WordWidth, centre, 0, 0, 16);

            // 4. The subtitle slides out from underneath, two characters at a time. It goes after the
            //    sweep rather than with it: the wordmark has to have finished being the thing on
            //    screen before something else is allowed to qualify it.
            for (var shown = 0; shown <= subtitleWidth; shown += 2)
                Draw(Tree.Length, WordWidth, -1, 0, shown, 18);

            // 5. …and the letters hop once, with the same wave the idle cycle uses. Introducing the
            //    effect here means it is recognised when it comes back ten seconds later, instead of
            //    looking like the terminal glitched.
            for (var elapsed = 0.0; elapsed <= HopWaveSeconds; elapsed += 0.04)
                Draw(Tree.Length, WordWidth, -1, HopMaskAt(elapsed), int.MaxValue, 40);

            ctx.UpdateTarget(Frame(Tree.Length, WordWidth, -1, 0, withTree, glyphs, int.MaxValue));
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

    private static IRenderable Frame(
        int treeRows, int wordColumns, int shimmer, int hopMask, bool withTree, Glyphs glyphs, int subtitleColumns)
    {
        var lines = new List<IRenderable>();

        // Everything is drawn one row lower than the art it comes from, so that "hopped" is simply
        // "read one row further down" — the same lookup for a letter that is up and a letter that
        // is not, instead of a second layout for the airborne case.
        for (var row = 0; row < ArtRows; row++)
        {
            var markup = new StringBuilder();
            var artRow = row - 1;

            if (withTree)
            {
                // Grown from the ground up: rows below the waterline are drawn, rows above are not.
                var visible = artRow >= 0 && artRow >= Tree.Length - treeRows;
                markup.Append(visible
                    ? Colorise(Text(Tree[artRow], glyphs), _ => TreeColour(artRow))
                    : new string(' ', TreeWidth));
                markup.Append(new string(' ', Gap));
            }

            var word = Text(WordRow(artRow, hopMask), glyphs);
            var revealed = word[..Math.Clamp(wordColumns, 0, word.Length)];
            markup.Append(Colorise(revealed, column => WordColour(column, wordColumns, shimmer)));

            lines.Add(new Markup(markup.ToString()));
        }

        lines.Add(new Markup(""));
        lines.Add(new Markup(Subtitle(glyphs, withTree, subtitleColumns)));

        return new Rows(lines);
    }

    // The subtitle slides out from under the wordmark: characters are revealed left to right, and the
    // few at the leading edge glow before settling. It is the same trick the wordmark's own wipe uses
    // one step earlier, so the two read as one movement rather than as two effects.
    private static string Subtitle(Glyphs glyphs, bool withTree, int columns)
    {
        var text = SubtitleText(glyphs);
        var shown = Math.Clamp(columns, 0, text.Length);
        var indent = withTree ? new string(' ', TreeWidth + Gap) : "";

        if (shown == 0) return indent;

        // The name half is the accent, the description half is muted — the same relationship the
        // wordmark and this line have to each other, one size down.
        var builder = new StringBuilder(indent);
        for (var i = 0; i < shown; i++)
        {
            var colour = shown < text.Length && i >= shown - 3
                ? Palette.Mix(Palette.Accent, Palette.Text, 0.8)
                : i < CliConsts.BinaryName.Length * 2
                    ? Palette.AccentSoft
                    : Palette.Muted;

            builder.Append('[').Append(Palette.Hex(colour)).Append(']')
                   .Append(Markup.Escape(text[i].ToString()))
                   .Append("[/]");
        }
        return builder.ToString();
    }

    // One row of the wordmark, assembled column by column because neighbouring letters can be at
    // different heights mid-wave. A source row outside the art is blank, which is what gives the
    // hopping letter its empty row underneath.
    private static string WordRow(int artRow, int hopMask)
    {
        if (hopMask == 0)
            return artRow >= 0 && artRow < Wordmark.Length ? Wordmark[artRow] : new string(' ', WordWidth);

        var row = new StringBuilder(WordWidth);
        for (var column = 0; column < WordWidth; column++)
        {
            var hopped = (hopMask >> (column / LetterWidth) & 1) == 1;
            var source = artRow + (hopped ? 1 : 0);
            row.Append(source >= 0 && source < Wordmark.Length ? Wordmark[source][column] : ' ');
        }
        return row.ToString();
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
