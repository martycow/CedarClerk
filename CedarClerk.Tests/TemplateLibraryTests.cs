using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// Wave 2 item 18 — every starter body, in both languages, must be a valid TipTap document that
// renders through the Blocks renderer without throwing: a starter that cannot publish is worse
// than none. Names and descriptions must exist in both UI languages.
public class TemplateLibraryTests
{
    public static TheoryData<string, string> EveryTemplateInEveryLanguage()
    {
        var data = new TheoryData<string, string>();
        foreach (var entry in TemplateLibrary.All)
        {
            data.Add(entry.Id, Languages.English);
            data.Add(entry.Id, Languages.Russian);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTemplateInEveryLanguage))]
    public void Every_body_is_a_valid_tiptap_document(string id, string language)
    {
        var body = TemplateLibrary.Body(TemplateLibrary.Find(id)!, language);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("doc", doc.RootElement.GetProperty("type").GetString());
        Assert.True(doc.RootElement.GetProperty("content").GetArrayLength() > 0);
    }

    [Theory]
    [MemberData(nameof(EveryTemplateInEveryLanguage))]
    public void Every_body_renders_through_the_blocks_renderer(string id, string language)
    {
        var body = TemplateLibrary.Body(TemplateLibrary.Find(id)!, language);
        var blocks = CedarToTelegramBlocksRenderer.Render(body);
        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void Russian_gets_russian_and_everything_else_starts_from_english()
    {
        var devlog = TemplateLibrary.Find("weekly-devlog")!;
        Assert.Contains("За неделю", TemplateLibrary.Body(devlog, Languages.Russian));
        Assert.Contains("This week", TemplateLibrary.Body(devlog, Languages.English));
        Assert.Contains("This week", TemplateLibrary.Body(devlog, Languages.German));
        Assert.Equal("Еженедельный девлог", TemplateLibrary.Name(devlog, Languages.Russian));
        Assert.Equal("Weekly devlog", TemplateLibrary.Name(devlog, Languages.English));
    }

    [Fact]
    public void The_library_holds_the_four_starters_with_unique_ids()
    {
        Assert.Equal(4, TemplateLibrary.All.Count);
        Assert.Equal(4, TemplateLibrary.All.Select(e => e.Id).Distinct().Count());
        Assert.All(TemplateLibrary.All, e =>
        {
            Assert.NotEmpty(e.NameEn);
            Assert.NotEmpty(e.NameRu);
            Assert.NotEmpty(e.DescriptionEn);
            Assert.NotEmpty(e.DescriptionRu);
        });
        Assert.Null(TemplateLibrary.Find("no-such-template"));
    }
}
