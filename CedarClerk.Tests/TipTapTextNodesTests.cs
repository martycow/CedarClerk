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
          {"type":"image","attrs":{"src":"/media/x.png","alt":"котик","caption":"подпись"}},
          {"type":"blockquote","content":[
            {"type":"paragraph","content":[{"type":"text","text":"цитата"}]}
          ]}
        ]}
        """;

    [Fact]
    public void Extracts_text_nodes_in_document_order()
    {
        var texts = TipTapTextNodes.ExtractTexts(Doc);
        Assert.Equal(new[] { "Привет", "мир", "котик", "подпись", "цитата" }, texts);
    }

    [Fact]
    public void Extracts_and_replaces_image_alt_and_caption()
    {
        var texts = TipTapTextNodes.ExtractTexts(Doc);
        Assert.Contains("котик", texts);
        Assert.Contains("подпись", texts);

        var translated = texts.Select(t => t + "!").ToList();
        var replaced = TipTapTextNodes.ReplaceTexts(Doc, translated);
        Assert.Contains("котик!", replaced);
        Assert.Contains("подпись!", replaced);
        Assert.Contains("/media/x.png", replaced); // src untouched
    }

    [Theory]
    [InlineData("""{"type":"doc","content":[{"type":"video","attrs":{"src":"/v.mp4","caption":"клип"}}]}""", "клип")]
    [InlineData("""{"type":"doc","content":[{"type":"audio","attrs":{"src":"/a.mp3","caption":"звук","title":"название"}}]}""", "звук")]
    [InlineData("""{"type":"doc","content":[{"type":"youtube","attrs":{"videoId":"abc","caption":"ролик"}}]}""", "ролик")]
    [InlineData("""{"type":"doc","content":[{"type":"footnote","attrs":{"id":"1","text":"сноска"}}]}""", "сноска")]
    [InlineData("""{"type":"doc","content":[{"type":"toggle","attrs":{"summary":"детали"},"content":[]}]}""", "детали")]
    public void Extracts_attrs_text_for_node_type(string doc, string expected)
    {
        Assert.Contains(expected, TipTapTextNodes.ExtractTexts(doc));
    }

    [Fact]
    public void Extracts_poll_question_and_options()
    {
        const string doc = """{"type":"doc","content":[{"type":"poll","attrs":{"id":"1","question":"Вопрос?","options":["Да","Нет"]}}]}""";
        var texts = TipTapTextNodes.ExtractTexts(doc);
        Assert.Equal(new[] { "Вопрос?", "Да", "Нет" }, texts);
    }

    [Fact]
    public void Poll_roundtrip_preserves_id_and_swaps_question_and_options()
    {
        const string doc = """{"type":"doc","content":[{"type":"poll","attrs":{"id":"1","question":"Вопрос?","options":["Да","Нет"]}}]}""";
        var replaced = TipTapTextNodes.ReplaceTexts(doc, ["Question?", "Yes", "No"]);
        Assert.Equal(new[] { "Question?", "Yes", "No" }, TipTapTextNodes.ExtractTexts(replaced));
        Assert.Contains("\"id\":\"1\"", replaced);
    }

    [Fact]
    public void Does_not_extract_structural_or_opaque_attrs()
    {
        var texts = TipTapTextNodes.ExtractTexts(Doc);
        Assert.DoesNotContain("/media/x.png", texts);

        const string other = """
            {"type":"doc","content":[
              {"type":"paragraph","attrs":{"textAlign":"center"},"content":[{"type":"text","marks":[{"type":"link","attrs":{"href":"https://x.test"}}],"text":"link"}]},
              {"type":"blockMath","attrs":{"latex":"x^2"}},
              {"type":"datetime","attrs":{"unix":123,"format":"short"}},
              {"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","attrs":{"colspan":2},"content":[]}]}]},
              {"type":"taskItem","attrs":{"checked":true},"content":[]},
              {"type":"carousel","attrs":{"images":["/a.png","/b.png"]}}
            ]}
            """;
        var otherTexts = TipTapTextNodes.ExtractTexts(other);
        Assert.Equal(new[] { "link" }, otherTexts);
    }

    [Fact]
    public void Replace_roundtrip_preserves_structure_and_swaps_text()
    {
        var texts = TipTapTextNodes.ExtractTexts(Doc);
        var replaced = TipTapTextNodes.ReplaceTexts(Doc, texts.Select(t => t switch
        {
            "Привет" => "Hello",
            "мир" => "world",
            "цитата" => "quote",
            _ => t + "!",
        }).ToList());
        Assert.Contains("\"bold\"", replaced);
        Assert.Contains("/media/x.png", replaced);
    }

    [Fact]
    public void Replace_with_wrong_count_throws()
    {
        Assert.Throws<ArgumentException>(() => TipTapTextNodes.ReplaceTexts(Doc, ["only one"]));
        Assert.Throws<ArgumentException>(() => TipTapTextNodes.ReplaceTexts(Doc, ["1", "2", "3", "4", "5", "6"]));
    }

    [Fact]
    public void Empty_document_extracts_nothing()
    {
        Assert.Empty(TipTapTextNodes.ExtractTexts("""{"type":"doc","content":[]}"""));
    }
}
