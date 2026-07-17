using System.Text.Json.Nodes;

namespace CedarClerk.Core;

public sealed record HeadingEntry(string Slug, string Text, int Level);

// Shared heading-collection pass used by both CedarToBlogHtmlRenderer (heading id= attributes)
// and CedarToTelegramBlocksRenderer (anchor blocks), so a "tableOfContents" node's links resolve
// to the exact same slugs the headings themselves get. One document-order walk, entries emitted
// 1:1 with every heading node encountered (never skipped, even if text is empty) so callers can
// pair them up by a simple running index during their own render pass.
public static class HeadingOutline
{
    public static List<HeadingEntry> Extract(JsonNode? doc)
    {
        var entries = new List<HeadingEntry>();
        var used = new HashSet<string>();
        Walk(doc, entries, used);
        return entries;
    }

    // Authors routinely type the "real" article title as the document's own first heading,
    // on top of the separate Draft.Title field. Callers use this to skip rendering a redundant
    // page-level title in that case instead of stacking two look-alike headings.
    public static bool StartsWithHeading(string cedarJson)
    {
        var root = JsonNode.Parse(cedarJson);
        var content = (root as JsonObject)?["content"] as JsonArray;
        var first = content?.Count > 0 ? content[0] : null;
        return (string?)(first as JsonObject)?["type"] == "heading";
    }

    private static void Walk(JsonNode? node, List<HeadingEntry> entries, HashSet<string> used)
    {
        if (node is JsonArray arr)
        {
            foreach (var n in arr)
                Walk(n, entries, used);
            return;
        }

        if (node is not JsonObject)
            return;

        if ((string?)node["type"] == "heading")
        {
            var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 1, 6);
            var text = PlainText(node["content"]?.AsArray());
            entries.Add(new HeadingEntry(UniqueSlug(text, used), text, level));
        }

        Walk(node["content"], entries, used);
    }

    private static string PlainText(JsonArray? nodes)
    {
        if (nodes is null)
            return "";

        var text = "";
        foreach (var n in nodes)
            if ((string?)n!["type"] == "text")
                text += (string?)n["text"] ?? "";
        return text.Trim();
    }

    private static string UniqueSlug(string text, HashSet<string> used)
    {
        var baseSlug = SlugGenerator.Slugify(text);
        var candidate = baseSlug;
        var n = 2;
        while (!used.Add(candidate))
            candidate = $"{baseSlug}-{n++}";
        return candidate;
    }
}
