using CedarClerk.Core;

namespace CedarClerk.Tests;

public class TipTapTextNodesTests
{
    private const string Doc = """
        {"type":"doc","content":[
          {"type":"paragraph","content":[
            {"type":"text","text":"Привет"},
            {"type":"text","marks":[{"type":"bold"}],"text":"мир"}
          ]},
          {"type":"image","attrs":{"src":"/media/x.png","alt":"котик"}},
          {"type":"blockquote","content":[
            {"type":"paragraph","content":[{"type":"text","text":"цитата"}]}
          ]}
        ]}
        """;

    [Fact]
    public void Extracts_text_nodes_in_document_order()
    {
        var texts = TipTapTextNodes.ExtractTexts(Doc);
        Assert.Equal(new[] { "Привет", "мир", "цитата" }, texts);
    }

    [Fact]
    public void Does_not_extract_attrs_strings()
    {
        var texts = TipTapTextNodes.ExtractTexts(Doc);
        Assert.DoesNotContain("котик", texts);
        Assert.DoesNotContain("/media/x.png", texts);
    }

    [Fact]
    public void Replace_roundtrip_preserves_structure_and_swaps_text()
    {
        var replaced = TipTapTextNodes.ReplaceTexts(Doc, ["Hello", "world", "quote"]);
        Assert.Equal(new[] { "Hello", "world", "quote" }, TipTapTextNodes.ExtractTexts(replaced));
        // structure and attrs untouched
        Assert.Contains("\"bold\"", replaced);
        Assert.Contains("/media/x.png", replaced);
        Assert.Contains("котик", replaced);
    }

    [Fact]
    public void Replace_with_wrong_count_throws()
    {
        Assert.Throws<ArgumentException>(() => TipTapTextNodes.ReplaceTexts(Doc, ["only one"]));
        Assert.Throws<ArgumentException>(() => TipTapTextNodes.ReplaceTexts(Doc, ["1", "2", "3", "4"]));
    }

    [Fact]
    public void Empty_document_extracts_nothing()
    {
        Assert.Empty(TipTapTextNodes.ExtractTexts("""{"type":"doc","content":[]}"""));
    }
}
