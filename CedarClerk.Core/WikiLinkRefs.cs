using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// ADR-128 — the draft ids a document's wikilink nodes point at: distinct, document order.
// Corrupt JSON contributes nothing — the diff-sync must never fail a save over one bad document.
public static class WikiLinkRefs
{
    public static IReadOnlyList<Guid> Collect(string cedarJson)
    {
        var ids = new List<Guid>();
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return ids;
        }

        Walk(doc, ids);
        return ids.Distinct().ToList();
    }

    private static void Walk(JsonNode? node, List<Guid> ids)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array) Walk(child, ids);
                return;

            case JsonObject obj:
                if ((string?)obj["type"] == "wikilink"
                    && obj["attrs"] is JsonObject attrs
                    && Guid.TryParse((string?)attrs["draftId"], out var id))
                {
                    ids.Add(id);
                }
                Walk(obj["content"], ids);
                return;
        }
    }
}
