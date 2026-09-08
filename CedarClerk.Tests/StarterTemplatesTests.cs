using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

public class StarterTemplatesTests
{
    [Theory]
    [InlineData(DocumentTypes.Design, ProjectTypes.FullGame, "en", "Core loop")]
    [InlineData(DocumentTypes.Changelog, ProjectTypes.Product, "en", "Unreleased")]
    [InlineData(DocumentTypes.Design, ProjectTypes.FullGame, "ru", "Концепт")]
    [InlineData(DocumentTypes.Changelog, ProjectTypes.Product, "ru", "Не выпущено")]
    public void Each_starter_gets_its_skeleton_in_the_right_language(string doc, string project, string lang, string expected)
    {
        Assert.Contains(expected, Headings(StarterTemplates.For(doc, project, lang)));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ja")]
    public void Other_languages_fall_back_to_english(string lang)
    {
        Assert.Contains("Core loop", Headings(StarterTemplates.For(DocumentTypes.Design, ProjectTypes.FullGame, lang)));
    }

    /// <summary>Heading texts, decoded — the raw JSON escapes Cyrillic as \uXXXX.</summary>
    private static List<string> Headings(string cedarJson)
    {
        using var doc = JsonDocument.Parse(cedarJson);
        return doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == "heading")
            .Select(n => n.GetProperty("content")[0].GetProperty("text").GetString() ?? "")
            .ToList();
    }

    [Theory]
    [InlineData(ProjectTypes.Empty)]
    [InlineData(ProjectTypes.Work)]
    [InlineData(ProjectTypes.Vault)]
    public void A_note_starter_carries_no_skeleton(string projectType)
    {
        using var doc = JsonDocument.Parse(StarterTemplates.For(DocumentTypes.Note, projectType, "en"));
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("paragraph", content[0].GetProperty("type").GetString());
    }

    [Fact]
    public void Post_starter_stays_a_blank_document()
    {
        using var doc = JsonDocument.Parse(StarterTemplates.For(DocumentTypes.Post, ProjectTypes.FullGame, "en"));
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("paragraph", content[0].GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(DocumentTypes.Design, ProjectTypes.FullGame)]
    [InlineData(DocumentTypes.Note, ProjectTypes.Vault)]
    [InlineData(DocumentTypes.Changelog, ProjectTypes.Product)]
    public void Every_skeleton_is_a_valid_tiptap_doc(string docType, string projectType)
    {
        using var doc = JsonDocument.Parse(StarterTemplates.For(docType, projectType, "en"));
        Assert.Equal("doc", doc.RootElement.GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("content").GetArrayLength() > 0);
    }
}
