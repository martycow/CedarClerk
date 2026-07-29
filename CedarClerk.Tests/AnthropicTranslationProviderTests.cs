using CedarClerk.Server.Translation;
using Xunit;

namespace CedarClerk.Tests;

// Only the pure ChunkByBudget helper is covered here — TranslateAsync itself makes a real
// Anthropic API call and is verified manually (see docs/DECISIONS.md ADR-059).
public class AnthropicTranslationProviderTests
{
    [Fact]
    public void ChunkByBudget_PacksItemsUntilCharBudgetExceeded()
    {
        var items = new List<string> { "12345", "12345", "12345" }; // 5 chars each
        var chunks = AnthropicTranslationProvider.ChunkByBudget(items, charBudget: 10, maxCount: 100);

        Assert.Equal(new[] { 2, 1 }, chunks.Select(c => c.Count));
    }

    [Fact]
    public void ChunkByBudget_ClosesChunkAtMaxCountEvenUnderCharBudget()
    {
        var items = new List<string> { "a", "b", "c", "d" };
        var chunks = AnthropicTranslationProvider.ChunkByBudget(items, charBudget: 1_000, maxCount: 2);

        Assert.Equal(new[] { 2, 2 }, chunks.Select(c => c.Count));
    }

    [Fact]
    public void ChunkByBudget_OversizedSingleStringBecomesItsOwnChunk()
    {
        var huge = new string('x', 100);
        var items = new List<string> { "a", huge, "b" };
        var chunks = AnthropicTranslationProvider.ChunkByBudget(items, charBudget: 10, maxCount: 100);

        Assert.Equal(3, chunks.Count);
        Assert.Equal([huge], chunks[1]);
    }

    [Fact]
    public void ChunkByBudget_PreservesOrderAndAllItems()
    {
        var items = Enumerable.Range(0, 20).Select(i => i.ToString()).ToList();
        var chunks = AnthropicTranslationProvider.ChunkByBudget(items, charBudget: 5, maxCount: 3);

        Assert.Equal(items, chunks.SelectMany(c => c));
    }

    [Fact]
    public void ChunkByBudget_EmptyInput_ReturnsNoChunks()
    {
        Assert.Empty(AnthropicTranslationProvider.ChunkByBudget([], charBudget: 10, maxCount: 10));
    }
}
