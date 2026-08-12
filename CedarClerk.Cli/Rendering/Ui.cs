using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Rendering;

// The two containers every screen is built from, in one place so that --no-unicode reaches all of
// them at once.
//
// Rule needs its own treatment: Spectre's version draws U+2500 whatever the profile says, so in
// ASCII mode it is replaced by a hand-made line rather than left to print a box character into
// cmd.exe.
public static class Ui
{
    public static IRenderable Rule(Glyphs glyphs, string markup, int width)
    {
        if (glyphs.IsUnicode)
            return new Rule(markup) { Justification = Justify.Left, Style = new Style(Palette.Faint) };

        var text = new Markup(markup);
        var used = Markup.Remove(markup).Length;
        var dashes = Math.Max(3, width - used - 2);
        return new Markup($"{markup} [grey35]{new string('-', dashes)}[/]");
    }

    public static IRenderable Panel(Glyphs glyphs, string title, IRenderable body, Color colour) =>
        new Panel(body)
        {
            Border = glyphs.Border,
            BorderStyle = new Style(Palette.Faint),
            Header = new PanelHeader($" [{Palette.Hex(colour)}]{title}[/] "),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };

    public static IRenderable Panel(Glyphs glyphs, string title, string markup, Color colour) =>
        Panel(glyphs, title, new Markup(markup), colour);
}
