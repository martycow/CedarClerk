using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// T-331/T-355 — the shape of a document preset's ConfigJson, shared by the server (validation,
// skeleton application) and mirrored on the client. A preset names a starting point: which
// built-in type it publishes as (so publishability stays the DocumentTypes contract, never a new
// stored string), an icon, and the heading skeleton the new document is born with.
public sealed record DocumentPresetConfig(string BaseType, string Icon, IReadOnlyList<string> Headings)
{
    public static DocumentPresetConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            var node = JsonNode.Parse(json);
            var baseType = node?["baseType"]?.GetValue<string>();
            var icon = node?["icon"]?.GetValue<string>() ?? "file-text";
            var headings = node?["headings"]?.AsArray()
                .Select(h => (h?.GetValue<string>() ?? "").Trim())
                .Where(h => h.Length > 0)
                .Take(20)
                .ToList() ?? [];
            return new DocumentPresetConfig(
                DocumentTypes.IsKnown(baseType) ? baseType! : DocumentTypes.Post, icon, headings);
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public static readonly DocumentPresetConfig Default = new(DocumentTypes.Post, "file-text", []);

    /// <summary>
    /// The starter document a preset produces — its headings, each with an empty paragraph. The
    /// TipTap shape is built inline (DocJson lives in the server); an empty paragraph carries no
    /// content array at all, which is how TipTap itself writes one.
    /// </summary>
    public string ToCedarJson()
    {
        var content = new JsonArray();
        if (Headings.Count == 0)
        {
            content.Add(new JsonObject { ["type"] = "paragraph" });
        }
        else
        {
            foreach (var heading in Headings)
            {
                content.Add(new JsonObject
                {
                    ["type"] = "heading",
                    ["attrs"] = new JsonObject { ["level"] = 2 },
                    ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = heading } },
                });
                content.Add(new JsonObject { ["type"] = "paragraph" });
            }
        }
        return new JsonObject { ["type"] = "doc", ["content"] = content }.ToJsonString();
    }
}
