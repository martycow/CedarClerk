using Spectre.Console;

namespace CedarClerk.Cli.Rendering;

public enum GraphStyle { Braille, Block, Ascii }

// One place that decides what this terminal can draw, and one set of glyphs per answer.
//
// Every status glyph is paired with a word or a number wherever it is used (ADR-118 decision 7):
// colour is never the only carrier of meaning, because monochrome terminals exist and so does
// red/green colour blindness.
public sealed class Glyphs
{
    public static Glyphs Unicode { get; } = new(
        ok: "✔", bad: "✘", warn: "▲", dot: "●", arrow: "→",
        full: "█", empty: "░", skip: "○", style: GraphStyle.Braille);

    public static Glyphs Plain { get; } = new(
        ok: "OK", bad: "XX", warn: "!!", dot: "*", arrow: "->",
        full: "#", empty: ".", skip: "-", style: GraphStyle.Ascii);

    private Glyphs(string ok, string bad, string warn, string dot, string arrow,
                   string full, string empty, string skip, GraphStyle style)
    {
        Ok = ok; Bad = bad; Warn = warn; Dot = dot; Arrow = arrow;
        Full = full; Empty = empty; Skip = skip; GraphStyle = style;
    }

    public string Ok { get; }
    public string Bad { get; }
    public string Warn { get; }
    public string Dot { get; }
    public string Arrow { get; }
    public string Full { get; }
    public string Empty { get; }
    public string Skip { get; }
    public GraphStyle GraphStyle { get; }

    public bool IsUnicode => GraphStyle != GraphStyle.Ascii;

    // Hundreds of these sit side by side in the test grid, which constrains the glyph three ways:
    // exactly one column in both modes (Ok/Bad are two characters in ASCII and sheared the grid);
    // not an emoji, because Windows hands U+2714 to Segoe UI Emoji, which ignores the ANSI colour —
    // that is why a wall of "green" ticks came out violet; and distinguishable by shape, not only
    // colour, so the grid survives monochrome.
    public string CellPending => IsUnicode ? "□" : ".";   // □ empty
    public string CellPassed => IsUnicode ? "▣" : "#";    // ▣ filled, border intact
    public string CellFailed => IsUnicode ? "⊠" : "X";    // ⊠ crossed
    public string CellSkipped => IsUnicode ? "⊡" : "-";   // ⊡ dotted

    // Typography leaks through too. Em dashes were swapped for hyphens in every user-facing string,
    // because U+2014 is in no DOS code page; the middle dot stayed, since it sits in both CP437 and
    // CP1252. The legend swatch does have to switch: U+25A0 is in neither.
    public string Swatch => IsUnicode ? "■" : "*";

    public BoxBorder Border => IsUnicode ? BoxBorder.Rounded : BoxBorder.Ascii;
    public TableBorder TableBorder => IsUnicode ? TableBorder.Rounded : TableBorder.Ascii;
    public Spinner Spinner => IsUnicode ? Spectre.Console.Spinner.Known.Dots : Spectre.Console.Spinner.Known.Line;

    // Automatic by capability, forced only by --no-unicode. A terminal that reports no Unicode and
    // gets braille anyway does not degrade gracefully — it prints question marks in a neat row.
    //
    // Choosing ASCII also switches the console profile itself, because our glyphs are only half the
    // output: Spectre draws its own panel and table borders from that flag, and leaving it alone
    // gave a screen of tidy ASCII content inside box-drawing frames.
    public static Glyphs For(IAnsiConsole console, bool forceAscii)
    {
        if (forceAscii) console.Profile.Capabilities.Unicode = false;
        return console.Profile.Capabilities.Unicode ? Unicode : Plain;
    }
}
