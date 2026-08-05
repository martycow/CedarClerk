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
}
