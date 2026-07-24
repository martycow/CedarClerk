using System.Text.Json.Nodes;
using CedarClerk.Core;

namespace CedarClerk.Tests;

public class MarkdownToCedarConverterTests
{
    private static JsonObject ConvertToDoc(string markdown, out string? title)
    {
        var json = MarkdownToCedarConverter.Convert(markdown, out title);
        return (JsonObject)JsonNode.Parse(json)!;
    }

    private static JsonArray Content(JsonNode doc) => (JsonArray)doc["content"]!;

    [Fact]
    public void Parses_heading_levels_and_extracts_title()
    {
        var doc = ConvertToDoc("# Title\n\n## Subtitle", out var title);
        var blocks = Content(doc);

        Assert.Equal("heading", blocks[0]!["type"]!.GetValue<string>());
        Assert.Equal(1, blocks[0]!["attrs"]!["level"]!.GetValue<int>());
        Assert.Equal("Title", blocks[0]!["content"]![0]!["text"]!.GetValue<string>());

        Assert.Equal("heading", blocks[1]!["type"]!.GetValue<string>());
        Assert.Equal(2, blocks[1]!["attrs"]!["level"]!.GetValue<int>());
        Assert.Equal("Title", title);
    }

    [Fact]
    public void Parses_plain_paragraph()
    {
        var blocks = Content(ConvertToDoc("Just some text.", out _));
        Assert.Equal("paragraph", blocks[0]!["type"]!.GetValue<string>());
        Assert.Equal("Just some text.", blocks[0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_bullet_list()
    {
        var blocks = Content(ConvertToDoc("- one\n- two", out _));
        Assert.Equal("bulletList", blocks[0]!["type"]!.GetValue<string>());
        var items = (JsonArray)blocks[0]!["content"]!;
        Assert.Equal(2, items.Count);
        Assert.Equal("listItem", items[0]!["type"]!.GetValue<string>());
        Assert.Equal("one", items[0]!["content"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_ordered_list()
    {
        var blocks = Content(ConvertToDoc("1. first\n2. second", out _));
        Assert.Equal("orderedList", blocks[0]!["type"]!.GetValue<string>());
        Assert.Equal(2, ((JsonArray)blocks[0]!["content"]!).Count);
    }

    [Fact]
    public void Parses_task_list_with_checked_state()
    {
        var blocks = Content(ConvertToDoc("- [ ] todo\n- [x] done", out _));
        Assert.Equal("taskList", blocks[0]!["type"]!.GetValue<string>());
        var items = (JsonArray)blocks[0]!["content"]!;
        Assert.Equal("taskItem", items[0]!["type"]!.GetValue<string>());
        Assert.False(items[0]!["attrs"]!["checked"]!.GetValue<bool>());
        Assert.True(items[1]!["attrs"]!["checked"]!.GetValue<bool>());
    }

    [Fact]
    public void Parses_nested_bullet_list_by_indent()
    {
        var blocks = Content(ConvertToDoc("- top\n  - nested", out _));
        var topItem = ((JsonArray)blocks[0]!["content"]!)[0]!;
        var nestedList = topItem["content"]![1]!;
        Assert.Equal("bulletList", nestedList!["type"]!.GetValue<string>());
        Assert.Equal("nested", nestedList["content"]![0]!["content"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_image_with_alt_text_and_url_decodes_basename()
    {
        var blocks = Content(ConvertToDoc("![My caption](Sub%20Folder/My%20Image.png)", out _));
        Assert.Equal("image", blocks[0]!["type"]!.GetValue<string>());
        Assert.Equal("/media/My Image.png", blocks[0]!["attrs"]!["src"]!.GetValue<string>());
        Assert.Equal("My caption", blocks[0]!["attrs"]!["alt"]!.GetValue<string>());
        Assert.Equal("My caption", blocks[0]!["attrs"]!["caption"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_blockquote()
    {
        var blocks = Content(ConvertToDoc("> A quote", out _));
        Assert.Equal("blockquote", blocks[0]!["type"]!.GetValue<string>());
        Assert.Equal("A quote", blocks[0]!["content"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_fenced_code_block()
    {
        var blocks = Content(ConvertToDoc("```\nvar x = 1;\n```", out _));
        Assert.Equal("codeBlock", blocks[0]!["type"]!.GetValue<string>());
        Assert.Equal("var x = 1;", blocks[0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_horizontal_rule()
    {
        var blocks = Content(ConvertToDoc("---", out _));
        Assert.Equal("horizontalRule", blocks[0]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("**bold**", "bold")]
    [InlineData("__bold__", "bold")]
    [InlineData("*italic*", "italic")]
    [InlineData("_italic_", "italic")]
    [InlineData("~~strike~~", "strike")]
    [InlineData("`code`", "code")]
    public void Parses_inline_marks(string markdown, string expectedMarkType)
    {
        var blocks = Content(ConvertToDoc(markdown, out _));
        var textNode = blocks[0]!["content"]![0]!;
        Assert.Equal(expectedMarkType, textNode["marks"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Parses_inline_link()
    {
        var blocks = Content(ConvertToDoc("[Cedar Clerk](https://example.com)", out _));
        var textNode = blocks[0]!["content"]![0]!;
        Assert.Equal("Cedar Clerk", textNode["text"]!.GetValue<string>());
        Assert.Equal("link", textNode["marks"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("https://example.com", textNode["marks"]![0]!["attrs"]!["href"]!.GetValue<string>());
    }

    [Fact]
    public void Unsupported_construct_degrades_to_plain_paragraph_without_throwing()
    {
        var blocks = Content(ConvertToDoc("| a | b |\n| - | - |\n| 1 | 2 |", out _));
        Assert.All(blocks, b => Assert.Equal("paragraph", b!["type"]!.GetValue<string>()));
    }

    [Fact]
    public void No_heading_leaves_title_null()
    {
        ConvertToDoc("Just text, no heading.", out var title);
        Assert.Null(title);
    }
}
