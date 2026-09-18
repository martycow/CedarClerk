using System.Text.Json;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-015. The splice reuses blocks by position, so a wrong alignment does not fail loudly — it
// silently pairs the wrong paragraphs and the author gets a shuffled translation. Every case
// below is one way that could happen.
public class IncrementalTranslationPlanTests
{
    private static string Doc(params string[] paragraphs) =>
        "{\"type\":\"doc\",\"content\":[" + string.Join(",", paragraphs.Select(p =>
            $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{p}\"}}]}}")) + "]}";

    private static List<string> TextsOf(string docJson)
    {
        using var doc = JsonDocument.Parse(docJson);
        return doc.RootElement.GetProperty("content").EnumerateArray()
            .Select(b => b.GetProperty("content")[0].GetProperty("text").GetString()!)
            .ToList();
    }

    [Fact]
    public void One_changed_paragraph_sends_only_that_paragraph()
    {
        var plan = IncrementalTranslationPlan.Build(
            sourceSnapshotJson: Doc("one", "two", "three"),
            newSourceJson: Doc("one", "TWO CHANGED", "three"),
            existingTranslationJson: Doc("один", "два", "три"));

        Assert.NotNull(plan);
        Assert.Equal(["TWO CHANGED"], TextsOf(IncrementalTranslationPlan.PartialDocument(plan!)));
        Assert.Equal(2, plan!.ReusedCount);
    }

    [Fact]
    public void Assemble_puts_the_translated_block_back_in_its_place()
    {
        var plan = IncrementalTranslationPlan.Build(
            Doc("one", "two", "three"), Doc("one", "TWO CHANGED", "three"), Doc("один", "два", "три"));

        var assembled = IncrementalTranslationPlan.Assemble(plan!, Doc("один", "два", "три"), Doc("ДВА НОВОЕ"));

        // The manual wording of the untouched blocks survives — that is the whole point.
        Assert.Equal(["один", "ДВА НОВОЕ", "три"], TextsOf(assembled));
    }

    [Fact]
    public void An_inserted_paragraph_shifts_the_rest_without_re_translating_them()
    {
        var plan = IncrementalTranslationPlan.Build(
            Doc("one", "two"), Doc("one", "inserted", "two"), Doc("один", "два"));

        Assert.Equal(["inserted"], TextsOf(IncrementalTranslationPlan.PartialDocument(plan!)));
        var assembled = IncrementalTranslationPlan.Assemble(plan!, Doc("один", "два"), Doc("вставлено"));
        Assert.Equal(["один", "вставлено", "два"], TextsOf(assembled));
    }

    [Fact]
    public void A_deleted_paragraph_drops_its_translation_too()
    {
        var plan = IncrementalTranslationPlan.Build(
            Doc("one", "two", "three"), Doc("one", "three"), Doc("один", "два", "три"));

        Assert.NotNull(plan);
        Assert.Empty(plan!.BlocksToTranslate);
        Assert.Equal(["один", "три"], TextsOf(IncrementalTranslationPlan.Assemble(plan, Doc("один", "два", "три"), Doc())));
    }

    [Fact]
    public void No_snapshot_means_translate_everything()
    {
        Assert.Null(IncrementalTranslationPlan.Build(null, Doc("one"), Doc("один")));
        Assert.Null(IncrementalTranslationPlan.Build("", Doc("one"), Doc("один")));
    }

    [Theory]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[]}]}")]
    public void Start_empty_requires_a_full_translation_even_when_the_source_is_unchanged(string empty)
    {
        Assert.Null(IncrementalTranslationPlan.Build(Doc("one"), Doc("one"), empty));
    }

    [Fact]
    public void Whitespace_only_translation_requires_a_full_translation()
    {
        Assert.Null(IncrementalTranslationPlan.Build(Doc("one", "two"), Doc("one", "two"), Doc(" ", "  ")));
    }

    [Fact]
    public void Partially_written_translation_preserves_manual_text_and_empty_blocks()
    {
        var existing = Doc("manual wording", " ");
        var plan = IncrementalTranslationPlan.Build(Doc("one", "two"), Doc("one", "two"), existing);

        Assert.NotNull(plan);
        Assert.Empty(plan!.BlocksToTranslate);
        Assert.Equal(["manual wording", " "], TextsOf(IncrementalTranslationPlan.Assemble(plan, existing, Doc())));
    }

    [Fact]
    public void A_source_without_text_can_still_reuse_empty_blocks()
    {
        var empty = Doc(" ");
        var plan = IncrementalTranslationPlan.Build(empty, empty, empty);

        Assert.NotNull(plan);
        Assert.Empty(plan!.BlocksToTranslate);
    }

    [Theory]
    [InlineData("{\"type\":\"image\",\"attrs\":{\"src\":\"/media/manual.png\"}}")]
    [InlineData("{\"type\":\"codeBlock\",\"content\":[{\"type\":\"text\",\"text\":\"manual code\"}]}")]
    public void Authored_non_paragraph_content_is_not_an_empty_translation(string block)
    {
        var existing = "{\"type\":\"doc\",\"content\":[" + block + "]}";
        var plan = IncrementalTranslationPlan.Build(Doc("one"), Doc("one"), existing);

        Assert.NotNull(plan);
        Assert.Empty(plan!.BlocksToTranslate);
        Assert.Equal(1, plan.ReusedCount);
    }

    [Fact]
    public void A_hand_restructured_translation_falls_back_to_a_full_translation()
    {
        // The translation has four blocks where the snapshot had three: position no longer means
        // anything, and splicing by position would pair the wrong paragraphs.
        Assert.Null(IncrementalTranslationPlan.Build(
            Doc("one", "two", "three"), Doc("one", "two", "THREE CHANGED"), Doc("один", "два", "три", "лишний")));
    }

    [Fact]
    public void A_fully_rewritten_document_falls_back_to_a_full_translation()
    {
        // Nothing to reuse: the splice would only add bookkeeping.
        Assert.Null(IncrementalTranslationPlan.Build(
            Doc("one", "two"), Doc("alpha", "beta"), Doc("один", "два")));
    }

    [Fact]
    public void An_unchanged_document_needs_no_provider_call_at_all()
    {
        var plan = IncrementalTranslationPlan.Build(Doc("one", "two"), Doc("one", "two"), Doc("один", "два"));

        Assert.NotNull(plan);
        Assert.Empty(plan!.BlocksToTranslate);
        Assert.Equal(2, plan.ReusedCount);
    }

    [Fact]
    public void Assemble_refuses_an_answer_with_the_wrong_number_of_blocks()
    {
        var plan = IncrementalTranslationPlan.Build(
            Doc("one", "two"), Doc("one", "TWO CHANGED"), Doc("один", "два"));

        // A provider that split or merged blocks would otherwise corrupt the document silently.
        Assert.Throws<ArgumentException>(() =>
            IncrementalTranslationPlan.Assemble(plan!, Doc("один", "два"), Doc("а", "б")));
    }

    [Fact]
    public void Unparseable_content_falls_back_rather_than_throwing()
    {
        Assert.Null(IncrementalTranslationPlan.Build("not json", Doc("one"), Doc("один")));
        Assert.Null(IncrementalTranslationPlan.Build(Doc("one"), "not json", Doc("один")));
        Assert.Null(IncrementalTranslationPlan.Build(Doc("one"), Doc("one"), "not json"));
    }
}
