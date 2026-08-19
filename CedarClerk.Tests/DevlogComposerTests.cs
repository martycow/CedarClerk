using System.Text.Json;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

public class DevlogComposerTests
{
    private static Build Released(string version, string notes = "") => new()
    {
        OwnerId = "o",
        ProjectId = Guid.NewGuid(),
        Version = version,
        Notes = notes,
        ReleasedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Full_sprint_produces_all_three_sections_in_order()
    {
        var json = SprintEndpoints.DevlogJson("en", ["Shipped A", "Fixed B"], ["Next C"], [Released("0.4.2", "hotfix")]);
        var text = Flatten(json);

        Assert.Contains("What got done", text);
        Assert.Contains("Shipped A", text);
        Assert.Contains("Released", text);
        Assert.Contains("0.4.2 — hotfix", text);
        Assert.Contains("What's next", text);
        Assert.Contains("Next C", text);
        Assert.True(text.IndexOf("What got done", StringComparison.Ordinal) < text.IndexOf("Released", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Released", StringComparison.Ordinal) < text.IndexOf("What's next", StringComparison.Ordinal));
    }

    [Fact]
    public void Russian_document_gets_russian_headings()
    {
        var text = Flatten(SprintEndpoints.DevlogJson("ru", ["Готово"], ["Дальше"], []));
        Assert.Contains("Что сделано", text);
        Assert.Contains("Что дальше", text);
        Assert.DoesNotContain("What got done", text);
    }

    [Fact]
    public void Empty_sections_are_omitted_not_rendered_empty()
    {
        var text = Flatten(SprintEndpoints.DevlogJson("en", ["Only this"], [], []));
        Assert.Contains("What got done", text);
        Assert.DoesNotContain("Released", text);
        Assert.DoesNotContain("What's next", text);
    }

    [Fact]
    public void Body_is_a_valid_tiptap_doc_and_opens_with_an_empty_paragraph()
    {
        using var doc = JsonDocument.Parse(SprintEndpoints.DevlogJson("en", [], [], []));
        Assert.Equal("doc", doc.RootElement.GetProperty("type").GetString());
        var first = doc.RootElement.GetProperty("content")[0];
        Assert.Equal("paragraph", first.GetProperty("type").GetString());
        // TipTap's empty paragraph has no content array at all.
        Assert.False(first.TryGetProperty("content", out _));
    }

    [Fact]
    public void Build_without_notes_is_just_the_version()
    {
        var text = Flatten(SprintEndpoints.DevlogJson("en", [], [], [Released("1.0.0")]));
        Assert.Contains("1.0.0", text);
        Assert.DoesNotContain("—", text);
    }

    /// <summary>Every text node's value, in document order, separated by newlines.</summary>
    private static string Flatten(string cedarJson)
    {
        using var doc = JsonDocument.Parse(cedarJson);
        var parts = new List<string>();
        Walk(doc.RootElement, parts);
        return string.Join("\n", parts);
    }

    private static void Walk(JsonElement node, List<string> parts)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) Walk(child, parts);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("text", out var text)) parts.Add(text.GetString() ?? "");
        foreach (var property in node.EnumerateObject()) Walk(property.Value, parts);
    }
}
