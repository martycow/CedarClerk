using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>
/// The outbound web links of a document, for the pre-publish dead-link probe (T-238): hrefs of
/// link marks plus any node whose src points at an external host. Only absolute http(s) URLs —
/// /media/ paths are ours and checked by existence, not by request.
/// </summary>
public static class CedarLinkScan
{
    public static IReadOnlyList<string> CollectLinks(string cedarJson)
    {
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (JsonException)
        {
            return links;
        }

        Walk(doc, links, seen);
        return links;
    }

    private static void Walk(JsonNode? node, List<string> links, HashSet<string> seen)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array) Walk(child, links, seen);
                return;

            case JsonObject obj:
                if (obj["marks"] is JsonArray marks)
                    foreach (var mark in marks)
                        if ((string?)mark?["type"] == "link")
                            Add((string?)mark?["attrs"]?["href"], links, seen);

                Add((string?)obj["attrs"]?["src"], links, seen);

                foreach (var (_, value) in obj) Walk(value, links, seen);
                return;
        }
    }

    private static void Add(string? url, List<string> links, HashSet<string> seen)
    {
        if (url is null) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        if (seen.Add(url)) links.Add(url);
    }
}
