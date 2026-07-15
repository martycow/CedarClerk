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

    [Fact]
    public void Renders_nested_bullet_list()
    {
        var json = """
                   {"type":"doc","content":[{"type":"bulletList","content":[
                       {"type":"listItem","content":[
                           {"type":"paragraph","content":[{"type":"text","text":"раз"}]},
                           {"type":"bulletList","content":[
                               {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"раз.один"}]}]}
                           ]}
                       ]}
                   ]}]}
                   """;
        Assert.Equal("<ul><li><p>раз</p><ul><li><p>раз.один</p></li></ul></li></ul>",
            CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_image_with_absolute_media_base_url()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}
                   """;
        Assert.Equal("<photo src=\"https://cedarclerk.mooexe.dev/media/pic.jpg\">",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Image_inside_list_item_resolves_media_base_url()
    {
        var json = """
                   {"type":"doc","content":[{"type":"bulletList","content":[
                       {"type":"listItem","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}
                   ]}]}
                   """;
        Assert.Equal("<ul><li><photo src=\"https://cedarclerk.mooexe.dev/media/pic.jpg\"></li></ul>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_video()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4"}}]}
                   """;
        Assert.Equal("<video src=\"https://cedarclerk.mooexe.dev/media/clip.mp4\"></video>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_audio()
    {
        var json = """
                   {"type":"doc","content":[{"type":"audio","attrs":{"src":"/media/sound.mp3"}}]}
                   """;
        Assert.Equal("<audio src=\"https://cedarclerk.mooexe.dev/media/sound.mp3\"></audio>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_carousel_as_slideshow()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        Assert.Equal(
            "<tg-slideshow><photo src=\"https://cedarclerk.mooexe.dev/media/a.jpg\">" +
            "<photo src=\"https://cedarclerk.mooexe.dev/media/b.jpg\"></tg-slideshow>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_table_with_header_and_colspan()
    {
        var json = """
                   {"type":"doc","content":[{"type":"table","content":[
                       {"type":"tableRow","content":[
                           {"type":"tableHeader","attrs":{"colspan":2},"content":[{"type":"paragraph","content":[{"type":"text","text":"Заголовок"}]}]}
                       ]},
                       {"type":"tableRow","content":[
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]},
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}
                       ]}
                   ]}]}
                   """;
        Assert.Equal(
            "<table><tr><th colspan=\"2\">Заголовок</th></tr><tr><td>a</td><td>b</td></tr></table>",
            CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_task_list_with_checked_and_unchecked_items()
    {
        var json = """
                   {"type":"doc","content":[{"type":"taskList","content":[
                       {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"сделано"}]}]},
                       {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"не сделано"}]}]}
                   ]}]}
                   """;
        Assert.Equal(
            "<ul><li><input type=\"checkbox\" checked><p>сделано</p></li>" +
            "<li><input type=\"checkbox\"><p>не сделано</p></li></ul>",
            CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_block_math_expression()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockMath","attrs":{"latex":"E = mc^2"}}]}
                   """;
        Assert.Equal("<tg-math-block>E = mc^2</tg-math-block>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_inline_math_expression_and_escapes_it()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"формула: "},
                       {"type":"inlineMath","attrs":{"latex":"a < b"}}
                   ]}]}
                   """;
        Assert.Equal("<p>формула: <tg-math>a &lt; b</tg-math></p>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_spoiler_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"secret","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><tg-spoiler>secret</tg-spoiler></p>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_collage_as_tg_collage()
    {
        var json = """
                   {"type":"doc","content":[{"type":"collage","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        Assert.Equal(
            "<tg-collage><photo src=\"https://cedarclerk.mooexe.dev/media/a.jpg\">" +
            "<photo src=\"https://cedarclerk.mooexe.dev/media/b.jpg\"></tg-collage>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_toggle_block_as_details()
    {
        var json = """
                   {"type":"doc","content":[{"type":"toggle","attrs":{"summary":"More"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"hidden"}]}
                   ]}]}
                   """;
        Assert.Equal("<details open><summary>More</summary><p>hidden</p></details>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_datetime_reference()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1700000000,"format":"wDT"}}
                   ]}]}
                   """;
        Assert.Equal("<p><img src=\"tg://time?unix=1700000000&amp;format=wDT\"></p>", CedarToTelegramHtmlRenderer.Render(json));
    }

    [Fact]
    public void Renders_image_with_caption_as_figure()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg","caption":"A caption"}}]}
                   """;
        Assert.Equal(
            "<figure><photo src=\"https://cedarclerk.mooexe.dev/media/pic.jpg\"><figcaption>A caption</figcaption></figure>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_video_with_caption_as_figure()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4","caption":"Video caption"}}]}
                   """;
        Assert.Equal(
            "<figure><video src=\"https://cedarclerk.mooexe.dev/media/clip.mp4\"></video><figcaption>Video caption</figcaption></figure>",
            CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev"));
    }

    [Fact]
    public void Renders_footnote_references_and_collected_footer()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"One"},
                       {"type":"footnote","attrs":{"text":"First"}},
                       {"type":"text","text":" Two"},
                       {"type":"footnote","attrs":{"text":"Second"}}
                   ]}]}
                   """;
        Assert.Equal(
            "<p>One<sup>[1]</sup> Two<sup>[2]</sup></p><hr><ol><li>First</li><li>Second</li></ol>",
            CedarToTelegramHtmlRenderer.Render(json));
    }
}