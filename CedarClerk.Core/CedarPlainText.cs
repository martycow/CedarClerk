using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>
/// The plain-text paragraph extraction the short-post builders share (Bluesky's, X's): every
/// paragraph and heading of a Cedar document, in order, marks flattened. What each network then
/// does with the paragraphs — grapheme limits, weighted limits — is the builder's business.
/// </summary>
public static class CedarPlainText
{
    public static List<string> Paragraphs(string cedarJson)
    {
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        var paragraphs = new List<string>();
        Collect(doc, paragraphs);
        return paragraphs;
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

                // A YouTube node is an embed everywhere else (an iframe on the blog, a thumbnail
                // plus a watch link on Telegram) and had no representation here at all — so a post
                // about a video reached Bluesky and X with the video **silently missing**, which is
                // exactly what Marty hit (10.08.2026). These networks cannot embed it either, but
                // they can carry the link, and a link is the whole of what a viewer needs.
                if (type == "youtube")
                {
                    if ((string?)obj["attrs"]?["videoId"] is { Length: > 0 } videoId)
                    {
                        var caption = (string?)obj["attrs"]?["caption"];
                        var url = $"https://www.youtube.com/watch?v={videoId}";
                        // The caption first, so a thread part reads as a sentence rather than as a
                        // bare URL somebody has to guess the point of.
                        paragraphs.Add(string.IsNullOrWhiteSpace(caption) ? url : $"{caption.Trim()} {url}");
                    }
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
            var childType = (string?)obj["type"];
            if (childType == "text") builder.Append((string?)obj["text"]);
            // ADR-128 — a wikilink's label is a word of the sentence; dropping it silently ate a
            // word from teasers and og:description.
            else if (childType == "wikilink") builder.Append((string?)obj["attrs"]?["label"]);
            else builder.Append(PlainText(obj["content"]));
        }
        return builder.ToString().Trim();
    }
}
