using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>
/// Extracts human-readable text from the JSON. Is used for translation
/// </summary>
public static class TipTapTextNodes
{
    // Translatable strings that live in `attrs` rather than a child `text` node — everything else
    // in `attrs` (src, href, images[] urls, latex, unix/format, colspan/rowspan, checked, ids) is
    // structural/opaque and must never be touched. `poll.options` is handled separately below since
    // it's an array of plain strings, not a single value.
    private static readonly Dictionary<string, string[]> AttrTextKeys = new()
    {
        ["image"] = ["alt", "caption"],
        ["video"] = ["caption"],
        ["audio"] = ["caption", "title"],
        ["youtube"] = ["caption"],
        ["footnote"] = ["text"],
        ["toggle"] = ["summary"],
        ["poll"] = ["question"],
    };

    public static List<string> ExtractTexts(string cedarJson)
    {
        var texts = new List<string>();
        var root = JsonNode.Parse(cedarJson);
        Walk(root, (get, _) => texts.Add(get() ?? ""));
        return texts;
    }

    public static string ReplaceTexts(string cedarJson, IReadOnlyList<string> translated)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid JSON");
        var i = 0;

        Walk(root, (_, set) =>
        {
            if (i >= translated.Count)
                throw new ArgumentException("Translated text count does not match document text nodes");
            set(translated[i++]);
        });

        if (i != translated.Count)
            throw new ArgumentException("Translated text count does not match document text nodes");

        // Keep Cyrillic/Unicode readable instead of \uXXXX escapes
        return root.ToJsonString(SerializerOptions);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Fires (get, set) for every translatable string leaf, depth-first, a node's own leaf(s) before
    // its children — ExtractTexts/ReplaceTexts rely on this exact order matching between calls.
    private static void Walk(JsonNode? node, Action<Func<string?>, Action<string>> onLeaf)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var type = obj["type"] is JsonValue t && t.TryGetValue<string>(out var typeStr) ? typeStr : null;

                if (type == "text" && obj["text"] is JsonValue)
                    onLeaf(() => (string?)obj["text"], v => obj["text"] = v);

                if (type is not null && obj["attrs"] is JsonObject attrs)
                {
                    if (AttrTextKeys.TryGetValue(type, out var keys))
                    {
                        foreach (var key in keys)
                        {
                            if (attrs[key] is JsonValue)
                            {
                                var capturedKey = key;
                                onLeaf(() => (string?)attrs[capturedKey], v => attrs[capturedKey] = v);
                            }
                        }
                    }

                    if (type == "poll" && attrs["options"] is JsonArray options)
                    {
                        for (var idx = 0; idx < options.Count; idx++)
                        {
                            if (options[idx] is JsonValue)
                            {
                                var capturedIdx = idx;
                                onLeaf(() => (string?)options[capturedIdx], v => options[capturedIdx] = v);
                            }
                        }
                    }
                }

                if (obj["content"] is JsonArray children)
                {
                    foreach (var child in children)
                        Walk(child, onLeaf);
                }

                break;
            }
            case JsonArray arr:
            {
                foreach (var child in arr)
                    Walk(child, onLeaf);
                break;
            }
        }
    }
}
