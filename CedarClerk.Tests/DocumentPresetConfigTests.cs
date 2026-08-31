using System.Text.Json;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-331/T-355 — a document preset's config parses defensively and produces a valid skeleton.
public class DocumentPresetConfigTests
{
    [Fact]
    public void Parse_keeps_a_known_base_type_and_falls_back_otherwise()
    {
        Assert.Equal("design", DocumentPresetConfig.Parse("""{"baseType":"design"}""").BaseType);
        // An unknown base type never becomes a stored type string — publishability keys on the
        // DocumentTypes contract, so it falls back to Post.
        Assert.Equal(DocumentTypes.Post, DocumentPresetConfig.Parse("""{"baseType":"nonsense"}""").BaseType);
        Assert.Equal(DocumentTypes.Post, DocumentPresetConfig.Parse("not json").BaseType);
    }

    [Fact]
    public void Headings_are_trimmed_capped_and_blanks_dropped()
    {
        var cfg = DocumentPresetConfig.Parse("""{"headings":["  A ","","B"]}""");
        Assert.Equal(["A", "B"], cfg.Headings);
    }

    [Fact]
    public void Skeleton_is_a_valid_tiptap_doc_with_a_heading_per_line()
    {
        var cfg = new DocumentPresetConfig("post", "file-text", ["Concept", "Systems"]);
        using var doc = JsonDocument.Parse(cfg.ToCedarJson());
        Assert.Equal("doc", doc.RootElement.GetProperty("type").GetString());
        var content = doc.RootElement.GetProperty("content");
        // A heading + an empty paragraph per line.
        Assert.Equal(4, content.GetArrayLength());
        Assert.Equal("heading", content[0].GetProperty("type").GetString());
        Assert.Equal("Concept", content[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void An_empty_skeleton_is_a_single_blank_paragraph()
    {
        var cfg = new DocumentPresetConfig("post", "file-text", []);
        using var doc = JsonDocument.Parse(cfg.ToCedarJson());
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("paragraph", content[0].GetProperty("type").GetString());
    }
}
