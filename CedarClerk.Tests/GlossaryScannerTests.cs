using CedarClerk.Core;

namespace CedarClerk.Tests;

// Idea #11. The scanner runs on already-escaped text and injects markup into it, which is exactly
// the position renderers.md's first invariant is about — these pin both the matching rules and
// the escaping.
public class GlossaryScannerTests
{
    private static GlossaryMatchTerm Entry(string term, string desc = "A description", string? img = null, params string[] aliases) =>
        new(term, desc, img, aliases);

    private static string Mark(string text, params GlossaryMatchTerm[] entries) =>
        GlossaryScanner.Mark(text, entries);

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
    public void Every_occurrence_is_marked()
    {
        var html = Mark("Unity and Unity and Unity", Entry("Unity"));
        Assert.Equal(3, CountOccurrences(html, "glossary-term"));
    }

    [Fact]
    public void Every_text_node_marks_the_term_again()
    {
        // One page renders through many text nodes; the shared set is what makes "first
        // occurrence on the page" mean the page and not the paragraph.
        var entries = new[] { Entry("Unity") };
        var first = GlossaryScanner.Mark("Unity is here", entries);
        var second = GlossaryScanner.Mark("Unity again", entries);
        Assert.Contains("glossary-term", first);
        Assert.Contains("glossary-term", second);
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
        Assert.Equal("Unity", GlossaryScanner.Mark("Unity", []));
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

    // Marty, 01.08.2026: a per-term "case sensitive" switch. Off by default, because a term at the
    // start of a sentence is the same term — which is what the existing tests above assert. On,
    // the casing IS the meaning: "IT" the industry against "it" the pronoun.
    private static GlossaryMatchTerm CaseSensitive(string term, params string[] aliases) =>
        new(term, "A description", null, aliases, IsCaseSensitive: true);

    [Fact]
    public void A_case_sensitive_term_ignores_a_differently_cased_match()
    {
        Assert.DoesNotContain("glossary-term", Mark("we use unity here", CaseSensitive("Unity")));
    }

    [Fact]
    public void A_case_sensitive_term_still_matches_its_exact_spelling()
    {
        Assert.Contains("glossary-term", Mark("we use Unity here", CaseSensitive("Unity")));
    }

    [Fact]
    public void Case_sensitivity_applies_to_aliases_too()
    {
        var entry = CaseSensitive("IT", "ИТ");
        Assert.Contains("glossary-term", Mark("работа в ИТ сегодня", entry));
        Assert.DoesNotContain("glossary-term", Mark("работа в ит сегодня", entry));
    }

    [Fact]
    public void Case_sensitivity_is_per_term_not_global()
    {
        // The pronoun must not be marked, the product name must — on the same page.
        var html = Mark("it is built with unity", CaseSensitive("IT"), Entry("Unity"));
        Assert.Contains("data-term=\"Unity\"", html);
        Assert.DoesNotContain("data-term=\"IT\"", html);
    }
}

// ADR-238. The counting half shares the matcher with Mark but runs on plain text, and its result is
// positional — a dictionary keyed by term would collide, since the same word is a separate term in
// each language and each project.
public class GlossaryCountHitsTests
{
    private static GlossaryMatchTerm Entry(string term, params string[] aliases) =>
        new(term, "A description", null, aliases);

    private static int[] Count(string text, params GlossaryMatchTerm[] entries) =>
        GlossaryScanner.CountHits(text, entries);

    [Fact]
    public void Counts_every_occurrence()
    {
        Assert.Equal([3], Count("Unity and Unity and Unity", Entry("Unity")));
    }

    [Fact]
    public void A_term_that_is_absent_counts_zero()
    {
        Assert.Equal([0], Count("Nothing to see.", Entry("Unity")));
    }

    [Fact]
    public void The_result_is_positional_even_when_two_terms_are_the_same_word()
    {
        // The caller's only handle on a term is where it sat in the list it passed in.
        var counts = Count("Unity", Entry("Godot"), Entry("Unity"), Entry("Blender"));
        Assert.Equal([0, 1, 0], counts);
    }

    [Fact]
    public void An_empty_glossary_yields_an_empty_array()
    {
        Assert.Empty(GlossaryScanner.CountHits("Unity", []));
    }

    [Fact]
    public void Empty_text_counts_nothing()
    {
        Assert.Equal([0], Count("", Entry("Unity")));
    }

    [Fact]
    public void Matching_is_case_insensitive_by_default()
    {
        Assert.Equal([2], Count("UNITY and unity", Entry("Unity")));
    }

    [Fact]
    public void A_case_sensitive_term_counts_only_its_own_spelling()
    {
        var it = new GlossaryMatchTerm("IT", "d", null, [], IsCaseSensitive: true);
        Assert.Equal([1], GlossaryScanner.CountHits("IT is not it", [it]));
    }

    [Fact]
    public void A_term_inside_a_longer_word_is_not_a_hit()
    {
        Assert.Equal([0], Count("These are articles.", Entry("art")));
    }

    [Fact]
    public void Aliases_count_towards_their_own_entry()
    {
        Assert.Equal([2], Count("рендерер и рендерера", Entry("рендерер", "рендерера")));
    }

    [Fact]
    public void The_longest_candidate_wins_and_the_shorter_one_is_not_counted_twice()
    {
        // "Unity engine" is one hit for the longer term, not one for each.
        var counts = Count("the Unity engine", Entry("Unity"), Entry("Unity engine"));
        Assert.Equal([0, 1], counts);
    }

    [Fact]
    public void An_ampersand_entity_is_five_letters_here()
    {
        // Mark skips entities because it runs on escaped HTML; this runs on the document's own
        // text, where "&amp;" is what the writer typed.
        Assert.Equal([1], Count("Tom &amp; Jerry", Entry("amp")));
    }

    [Fact]
    public void Blank_aliases_do_not_match_everywhere()
    {
        Assert.Equal([0], Count("Some text", Entry("Unity", "", "   ")));
    }
}

// The renderer half: a term must not be marked where marking it would be wrong.
public class GlossaryRendererTests
{
    private static readonly GlossaryMatchTerm[] Glossary = [new("Unity", "A game engine", null, [])];

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
