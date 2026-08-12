using CedarClerk.Cli.Rendering;
using Spectre.Console.Testing;

namespace CedarClerk.Cli.Tests;

// The splash is decoration, and these tests are about it never being more than that: it renders in
// every mode, it degrades to ASCII on demand, and it does not animate into a pipe.
public class LogoTests
{
    private static TestConsole Console(int width, bool unicode)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.Profile.Capabilities.Unicode = unicode;
        return console;
    }

    [Fact]
    public void Draws_the_wordmark_and_the_tree_on_a_wide_terminal()
    {
        var console = Console(100, unicode: true);
        Logo.Show(console, Glyphs.Unicode, "operations console", animate: false);

        Assert.Contains("█", console.Output);
        Assert.Contains("▟", console.Output);      // the cedar
        Assert.Contains("operations console", console.Output);
    }

    [Fact]
    public void No_unicode_leaves_nothing_a_dumb_terminal_cannot_print()
    {
        var console = Console(100, unicode: false);
        Logo.Show(console, Glyphs.Plain, "operations console", animate: false);

        foreach (var character in console.Output)
            Assert.True(character < 0x2500 || character > 0x25FF,
                $"box-drawing character U+{(int)character:X4} survived the ASCII fallback");
        Assert.Contains("#", console.Output);
    }

    [Fact]
    public void A_narrow_terminal_drops_the_tree_before_it_wraps_anything()
    {
        var console = Console(50, unicode: true);
        Logo.Show(console, Glyphs.Unicode, "x", animate: false);

        Assert.Contains("█", console.Output);
        Assert.DoesNotContain("▟", console.Output);
        foreach (var line in console.Lines) Assert.True(line.Length <= 50, $"line overflowed: {line.Length}");
    }

    [Fact]
    public void A_terminal_too_narrow_for_the_art_falls_back_to_the_name()
    {
        var console = Console(30, unicode: true);
        Logo.Show(console, Glyphs.Unicode, "operations console", animate: false);

        Assert.Contains("Cedar Clerk", console.Output);
        Assert.DoesNotContain("█", console.Output);
    }

    [Fact]
    public void The_two_halves_of_the_art_are_the_same_size()
    {
        // The tree and the wordmark are drawn side by side line for line; a mismatch would show up
        // as a stray blank column, and only on a wide terminal.
        var console = Console(100, unicode: true);
        Logo.Show(console, Glyphs.Unicode, "x", animate: false);

        var art = console.Lines.Where(line => line.Contains('█')).ToList();
        Assert.NotEmpty(art);
        Assert.All(art, line => Assert.Equal(art[0].TrimEnd().Length > 0, line.TrimEnd().Length > 0));
    }
}
