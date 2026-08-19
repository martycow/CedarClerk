using System.Text.Json.Nodes;

namespace CedarClerk.Server.Modules.IndieDev;

// TipTap node builders shared by the module's document generators (changelog T-126, devlog T-158).
internal static class DocJson
{
    public static JsonObject Heading(string text, int level = 2) => new()
    {
        ["type"] = "heading",
        ["attrs"] = new JsonObject { ["level"] = level },
        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
    };

    public static JsonObject Paragraph(string text)
    {
        var node = new JsonObject { ["type"] = "paragraph" };
        // TipTap writes an empty paragraph with no content array at all, not with an empty one.
        if (!string.IsNullOrEmpty(text))
            node["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
        return node;
    }

    public static JsonObject BulletList(IEnumerable<string> items)
    {
        var list = new JsonArray();
        foreach (var item in items)
        {
            list.Add(new JsonObject
            {
                ["type"] = "listItem",
                ["content"] = new JsonArray { Paragraph(item) },
            });
        }
        return new JsonObject { ["type"] = "bulletList", ["content"] = list };
    }

    public static string Doc(JsonArray content) =>
        new JsonObject { ["type"] = "doc", ["content"] = content }.ToJsonString();
}
