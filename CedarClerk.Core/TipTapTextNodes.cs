using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>
/// Extracts human-readable text from the JSON. Is used for translation 
/// </summary>
public static class TipTapTextNodes
{
    public static List<string> ExtractTexts(string cedarJson)
    {
        var texts = new List<string>();
        var root = JsonNode.Parse(cedarJson);
        Walk(root, node =>
        {
            if (node["text"] is JsonValue v && v.TryGetValue<string>(out var s))
                texts.Add(s);
        });
        return texts;
    }

    public static string ReplaceTexts(string cedarJson, IReadOnlyList<string> translated)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid JSON");
        var i = 0;
        
        Walk(root, node =>
        {
            if (node["text"] is JsonValue v && v.TryGetValue<string>(out _))
            {
                if (i >= translated.Count)
                    throw new ArgumentException("Translated text count does not match document text nodes");
                node["text"] = translated[i++];
            }
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
    
    private static void Walk(JsonNode? node, Action<JsonObject> onTextNode)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                if (obj["type"] is JsonValue t && t.TryGetValue<string>(out var type) && type == "text")
                    onTextNode(obj);
                
                if (obj["content"] is JsonArray children)
                {
                    foreach (var child in children)
                        Walk(child, onTextNode);
                }
                
                break;
            }
            case JsonArray arr:
            {
                foreach (var child in arr)
                    Walk(child, onTextNode);
                break;
            }
        }
    }
}
