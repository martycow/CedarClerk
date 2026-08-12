using System.Globalization;
using System.Text;

namespace CedarClerk.Core;

// Offsets are UTF-8 BYTES, not character indexes — the trap in this format. A Russian post is two
// bytes per letter, so a facet measured in characters lands mid-word, or mid-codepoint, and the link
// silently covers the wrong text.
public sealed record BlueskyFacet(int ByteStart, int ByteEnd, string Uri);

public sealed record BlueskyPost(string Text, IReadOnlyList<BlueskyFacet> Facets);

// One short post for Bluesky (T-089). ADR-077: a cross-post is a standalone post with a manual
// override, so the author's own text wins; this builds the fallback, because publishing must never
// block on writing a second version.
public static class BlueskyPostBuilder
{
    // Graphemes, not chars: an emoji or combining sequence is one, and counting UTF-16 units would
    // reject posts the network accepts.
    public const int MaxGraphemes = 300;

    private const string Ellipsis = "…";
    public static BlueskyPost Build(string? authorText, string cedarJson, string? blogUrl)
    {
        var body = string.IsNullOrWhiteSpace(authorText)
            ? Teaser(cedarJson)
            : authorText.Trim();

        // The link is worth more than the last sentence of a teaser, so the body is trimmed to fit
        // around it rather than the link being dropped when the two do not both fit.
        if (!string.IsNullOrWhiteSpace(blogUrl))
        {
            var linkLength = GraphemeCount(blogUrl) + 1; // the newline before it
            body = Truncate(body, Math.Max(0, MaxGraphemes - linkLength));
            var text = body.Length == 0 ? blogUrl! : body + "\n" + blogUrl;
            var start = Utf8Length(text) - Utf8Length(blogUrl!);
            return new BlueskyPost(text, [new BlueskyFacet(start, Utf8Length(text), blogUrl!)]);
        }

        return new BlueskyPost(Truncate(body, MaxGraphemes), []);
    }

    // Opening paragraphs until the limit, rather than a character slice, so the post ends on a
    // thought rather than mid-word.
    public static string Teaser(string cedarJson)
    {
        var paragraphs = CedarPlainText.Paragraphs(cedarJson);

        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length == 0) continue;
            var candidate = builder.Length == 0 ? paragraph : builder + "\n\n" + paragraph;
            if (GraphemeCount(candidate) > MaxGraphemes) break;
            builder.Clear();
            builder.Append(candidate);
        }

        // Nothing fit whole: fall back to a truncated first paragraph rather than an empty post.
        return builder.Length > 0 ? builder.ToString() : Truncate(paragraphs.FirstOrDefault() ?? "", MaxGraphemes);
    }

    /// <summary>Truncates on a grapheme boundary and marks the cut, never mid-codepoint.</summary>
    public static string Truncate(string text, int maxGraphemes)
    {
        if (maxGraphemes <= 0) return "";
        if (GraphemeCount(text) <= maxGraphemes) return text;
        if (maxGraphemes == 1) return Ellipsis;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var builder = new StringBuilder();
        var taken = 0;
        while (enumerator.MoveNext() && taken < maxGraphemes - 1)
        {
            builder.Append(enumerator.GetTextElement());
            taken++;
        }
        return builder.ToString().TrimEnd() + Ellipsis;
    }

    public static int GraphemeCount(string text) => new StringInfo(text).LengthInTextElements;

    private static int Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);
}
