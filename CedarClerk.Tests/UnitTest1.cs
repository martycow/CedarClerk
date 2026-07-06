using CedarClerk.Core;

namespace CedarClerk.Tests;

public class RendererTests
{
    [Fact]
    public void Renders_bold_paragraph()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"Привет, "},
                       {"type":"text","text":"мир","marks":[{"type":"bold"}]}
                   ]}]}
                   """;
        Assert.Equal("Привет, <b>мир</b>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Escapes_user_html()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a < b & <script>"}
                   ]}]}
                   """;
        Assert.Equal("a &lt; b &amp; &lt;script&gt;", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Nested_marks_close_in_reverse_order()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("<b><i>x</i></b>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_code_block_with_language()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"csharp"},"content":[
                       {"type":"text","text":"var x = 1;"}
                   ]}]}
                   """;
        Assert.Equal("<pre><code class=\"language-csharp\">var x = 1;</code></pre>",
            CedarToTelegramHtmlRenderer.Render(json));
    }
}