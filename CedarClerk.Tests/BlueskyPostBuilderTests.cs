using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-089. Two things here are easy to get wrong and impossible to notice afterwards: a facet
// measured in characters instead of UTF-8 bytes (the link ends up covering the wrong text, and on
// a Russian post it always will), and a truncation that cuts a codepoint in half.
public class BlueskyPostBuilderTests
{
    private static string Doc(params string[] paragraphs) =>
        "{\"type\":\"doc\",\"content\":[" + string.Join(",", paragraphs.Select(p =>
            $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{p}\"}}]}}")) + "]}";

    [Fact]
    public void The_authors_own_text_wins_over_the_teaser()
    {
        var post = BlueskyPostBuilder.Build("My own wording.", Doc("The article opens like this."), null);

        Assert.Equal("My own wording.", post.Text);
    }

    [Fact]
    public void Without_an_override_the_opening_paragraphs_become_the_post()
    {
        var post = BlueskyPostBuilder.Build(null, Doc("First paragraph.", "Second paragraph."), null);

        Assert.Equal("First paragraph.\n\nSecond paragraph.", post.Text);
    }

    [Fact]
    public void A_paragraph_that_would_overflow_is_left_out_whole()
    {
        var doc = Doc("Short one.", new string('x', 400));

        var post = BlueskyPostBuilder.Build(null, doc, null);

        Assert.Equal("Short one.", post.Text);
    }

    [Fact]
    public void Nothing_fitting_whole_still_produces_a_post()
    {
        var post = BlueskyPostBuilder.Build(null, Doc(new string('x', 400)), null);

        Assert.Equal(BlueskyPostBuilder.MaxGraphemes, BlueskyPostBuilder.GraphemeCount(post.Text));
        Assert.EndsWith("…", post.Text);
    }

    [Fact]
    public void The_link_is_kept_and_the_body_is_trimmed_around_it()
    {
        const string url = "https://blog.mooexe.dev/my-post";

        var post = BlueskyPostBuilder.Build(new string('x', 400), "{}", url);

        Assert.EndsWith(url, post.Text);
        Assert.True(BlueskyPostBuilder.GraphemeCount(post.Text) <= BlueskyPostBuilder.MaxGraphemes);
    }

    // The one that cannot be eyeballed: on Cyrillic every character is two UTF-8 bytes, so a facet
    // computed from string indices points at the wrong span — and the wrong span is still valid
    // JSON, so nothing complains, the link just covers the wrong words.
    [Fact]
    public void Facet_offsets_are_utf8_bytes_not_characters()
    {
        const string url = "https://blog.mooexe.dev/post";
        var post = BlueskyPostBuilder.Build("Привет, это пост про кедры", "{}", url);

        var facet = Assert.Single(post.Facets);
        var bytes = Encoding.UTF8.GetBytes(post.Text);
        Assert.Equal(url, Encoding.UTF8.GetString(bytes, facet.ByteStart, facet.ByteEnd - facet.ByteStart));
        // Proof the test is worth having: the character index would have been a different number.
        Assert.NotEqual(post.Text.IndexOf(url, StringComparison.Ordinal), facet.ByteStart);
    }

    [Fact]
    public void Facet_offsets_are_right_for_plain_ascii_too()
    {
        const string url = "https://blog.mooexe.dev/post";
        var post = BlueskyPostBuilder.Build("Plain english text", "{}", url);

        var facet = Assert.Single(post.Facets);
        var bytes = Encoding.UTF8.GetBytes(post.Text);
        Assert.Equal(url, Encoding.UTF8.GetString(bytes, facet.ByteStart, facet.ByteEnd - facet.ByteStart));
    }

    [Fact]
    public void Truncation_never_splits_a_grapheme()
    {
        // Family emoji: several codepoints joined into one grapheme. Cutting inside it produces
        // a different emoji, or a broken one.
        var text = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 20));

        var truncated = BlueskyPostBuilder.Truncate(text, 5);

        Assert.Equal(5, BlueskyPostBuilder.GraphemeCount(truncated));
        Assert.DoesNotContain('�', truncated);
    }

    [Fact]
    public void An_empty_document_produces_an_empty_teaser_rather_than_throwing()
    {
        Assert.Equal("", BlueskyPostBuilder.Teaser("{}"));
        Assert.Equal("", BlueskyPostBuilder.Teaser("not json at all"));
    }
}
