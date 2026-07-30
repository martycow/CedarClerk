using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-018.1. The rule that would have stopped the 29.07.2026 wipe, pinned here because it is
// deliberately lenient: every threshold below is a judgement call about how much deletion is
// normal, and a silent change to one of them brings back exactly the incident it exists for.
public class ShrinkGuardTests
{
    private static string Doc(params string[] paragraphs) =>
        "{\"type\":\"doc\",\"content\":[" + string.Join(",", paragraphs.Select(p =>
            $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{p}\"}}]}}")) + "]}";

    private static string LongDoc(int chars) => Doc(new string('a', chars));

    private const string EmptyDoc = "{\"type\":\"doc\",\"content\":[]}";

    [Fact]
    public void Empty_over_a_real_post_is_suspicious()
    {
        var verdict = ShrinkGuard.Inspect(LongDoc(2000), EmptyDoc);
        Assert.True(verdict.Suspicious);
        Assert.Equal(2000, verdict.StoredTextLength);
        Assert.Equal(0, verdict.IncomingTextLength);
    }

    [Fact]
    public void Empty_over_a_stub_is_not_guarded()
    {
        // Clearing a couple of sentences is ordinary editing, not the incident.
        Assert.False(ShrinkGuard.Inspect(LongDoc(ShrinkGuard.MinGuardedTextLength - 1), EmptyDoc).Suspicious);
    }

    [Fact]
    public void Heavy_but_ordinary_editing_passes()
    {
        // Half the post deleted in one save: aggressive, still nowhere near a wipe.
        Assert.False(ShrinkGuard.Inspect(LongDoc(2000), LongDoc(1000)).Suspicious);
    }

    [Fact]
    public void Cutting_past_the_share_is_suspicious()
    {
        Assert.True(ShrinkGuard.Inspect(LongDoc(2000), LongDoc(300)).Suspicious);
        Assert.False(ShrinkGuard.Inspect(LongDoc(2000), LongDoc(401)).Suspicious);
    }

    [Fact]
    public void Growth_is_never_suspicious()
    {
        Assert.False(ShrinkGuard.Inspect(LongDoc(2000), LongDoc(2001)).Suspicious);
    }

    [Fact]
    public void Structural_churn_is_measured_by_text_not_json()
    {
        // A table turning into paragraphs rewrites most of the JSON while keeping the words —
        // measuring raw JSON length here would refuse an ordinary edit.
        var table = "{\"type\":\"doc\",\"content\":[{\"type\":\"table\",\"content\":[{\"type\":\"tableRow\",\"content\":" +
                    "[{\"type\":\"tableCell\",\"attrs\":{\"colspan\":1,\"rowspan\":1},\"content\":[{\"type\":\"paragraph\"," +
                    $"\"content\":[{{\"type\":\"text\",\"text\":\"{new string('b', 400)}\"}}]}}]}}]}}]}}";
        Assert.False(ShrinkGuard.Inspect(table, LongDoc(400)).Suspicious);
    }

    [Fact]
    public void Unparseable_stored_content_does_not_block_the_save()
    {
        // It can't be measured, and refusing every save of a document the guard doesn't
        // understand would be worse than not guarding it.
        Assert.False(ShrinkGuard.Inspect("not json at all", EmptyDoc).Suspicious);
    }
}
