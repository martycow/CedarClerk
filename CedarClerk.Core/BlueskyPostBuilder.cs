using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>A link inside a Bluesky post, addressed the way the AT Protocol addresses it.</summary>
/// <param name="ByteStart">
/// UTF-8 BYTE offset, not a character index. This is the trap in the whole format: a Russian post
/// is two bytes per letter, so a facet measured in characters points into the middle of a word —
/// or into the middle of a codepoint — and the link silently covers the wrong text.
/// </param>
public sealed record BlueskyFacet(int ByteStart, int ByteEnd, string Uri);

public sealed record BlueskyPost(string Text, IReadOnlyList<BlueskyFacet> Facets);

/// <summary>
/// Turns a Cedar document into the one short post Bluesky takes (T-089).
///
/// ADR-077 decided what this is for: a cross-post is a **standalone post with a manual override**,
/// so the author's own text wins whenever they wrote one. This builds the fallback for when they
/// did not — publishing must never block on writing a second version of the post.
/// </summary>
public static class BlueskyPostBuilder
{
    /// <summary>
    /// Bluesky counts graphemes, not chars: an emoji or a combining sequence is one. Counting
    /// UTF-16 units instead would reject posts the network accepts.
    /// </summary>
    public const int MaxGraphemes = 300;

    private const string Ellipsis = "…";

    /// <param name="authorText">The author's own text for this network (ADR-077), or null.</param>
    /// <param name="blogUrl">Appended when there is room, and always as a real link facet.</param>
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

    /// <summary>
    /// The fallback text: the document's opening paragraphs, in order, until the limit. Paragraphs
    /// rather than a character slice, so the post ends on a thought rather than mid-word.
    /// </summary>
    public static string Teaser(string cedarJson)
    {
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }

        var paragraphs = new List<string>();
        Collect(doc, paragraphs);

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

    private static void Collect(JsonNode? node, List<string> paragraphs)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array) Collect(child, paragraphs);
                return;
            case JsonObject obj:
                var type = (string?)obj["type"];
                if (type is "paragraph" or "heading")
                {
                    var text = PlainText(obj["content"]);
                    if (text.Length > 0) paragraphs.Add(text);
                    return;
                }
                foreach (var (_, value) in obj) Collect(value, paragraphs);
                return;
        }
    }

    private static string PlainText(JsonNode? content)
    {
        if (content is not JsonArray array) return "";
        var builder = new StringBuilder();
        foreach (var child in array)
        {
            if (child is not JsonObject obj) continue;
            if ((string?)obj["type"] == "text") builder.Append((string?)obj["text"]);
            else builder.Append(PlainText(obj["content"]));
        }
        return builder.ToString().Trim();
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
