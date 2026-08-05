using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-110. The counting is the whole risk here: X measures weighted units, not characters — a
// naive string.Length would refuse valid posts with links (every URL is 23, not its length) and
// build invalid CJK ones (two units per character). Each rule is pinned separately.
public class XPostBuilderTests
{
    private static string Doc(params string[] paragraphs) =>
        "{\"type\":\"doc\",\"content\":[" + string.Join(",", paragraphs.Select(p =>
            $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{p}\"}}]}}")) + "]}";

    [Fact]
    public void The_authors_own_text_wins_over_the_teaser()
    {
        var post = XPostBuilder.Build("My own wording.", Doc("The article opens like this."), null);

        Assert.Equal("My own wording.", post.Text);
    }

    [Fact]
    public void Without_an_override_the_opening_paragraphs_become_the_post()
    {
        var post = XPostBuilder.Build(null, Doc("First paragraph.", "Second paragraph."), null);

        Assert.Equal("First paragraph.\n\nSecond paragraph.", post.Text);
    }

    [Fact]
    public void A_paragraph_that_would_overflow_is_left_out_whole()
    {
        var post = XPostBuilder.Build(null, Doc("Short one.", new string('x', 400)), null);

        Assert.Equal("Short one.", post.Text);
    }

    // Cyrillic sits inside twitter-text's weight-1 ranges — a Russian post is NOT half length,
    // which is exactly the mistake "CJK counts double" invites.
    [Fact]
    public void Russian_weighs_one_per_character()
    {
        var text = new string('я', XPostBuilder.MaxWeightedChars);

        Assert.Equal(XPostBuilder.MaxWeightedChars, XPostBuilder.WeightedLength(text));
        Assert.Equal(text, XPostBuilder.Build(text, "{}", null).Text);
    }

    [Fact]
    public void Cjk_weighs_two_per_character()
    {
        Assert.Equal(280, XPostBuilder.WeightedLength(new string('漢', 140)));

        var over = XPostBuilder.Build(new string('漢', 141), "{}", null);
        Assert.True(over.WeightedLength <= XPostBuilder.MaxWeightedChars);
        Assert.EndsWith("…", over.Text);
    }

    [Fact]
    public void A_url_counts_as_23_no_matter_how_long_it_is()
    {
        var longUrl = "https://blog.mooexe.dev/a-very-long-slug-" + new string('x', 100);

        Assert.Equal(XPostBuilder.UrlWeight, XPostBuilder.WeightedLength(longUrl));
        Assert.Equal(XPostBuilder.UrlWeight + 6, XPostBuilder.WeightedLength($"look: {longUrl}"));
    }

    [Fact]
    public void The_link_is_kept_and_the_body_is_trimmed_around_it()
    {
        const string url = "https://blog.mooexe.dev/my-post";

        var post = XPostBuilder.Build(new string('x', 400), "{}", url);

        Assert.EndsWith(url, post.Text);
        Assert.True(post.WeightedLength <= XPostBuilder.MaxWeightedChars);
    }

    [Fact]
    public void A_body_that_fits_keeps_the_link_without_truncation()
    {
        const string url = "https://blog.mooexe.dev/my-post";

        var post = XPostBuilder.Build("Short body.", "{}", url);

        Assert.Equal("Short body.\n" + url, post.Text);
        Assert.Equal(XPostBuilder.WeightedLength("Short body.\n") + XPostBuilder.UrlWeight, post.WeightedLength);
    }

    // A ZWJ family sequence is seven codepoints and eleven UTF-16 units; X charges it as one
    // emoji of weight 2.
    [Fact]
    public void An_emoji_sequence_is_one_element_of_weight_two()
    {
        Assert.Equal(2, XPostBuilder.WeightedLength("👨‍👩‍👧‍👦"));
        Assert.Equal(4, XPostBuilder.WeightedLength("😀😀"));
    }

    [Fact]
    public void Truncation_never_splits_a_grapheme()
    {
        var text = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 200));

        var truncated = XPostBuilder.TruncateToWeight(text, 11);

        // 11 = ellipsis (2) + four families (2 each) with one unit left over that no element fits.
        Assert.Equal("👨‍👩‍👧‍👦👨‍👩‍👧‍👦👨‍👩‍👧‍👦👨‍👩‍👧‍👦…", truncated);
        Assert.DoesNotContain('�', truncated);
    }

    [Fact]
    public void An_empty_document_produces_an_empty_teaser_rather_than_throwing()
    {
        Assert.Equal("", XPostBuilder.Teaser("{}"));
        Assert.Equal("", XPostBuilder.Teaser("not json at all"));
    }
}
