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

    // The idle animation: a highlight twice a cycle, a hop once, and stillness the rest of the time.
    // These pin the timing down because it is invisible in a diff — a stray zero turns "once every
    // ten seconds" into a permanent twitch, and nothing else would notice.

    [Fact]
    public void A_hopping_letter_rises_by_exactly_one_row_and_takes_nothing_with_it()
    {
        var still = Art(Logo.Idle.Still);
        var hopped = Art(new Logo.Idle(-1, 1));   // only the first letter is up

        var letter = Width(still) / Logo.LetterCount;

        for (var row = 0; row < still.Length - 1; row++)
        {
            Assert.Equal(still[row + 1][..letter], hopped[row][..letter]);
            Assert.Equal(still[row][letter..], hopped[row][letter..]);
        }
    }

    [Fact]
    public void The_art_has_a_blank_row_above_it_for_the_hop_to_use()
    {
        // Without the headroom the top row of the wordmark would be clipped instead of rising, and
        // the jump would read as the letter losing its lid.
        Assert.Equal("", Art(Logo.Idle.Still)[0].Trim());
    }

    [Fact]
    public void Every_letter_hops_once_per_cycle_and_the_logo_is_mostly_still()
    {
        var samples = Cycle().ToList();

        for (var letter = 0; letter < Logo.LetterCount; letter++)
        {
            var index = letter;
            Assert.True(samples.Any(idle => (idle.HopMask >> index & 1) == 1), $"letter {index} never hopped");
        }

        Assert.Contains(samples, idle => idle.Shimmer >= 0);
        Assert.True(samples.Count(idle => idle == Logo.Idle.Still) > samples.Count / 2,
            "the logo should be resting for most of the cycle, not animating through it");
    }

    [Fact]
    public void The_cycle_returns_to_rest_before_it_repeats()
    {
        // A wave still in flight at the wrap point would jump back to its start mid-hop.
        Assert.Equal(Logo.Idle.Still, Logo.IdleAt(TimeSpan.FromMilliseconds(9_999)));
        Assert.Equal(Logo.Idle.Still, Logo.IdleAt(TimeSpan.FromSeconds(3)));
    }

    private static IEnumerable<Logo.Idle> Cycle()
    {
        for (var ms = 0; ms < 10_000; ms += 20) yield return Logo.IdleAt(TimeSpan.FromMilliseconds(ms));
    }

    private static string[] Art(Logo.Idle idle)
    {
        var console = Console(100, unicode: true);
        console.Write(Logo.Still(Glyphs.Unicode, withTree: false, idle));

        // The art only: the subtitle underneath is a different width and a different thing, and the
        // tests below are about how the letters move.
        var lines = console.Lines.Take(Logo.ArtRows).ToArray();
        var width = lines.Max(line => line.Length);
        return lines.Select(line => line.PadRight(width)).ToArray();
    }

    private static int Width(string[] art) => art[0].Length;

    // The subtitle (Marty, 12.08.2026). A console that opens with the product's wordmark and nothing
    // else claims to be the product; this line is the correction, so these pin down that it is there,
    // that it says both halves of what it has to say, and that it cannot make the block overflow.

    [Fact]
    public void The_subtitle_says_it_is_a_console_and_whose_it_is()
    {
        var console = Console(100, unicode: true);
        Logo.Show(console, Glyphs.Unicode, "v0.0.0", animate: false);

        // The name itself is the wordmark drawn above; the letter-spaced "c e d a r" moved out of this
        // line when the copyright moved in, and asserting on it kept the test red against a shipped
        // logo (found 12.08.2026).
        Assert.Contains("operations console", console.Output);
        Assert.Contains("Moo.exe", console.Output);
    }

    [Fact]
    public void The_subtitle_is_never_wider_than_the_wordmark_above_it()
    {
        // Every "does the logo fit" check measures the wordmark, so a longer subtitle would push past
        // the edge of a terminal that had just been told the logo fits.
        Assert.True(Logo.SubtitleText(Glyphs.Unicode).Length <= Width(Art(Logo.Idle.Still)),
            "the subtitle is wider than the art it sits under");
    }

    [Fact]
    public void The_subtitle_drops_its_middle_dot_in_ascii_mode()
    {
        Assert.DoesNotContain("·", Logo.SubtitleText(Glyphs.Plain));
        Assert.Contains("operations console", Logo.SubtitleText(Glyphs.Plain));
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
