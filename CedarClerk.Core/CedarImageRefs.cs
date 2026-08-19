using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// A document's images in reading order, with alt text. Separate from
// CedarPackage.FindReferencedMediaPaths, which is an unordered set of every media path for packing a
// .cedar: a network that attaches four pictures needs the first four in order, with the alt text
// Bluesky expects.
public static class CedarImageRefs
{
    public record ImageRef(string Src, string? Alt);

    public static List<ImageRef> Collect(string cedarJson)
    {
        var images = new List<ImageRef>();
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return images;
        }

        Walk(doc, images);
        return images;
    }

    private static void Walk(JsonNode? node, List<ImageRef> images)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var child in array) Walk(child, images);
                return;

            case JsonObject obj:
                switch ((string?)obj["type"])
                {
                    case "image":
                        Add(images, (string?)obj["attrs"]?["src"], (string?)obj["attrs"]?["alt"]);
                        break;

                    // A carousel or collage is several pictures in one node, in an attrs array
                    // rather than document nodes — walking the tree alone would miss every one.
                    // The editor stores the items as plain URL strings; indexing ["src"] into a
                    // JsonValue throws, which takes down every page that renders the document.
                    // Objects are also accepted in case an older document shape carried {src, alt}.
                    case "carousel":
                    case "collage":
                        if (obj["attrs"]?["images"] is JsonArray gallery)
                            foreach (var item in gallery)
                            {
                                if (item is JsonObject o) Add(images, (string?)o["src"], (string?)o["alt"]);
                                else Add(images, (string?)item, null);
                            }
                        break;
                }

                foreach (var (_, value) in obj) Walk(value, images);
                return;
        }
    }

    private static void Add(List<ImageRef> images, string? src, string? alt)
    {
        if (string.IsNullOrWhiteSpace(src)) return;
        images.Add(new ImageRef(src, string.IsNullOrWhiteSpace(alt) ? null : alt.Trim()));
    }

    /// <summary>
    /// The bare file name of a <c>/media/...</c> reference, or null for anything else — an external
    /// URL cannot be uploaded from disk, and pretending otherwise would produce a broken post.
    /// </summary>
    public static string? LocalFileName(string src)
    {
        const string prefix = "/media/";
        if (!src.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var name = src[prefix.Length..];
        var query = name.IndexOf('?');
        if (query >= 0) name = name[..query];
        // A traversal in a stored document should be impossible, but this string ends up as a path.
        return name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..")
            ? null
            : Uri.UnescapeDataString(name);
    }
}
