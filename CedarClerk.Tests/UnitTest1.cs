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
        Assert.Equal("<p>Привет, <b>мир</b></p>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Escapes_user_html()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a < b & <script>"}
                   ]}]}
                   """;
        Assert.Equal("<p>a &lt; b &amp; &lt;script&gt;</p>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Nested_marks_close_in_reverse_order()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><b><i>x</i></b></p>", CedarToTelegramHtmlRenderer.Render(json));
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

    [Fact]
    public void Renders_heading_as_native_tag()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[
                       {"type":"text","text":"Заголовок"}
                   ]}]}
                   """;
        Assert.Equal("<h2>Заголовок</h2>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_bullet_list()
    {
        var json = """
                   {"type":"doc","content":[{"type":"bulletList","content":[
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"раз"}]}]},
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"два"}]}]}
                   ]}]}
                   """;
        Assert.Equal("<ul><li><p>раз</p></li><li><p>два</p></li></ul>",
            CedarToTelegramHtmlRenderer.Render(json));
    }
}