using CedarClerk.Core;

namespace CedarClerk.Tests;

// Idea #11. The scanner runs on already-escaped text and injects markup into it, which is exactly
// the position renderers.md's first invariant is about — these pin both the matching rules and
// the escaping.
public class GlossaryScannerTests
{
    private static GlossaryEntry Entry(string term, string desc = "A description", string? img = null, params string[] aliases) =>
        new(term, desc, img, aliases);

    private static string Mark(string text, params GlossaryEntry[] entries) =>
        GlossaryScanner.Mark(text, entries, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Marks_a_term_it_finds()
    {
        var html = Mark("We use Unity here.", Entry("Unity"));
        Assert.Contains("<span class=\"glossary-term\"", html);
        Assert.Contains(">Unity</span>", html);
        Assert.Contains("data-desc=\"A description\"", html);
    }

    [Fact]
    public void Leaves_text_alone_when_nothing_matches()
    {
        Assert.Equal("Nothing to see.", Mark("Nothing to see.", Entry("Unity")));
    }

    [Fact]
    public void Matches_case_insensitively_but_keeps_the_original_spelling()
    {
        var html = Mark("we use UNITY here", Entry("Unity"));
        Assert.Contains(">UNITY</span>", html);
        Assert.Contains("data-term=\"Unity\"", html);
    }

    [Fact]
    public void Does_not_match_inside_a_longer_word()
    {
        Assert.DoesNotContain("glossary-term", Mark("These are articles.", Entry("art")));
    }

    [Fact]
    public void Only_the_first_occurrence_is_marked()
    {
        var html = Mark("Unity and Unity and Unity", Entry("Unity"));
        Assert.Equal(1, CountOccurrences(html, "glossary-term"));
    }

    [Fact]
    public void The_first_occurrence_rule_spans_several_calls()
    {
        // One page renders through many text nodes; the shared set is what makes "first
        // occurrence on the page" mean the page and not the paragraph.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new[] { Entry("Unity") };
        var first = GlossaryScanner.Mark("Unity is here", entries, seen);
        var second = GlossaryScanner.Mark("Unity again", entries, seen);
        Assert.Contains("glossary-term", first);
        Assert.DoesNotContain("glossary-term", second);
    }

    [Fact]
    public void Aliases_match_the_same_entry()
    {
        var html = Mark("про рендерера речь", Entry("рендерер", "Описание", null, "рендерера", "рендереру"));
        Assert.Contains(">рендерера</span>", html);
        Assert.Contains("data-term=\"рендерер\"", html);
    }

    [Fact]
    public void The_longest_candidate_wins()
    {
        var html = Mark("the Unity engine is here", Entry("Unity"), Entry("Unity engine"));
        Assert.Contains(">Unity engine</span>", html);
    }

    [Fact]
    public void A_description_cannot_break_out_of_the_attribute()
    {
        var html = Mark("Unity", Entry("Unity", "a \"quote\" & <b>bold</b>"));
        Assert.Contains("data-desc=\"a &quot;quote&quot; &amp; &lt;b&gt;bold&lt;/b&gt;\"", html);
        Assert.DoesNotContain("<b>bold</b>", html);
    }

    [Fact]
    public void Html_entities_in_the_text_survive_untouched()
    {
        // The input is already escaped, so "&amp;" is one character to the reader — the matcher
        // must not walk into it and mark "amp", which would also split the entity.
        var html = Mark("Tom &amp; Unity", Entry("amp"), Entry("Unity"));
        Assert.Contains("Tom &amp; ", html);
        Assert.Contains(">Unity</span>", html);
        Assert.DoesNotContain(">amp</span>", html);
    }

    [Fact]
    public void An_image_is_carried_when_there_is_one()
    {
        var html = Mark("Unity", Entry("Unity", "d", "/media/x.png"));
        Assert.Contains("data-img=\"/media/x.png\"", html);
    }

    [Fact]
    public void No_image_attribute_when_there_is_no_image()
    {
        Assert.DoesNotContain("data-img", Mark("Unity", Entry("Unity")));
    }

    [Fact]
    public void An_empty_glossary_changes_nothing()
    {
        Assert.Equal("Unity", GlossaryScanner.Mark("Unity", [], []));
    }

    [Fact]
    public void Blank_aliases_are_ignored_rather_than_matching_everywhere()
    {
        var html = Mark("Some text", Entry("Unity", "d", null, "", "   "));
        Assert.DoesNotContain("glossary-term", html);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            count++;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }
}

// The renderer half: a term must not be marked where marking it would be wrong.
public class GlossaryRendererTests
{
    private static readonly GlossaryEntry[] Glossary = [new("Unity", "A game engine", null, [])];

    private static string Render(string cedarJson) =>
        CedarToBlogHtmlRenderer.Render(cedarJson, "https://blog.test", "ru", Glossary);

    [Fact]
    public void Marks_a_term_in_a_paragraph()
    {
        var html = Render("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"We use Unity."}]}]}""");
        Assert.Contains("glossary-term", html);
    }

    [Fact]
    public void Does_not_mark_inside_a_code_block()
    {
        var html = Render("""{"type":"doc","content":[{"type":"codeBlock","content":[{"type":"text","text":"Unity.Run()"}]}]}""");
        Assert.DoesNotContain("glossary-term", html);
    }

    [Fact]
    public void Does_not_mark_inside_inline_code()
    {
        var html = Render("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Unity","marks":[{"type":"code"}]}]}]}""");
        Assert.DoesNotContain("glossary-term", html);
    }

    [Fact]
    public void Does_not_mark_inside_a_link()
    {
        var html = Render("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Unity","marks":[{"type":"link","attrs":{"href":"https://x.test"}}]}]}]}""");
        Assert.DoesNotContain("glossary-term", html);
        Assert.Contains("<a href=\"https://x.test\"", html);
    }

    [Fact]
    public void Marks_nothing_when_no_glossary_is_passed()
    {
        var html = CedarToBlogHtmlRenderer.Render(
            """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"We use Unity."}]}]}""",
            "https://blog.test");
        Assert.DoesNotContain("glossary-term", html);
    }
}
