using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-381 / ADR-299. LinkedIn has no markup, so "the layout survives" is a set of plain-text rules,
// each pinned here: blank lines between blocks, markers for lists, Unicode bold for headings, and
// the little-text escaping without which a post with a bracket is refused.
public class LinkedInPostBuilderTests
{
    private static string Doc(params string[] nodes) => "{\"type\":\"doc\",\"content\":[" + string.Join(",", nodes) + "]}";
    private static string Para(string text) => $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{text}\"}}]}}";
    private static string Heading(string text) => $"{{\"type\":\"heading\",\"attrs\":{{\"level\":2}},\"content\":[{{\"type\":\"text\",\"text\":\"{text}\"}}]}}";
    private static string Item(string text) => $"{{\"type\":\"listItem\",\"content\":[{Para(text)}]}}";

    [Fact]
    public void The_authors_own_text_wins_over_the_document()
    {
        var post = LinkedInPostBuilder.Build("My own wording.", Doc(Para("The article opens like this.")), null);

        Assert.Equal("My own wording.", post.Text);
    }

    [Fact]
    public void Without_an_override_the_whole_document_goes_out_paragraph_by_paragraph()
    {
        var post = LinkedInPostBuilder.Build(null, Doc(Para("First."), Para("Second."), Para("Third.")), null);

        Assert.Equal("First.\n\nSecond.\n\nThird.", post.Text);
    }

    [Fact]
    public void A_heading_becomes_a_bold_line_in_the_mathematical_sans_face()
    {
        var text = LinkedInPostBuilder.Render(Doc(Heading("Devlog 14"), Para("Body.")));

        Assert.Equal("𝗗𝗲𝘃𝗹𝗼𝗴 𝟭𝟰\n\nBody.", text);
    }

    [Fact]
    public void Bold_and_italic_marks_map_to_their_faces_and_the_styling_can_be_switched_off()
    {
        var doc = Doc("""{"type":"paragraph","content":[{"type":"text","text":"a "},{"type":"text","marks":[{"type":"bold"}],"text":"bold"},{"type":"text","text":" and "},{"type":"text","marks":[{"type":"italic"}],"text":"soft"},{"type":"text","text":" word"}]}""");

        Assert.Equal("a 𝗯𝗼𝗹𝗱 and 𝘴𝘰𝘧𝘵 word", LinkedInPostBuilder.Render(doc));
        Assert.Equal("a bold and soft word", LinkedInPostBuilder.Render(doc, styled: false));
    }

    // The block has Latin letters and digits only — a Russian heading must come out readable,
    // not as a broken half-styled mix.
    [Fact]
    public void Cyrillic_emphasis_degrades_to_plain_text()
    {
        Assert.Equal("Новая сборка 𝟯", UnicodeStyle.Apply("Новая сборка 3", bold: true, italic: false));
    }

    [Fact]
    public void Lists_get_markers_numbers_and_checkboxes()
    {
        var doc = Doc(
            $"{{\"type\":\"bulletList\",\"content\":[{Item("one")},{Item("two")}]}}",
            $"{{\"type\":\"orderedList\",\"attrs\":{{\"start\":3}},\"content\":[{Item("three")},{Item("four")}]}}",
            $"{{\"type\":\"taskList\",\"content\":[{{\"type\":\"taskItem\",\"attrs\":{{\"checked\":true}},\"content\":[{Para("done")}]}},{{\"type\":\"taskItem\",\"attrs\":{{\"checked\":false}},\"content\":[{Para("open")}]}}]}}");

        Assert.Equal("• one\n• two\n\n3. three\n4. four\n\n☑ done\n☐ open", LinkedInPostBuilder.Render(doc));
    }

    [Fact]
    public void A_nested_list_is_indented_under_its_item()
    {
        var nested = $"{{\"type\":\"listItem\",\"content\":[{Para("outer")},{{\"type\":\"bulletList\",\"content\":[{Item("inner")}]}}]}}";

        Assert.Equal("• outer\n    • inner", LinkedInPostBuilder.Render(Doc($"{{\"type\":\"bulletList\",\"content\":[{nested}]}}")));
    }

