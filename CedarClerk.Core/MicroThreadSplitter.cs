using System.Globalization;
using System.Text;

namespace CedarClerk.Core;

// ADR-094 — a whole document as a microblog thread, each part measured the way that network measures
// (X's weighted units, Bluesky's graphemes) and numbered "N/M". The caller appends the blog link;
// the last part's budget reserves room for it.
//
// Paragraph-first, like every splitter here: a paragraph that overflows breaks on sentences, then on
// words, and only a word longer than a whole post is cut mid-word.
public static class MicroThreadSplitter
{
    /// <summary>Budget kept for the "\n\nN/M" suffix ("\n\n999/999" measures 9 either way).</summary>
    public const int NumberingReserve = 10;

    public static IReadOnlyList<string> Split(string cedarJson, string network, int linkReserve = 0)
    {
        var (measure, limit) = ForNetwork(network);
        var paragraphs = CedarPlainText.Paragraphs(cedarJson).Where(p => p.Length > 0).ToList();
        if (paragraphs.Count == 0) return [];

        // A document that fits one post whole is not a thread — no numbering, no reserve for it.
        var whole = string.Join("\n\n", paragraphs);
        if (measure(whole) <= limit - linkReserve) return [whole];

        var budget = limit - NumberingReserve;
        var parts = new List<string>();
        var current = new StringBuilder();

        foreach (var piece in paragraphs.SelectMany(p => FitPieces(p, measure, budget)))
        {
            var candidate = current.Length == 0 ? piece : current + "\n\n" + piece;
            if (measure(candidate) > budget && current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
                candidate = piece;
            }
            current.Clear();
            current.Append(candidate);
        }
        if (current.Length > 0) parts.Add(current.ToString());

        // The link rides on the last part; when it does not fit there, it becomes its own closing
        // part rather than pushing the text around.
        if (linkReserve > 0 && parts.Count > 0 && measure(parts[^1]) > budget - linkReserve)
            parts.Add("");

        return parts.Select((p, i) => p.Length == 0 ? $"{i + 1}/{parts.Count}" : $"{p}\n\n{i + 1}/{parts.Count}").ToList();
    }

    /// <summary>How many parts a thread publish of this document will create — what the queue and the cost note ask.</summary>
    public static int CountParts(string cedarJson, string network, int linkReserve = 0) =>
        Math.Max(1, Split(cedarJson, network, linkReserve).Count);

    public static (Func<string, int> Measure, int Limit) ForNetwork(string network) => network switch
    {
        PublishNetworks.X => (XPostBuilder.WeightedLength, XPostBuilder.MaxWeightedChars),
        PublishNetworks.Bluesky => (BlueskyPostBuilder.GraphemeCount, BlueskyPostBuilder.MaxGraphemes),
        // Discord never threads (ADR-131) — this only serves length displays and previews.
        PublishNetworks.Discord => (static text => text.Length, DiscordPostBuilder.MaxChars),
        _ => throw new NotSupportedException($"No micro-thread rules for network '{network}'"),
    };

    /// <summary>A paragraph as-is when it fits, otherwise sentence pieces, then word pieces.</summary>
    private static IEnumerable<string> FitPieces(string paragraph, Func<string, int> measure, int budget)
    {
        if (measure(paragraph) <= budget) return [paragraph];
        return PackPieces(SplitSentences(paragraph), measure, budget);
    }

    private static IEnumerable<string> PackPieces(IEnumerable<string> pieces, Func<string, int> measure, int budget)
    {
        var current = new StringBuilder();
        foreach (var raw in pieces)
        {
            var piece = raw.Trim();
            if (piece.Length == 0) continue;

            if (measure(piece) > budget)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                // A sentence over budget packs word by word; a word over budget is sliced hard.
                var words = piece.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 1)
                {
                    foreach (var packed in PackPieces(words, measure, budget)) yield return packed;
                }
                else
                {
                    foreach (var slice in HardSlice(piece, measure, budget)) yield return slice;
                }
                continue;
            }

            var candidate = current.Length == 0 ? piece : current + " " + piece;
            if (measure(candidate) > budget)
            {
                yield return current.ToString();
                current.Clear();
                current.Append(piece);
            }
            else
            {
                current.Clear();
                current.Append(candidate);
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static IEnumerable<string> SplitSentences(string paragraph)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < paragraph.Length; i++)
        {
            if (paragraph[i] is '.' or '!' or '?' && (i + 1 == paragraph.Length || paragraph[i + 1] == ' '))
            {
                sentences.Add(paragraph[start..(i + 1)]);
                start = i + 1;
            }
        }
        if (start < paragraph.Length) sentences.Add(paragraph[start..]);
        return sentences;
    }

    private static IEnumerable<string> HardSlice(string word, Func<string, int> measure, int budget)
    {
        var current = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(word);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (current.Length > 0 && measure(current.ToString() + element) > budget)
            {
                yield return current.ToString();
                current.Clear();
            }
            current.Append(element);
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
