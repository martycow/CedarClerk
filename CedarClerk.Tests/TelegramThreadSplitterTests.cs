using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-106. What is tested is not "does it fit" — that part is arithmetic — but where the cuts land,
// because a message that ends mid-section reads like a transmission error and one that ends where
// a section ends reads like a chapter. The preview shown to the author is only trustworthy if
// these rules are.
public class TelegramThreadSplitterTests
{
    private static PublishCapabilities Caps(int chars = 1000, int media = 10) => new()
    {
        Network = PublishNetworks.Telegram,
        MaxCharacters = chars,
        MaxMediaItems = media,
    };

    private static CedarRichBlock Para(string text) => new RichParagraphBlock(new RichRunText(text));
    private static CedarRichBlock Para(int length) => Para(new string('x', length));
    private static CedarRichBlock Heading(string text) => new RichHeadingBlock(2, new RichRunText(text));
    private static CedarRichBlock Photo() => new RichPhotoBlock("https://example.com/a.jpg", null);

    [Fact]
    public void A_document_that_fits_is_one_part()
    {
        var parts = TelegramThreadSplitter.Split([Para("short")], Caps());

        var part = Assert.Single(parts);
        Assert.Equal(ThreadCutReason.End, part.CutReason);
    }

    [Fact]
    public void A_long_document_is_cut_on_size()
    {
        var parts = TelegramThreadSplitter.Split([Para(600), Para(600), Para(600)], Caps(chars: 1000));

        Assert.Equal(3, parts.Count);
        Assert.Equal(ThreadCutReason.Size, parts[0].CutReason);
        Assert.All(parts, p => Assert.True(p.Characters <= 1000));
    }

    // The rule that makes a thread readable: once a part is comfortably full, the next heading
    // ends it, rather than three paragraphs later at an arbitrary character.
    [Fact]
    public void A_heading_ends_a_part_that_is_already_comfortably_full()
    {
        var blocks = new[] { Heading("First"), Para(700), Heading("Second"), Para(100) };

        var parts = TelegramThreadSplitter.Split(blocks, Caps(chars: 1000));

        Assert.Equal(2, parts.Count);
        Assert.Equal(ThreadCutReason.Heading, parts[0].CutReason);
        Assert.Equal("Second", parts[1].StartsWith);
    }

    [Fact]
    public void A_heading_in_a_still_short_part_is_just_the_next_section()
    {
        // 200 of a 1000 budget: cutting here would produce a two-line message for no reason.
        var blocks = new[] { Heading("First"), Para(200), Heading("Second"), Para(200) };

        Assert.Single(TelegramThreadSplitter.Split(blocks, Caps(chars: 1000)));
    }

    [Fact]
    public void Media_cuts_a_part_even_when_the_text_would_fit()
    {
        var blocks = Enumerable.Range(0, 25).Select(_ => Photo()).ToList();

        var parts = TelegramThreadSplitter.Split(blocks, Caps(chars: 100_000, media: 10));

        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.True(p.MediaCount <= 10));
        Assert.Equal(ThreadCutReason.Media, parts[0].CutReason);
    }

    [Fact]
    public void A_slideshow_counts_as_all_of_its_images()
    {
        var blocks = new CedarRichBlock[]
        {
            new RichSlideshowBlock(["a", "b", "c", "d", "e", "f"]),
            new RichSlideshowBlock(["g", "h", "i", "j", "k", "l"]),
        };

        var parts = TelegramThreadSplitter.Split(blocks, Caps(media: 10));

        // Twelve images across two groups cannot share a message that takes ten.
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void A_block_bigger_than_the_whole_budget_still_ships()
    {
        // Refusing here would leave the author with a document that cannot be published and no
        // explanation — the network's own refusal is a better answer than ours.
        var parts = TelegramThreadSplitter.Split([Para(5000)], Caps(chars: 1000));

        Assert.Single(parts);
        Assert.Equal(5000, parts[0].Characters);
    }

    [Fact]
    public void Every_part_is_labelled_by_what_it_opens_with()
    {
        var blocks = new[] { Heading("Introduction"), Para(700), Heading("Mechanics"), Para(700) };

        var parts = TelegramThreadSplitter.Split(blocks, Caps(chars: 1000));

        Assert.Equal("Introduction", parts[0].StartsWith);
        Assert.Equal("Mechanics", parts[1].StartsWith);
    }

    [Fact]
    public void No_part_is_ever_empty()
    {
        var parts = TelegramThreadSplitter.Split(
            [Para(900), Photo(), Para(900), Photo()], Caps(chars: 1000, media: 1));

        Assert.All(parts, p => Assert.NotEmpty(p.Blocks));
    }
}
