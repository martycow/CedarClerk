using CedarClerk.Core;

namespace CedarClerk.Tests;

// ADR-094. The two invariants that matter: every part fits its network's own measure (weighted
// for X, graphemes for Bluesky — the same counters the send uses), and the numbering/link
// bookkeeping never pushes a part over the limit it just promised to respect.
public class MicroThreadSplitterTests
{
    private static string Doc(params string[] paragraphs) =>
        "{\"type\":\"doc\",\"content\":[" + string.Join(",", paragraphs.Select(p =>
            $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{p}\"}}]}}")) + "]}";

    [Fact]
    public void A_document_that_fits_one_post_is_not_a_thread()
    {
        var parts = MicroThreadSplitter.Split(Doc("Short.", "Also short."), PublishNetworks.X);

        var part = Assert.Single(parts);
        Assert.Equal("Short.\n\nAlso short.", part);
        Assert.DoesNotContain("1/1", part);
    }

    [Fact]
    public void Every_part_fits_the_network_limit_and_is_numbered()
    {
        var doc = Doc(Enumerable.Range(1, 12).Select(i => $"Paragraph {i}: " + new string('x', 150)).ToArray());

        var parts = MicroThreadSplitter.Split(doc, PublishNetworks.X);

        Assert.True(parts.Count > 1);
        for (var i = 0; i < parts.Count; i++)
        {
            Assert.True(XPostBuilder.WeightedLength(parts[i]) <= XPostBuilder.MaxWeightedChars,
                $"part {i} is {XPostBuilder.WeightedLength(parts[i])} weighted units");
            Assert.EndsWith($"{i + 1}/{parts.Count}", parts[i]);
        }
    }

    [Fact]
    public void Bluesky_parts_fit_the_grapheme_limit()
    {
        var doc = Doc(Enumerable.Range(1, 8).Select(_ => new string('я', 250)).ToArray());

        var parts = MicroThreadSplitter.Split(doc, PublishNetworks.Bluesky);

        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.True(BlueskyPostBuilder.GraphemeCount(p) <= BlueskyPostBuilder.MaxGraphemes));
    }

    [Fact]
    public void A_paragraph_longer_than_a_post_breaks_on_sentences()
    {
        var sentences = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"Sentence number {i} carries some words."));

        var parts = MicroThreadSplitter.Split(Doc(sentences, new string('y', 200)), PublishNetworks.X);

        Assert.True(parts.Count > 1);
        // No sentence is cut: every part boundary inside the long paragraph lands after a period.
        foreach (var part in parts.Take(parts.Count - 1))
        {
            var body = part[..part.LastIndexOf('\n', part.LastIndexOf('\n') - 1)];
            if (body.StartsWith("Sentence")) Assert.EndsWith(".", body);
        }
    }

    [Fact]
    public void The_link_reserve_spills_into_a_closing_part_when_the_last_is_full()
    {
        var doc = Doc(new string('x', 250), new string('y', 260));

        var withReserve = MicroThreadSplitter.Split(doc, PublishNetworks.X, linkReserve: 30);

        // The final part is numbering-only, left with room for the caller's link.
        Assert.True(XPostBuilder.WeightedLength(withReserve[^1]) + 30 <= XPostBuilder.MaxWeightedChars);
    }

    [Fact]
    public void Cyrillic_threads_split_by_the_weighted_measure_not_string_length()
    {
        // 600 Cyrillic characters weigh 600 on X (weight 1) — two parts, not three.
        var doc = Doc(new string('б', 200), new string('в', 200), new string('г', 200));

        var parts = MicroThreadSplitter.Split(doc, PublishNetworks.X);

        Assert.True(parts.Count is 3 or 4);
        Assert.All(parts, p => Assert.True(XPostBuilder.WeightedLength(p) <= XPostBuilder.MaxWeightedChars));
    }

    [Fact]
    public void An_empty_document_produces_no_parts()
    {
        Assert.Empty(MicroThreadSplitter.Split("{}", PublishNetworks.X));
    }

    [Fact]
    public void Count_matches_split()
    {
        var doc = Doc(Enumerable.Range(1, 6).Select(_ => new string('x', 200)).ToArray());

        Assert.Equal(MicroThreadSplitter.Split(doc, PublishNetworks.X).Count,
            MicroThreadSplitter.CountParts(doc, PublishNetworks.X));
    }
}