    [Fact]
    public void A_quote_is_wrapped_in_curly_quotes_and_a_rule_becomes_a_dash_line()
    {
        var doc = Doc($"{{\"type\":\"blockquote\",\"content\":[{Para("Ship it.")}]}}", "{\"type\":\"horizontalRule\"}", Para("After."));

        Assert.Equal("“Ship it.”\n\n———\n\nAfter.", LinkedInPostBuilder.Render(doc));
    }

    [Fact]
    public void A_link_carries_its_address_unless_the_label_already_is_one()
    {
        var doc = Doc(
            """{"type":"paragraph","content":[{"type":"text","marks":[{"type":"link","attrs":{"href":"https://mooexe.dev/p"}}],"text":"the page"}]}""",
            """{"type":"paragraph","content":[{"type":"text","marks":[{"type":"link","attrs":{"href":"https://mooexe.dev/p"}}],"text":"https://mooexe.dev/p"}]}""");

        Assert.Equal("the page (https://mooexe.dev/p)\n\nhttps://mooexe.dev/p", LinkedInPostBuilder.Render(doc));
    }

    [Fact]
    public void Pictures_leave_no_text_behind_and_a_youtube_node_becomes_its_link()
    {
        var doc = Doc(Para("Look"), "{\"type\":\"image\",\"attrs\":{\"src\":\"/media/a.png\"}}",
            "{\"type\":\"youtube\",\"attrs\":{\"videoId\":\"dQw4w9WgXcQ\",\"caption\":\"Trailer\"}}");

        Assert.Equal("Look\n\nTrailer https://www.youtube.com/watch?v=dQw4w9WgXcQ", LinkedInPostBuilder.Render(doc));
    }

    [Fact]
    public void The_blog_link_survives_truncation_and_the_cut_lands_on_a_block()
    {
        var url = "https://blog.example/post";
        var post = LinkedInPostBuilder.Build(null, Doc(Para(new string('a', 1500)), Para(new string('b', 1500)), Para("tail")), url);

        Assert.EndsWith("\n\n…\n\n" + url, post.Text);
        Assert.DoesNotContain("bbb", post.Text);
        Assert.True(post.Length <= LinkedInPostBuilder.MaxChars);
    }

    [Fact]
    public void A_first_block_bigger_than_the_whole_budget_is_cut_on_a_grapheme()
    {
        var text = LinkedInPostBuilder.Fit(new string('x', 40) + "👨‍👩‍👧", 30);

        Assert.EndsWith("…", text);
        Assert.True(LinkedInPostBuilder.Measure(text) <= 30);
    }

    [Fact]
    public void Text_that_fits_is_never_touched()
    {
        Assert.Equal("short", LinkedInPostBuilder.Fit("short", 10));
    }

    // LinkedIn's "little" grammar: every reserved character means something unless escaped, so
    // a post with a parenthesis or an underscore is refused — or silently parsed — without this.
    [Fact]
    public void Reserved_characters_are_escaped_and_hashtags_are_not()
    {
        Assert.Equal(@"v1.2 \(beta\) \[x\] snake\_case #5 #devlog \@you \{t\} a\|b \<i\> \*s\* \~z\~ \\",
            LinkedInLittleText.Escape("v1.2 (beta) [x] snake_case #5 #devlog @you {t} a|b <i> *s* ~z~ \\"));
        Assert.Equal("#devlog", LinkedInLittleText.Escape("#devlog"));
        Assert.Equal(@"\#", LinkedInLittleText.Escape("#"));
    }

    [Fact]
    public void The_length_counts_the_escaped_wire_text()
    {
        Assert.Equal(6, LinkedInPostBuilder.Measure("(a)b"));
    }

    [Fact]
    public void Footnotes_are_numbered_inline_and_listed_at_the_end()
    {
        var doc = Doc("""{"type":"paragraph","content":[{"type":"text","text":"Claim"},{"type":"footnote","attrs":{"text":"Source A"}}]}""");

        Assert.Equal("Claim[1]\n\n1. Source A", LinkedInPostBuilder.Render(doc));
    }

    [Fact]
    public void Malformed_json_renders_to_nothing_rather_than_throwing()
    {
        Assert.Equal("", LinkedInPostBuilder.Render("{not json"));
        Assert.Equal("", LinkedInPostBuilder.Build(null, "{not json", null).Text);
    }
}
