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
        Assert.Equal("<p>Привет, <b>мир</b></p>", CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Escapes_user_html()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a < b & <script>"}
                   ]}]}
                   """;
        Assert.Equal("<p>a &lt; b &amp; &lt;script&gt;</p>", CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Nested_marks_close_in_reverse_order()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><b><i>x</i></b></p>", CedarToTelegramHtmlRenderer.Render(json).Text);
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
            CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_heading_as_native_tag()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[
                       {"type":"text","text":"Заголовок"}
                   ]}]}
                   """;
        Assert.Equal("<h2>Заголовок</h2>", CedarToTelegramHtmlRenderer.Render(json).Text);
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
            CedarToTelegramHtmlRenderer.Render(json).Text);
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
            CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_image_with_absolute_media_base_url()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("<img src=\"tg://photo?id=m1\">", result.Text);
        var media = Assert.Single(result.Media);
        Assert.Equal("m1", media.Id);
        Assert.Equal(RichMediaKind.Photo, media.Kind);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/pic.jpg", media.Url);
    }

    [Fact]
    public void Image_inside_list_item_resolves_media_base_url()
    {
        var json = """
                   {"type":"doc","content":[{"type":"bulletList","content":[
                       {"type":"listItem","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}
                   ]}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("<ul><li><img src=\"tg://photo?id=m1\"></li></ul>", result.Text);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/pic.jpg", Assert.Single(result.Media).Url);
    }

    [Fact]
    public void Renders_video()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4"}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("<video src=\"tg://video?id=m1\"></video>", result.Text);
        Assert.Equal(RichMediaKind.Video, Assert.Single(result.Media).Kind);
    }

    [Fact]
    public void Renders_audio()
    {
        var json = """
                   {"type":"doc","content":[{"type":"audio","attrs":{"src":"/media/sound.mp3"}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("<audio src=\"tg://audio?id=m1\"></audio>", result.Text);
        Assert.Equal(RichMediaKind.Audio, Assert.Single(result.Media).Kind);
    }

    [Fact]
    public void Renders_carousel_as_slideshow()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal(
            "<tg-slideshow><img src=\"tg://photo?id=m1\"><img src=\"tg://photo?id=m2\"></tg-slideshow>",
            result.Text);
        Assert.Equal(2, result.Media.Count);
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
            CedarToTelegramHtmlRenderer.Render(json).Text);
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
            CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_block_math_expression()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockMath","attrs":{"latex":"E = mc^2"}}]}
                   """;
        Assert.Equal("<tg-math-block>E = mc^2</tg-math-block>", CedarToTelegramHtmlRenderer.Render(json).Text);
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
        Assert.Equal("<p>формула: <tg-math>a &lt; b</tg-math></p>", CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_spoiler_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"secret","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><tg-spoiler>secret</tg-spoiler></p>", CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_collage_as_tg_collage()
    {
        var json = """
                   {"type":"doc","content":[{"type":"collage","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal(
            "<tg-collage><img src=\"tg://photo?id=m1\"><img src=\"tg://photo?id=m2\"></tg-collage>",
            result.Text);
        Assert.Equal(2, result.Media.Count);
    }

    [Fact]
    public void Renders_toggle_block_as_details()
    {
        var json = """
                   {"type":"doc","content":[{"type":"toggle","attrs":{"summary":"More"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"hidden"}]}
                   ]}]}
                   """;
        Assert.Equal("<details open><summary>More</summary><p>hidden</p></details>", CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_datetime_reference()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1700000000,"format":"wDT"}}
                   ]}]}
                   """;
        Assert.Equal("<p><img src=\"tg://time?unix=1700000000&amp;format=wDT\"></p>", CedarToTelegramHtmlRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_image_caption_as_plain_paragraph_after_the_tag()
    {
        // Verified 16.07.2026 against @testingandfun: InputMediaPhoto.Caption is ignored for
        // inline (non-Blocks) media — the caption must be a plain paragraph instead.
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg","caption":"A caption"}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("<img src=\"tg://photo?id=m1\"><p>A caption</p>", result.Text);
    }

    [Fact]
    public void Renders_video_caption_as_plain_paragraph_after_the_tag()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4","caption":"Video caption"}}]}
                   """;
        var result = CedarToTelegramHtmlRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("<video src=\"tg://video?id=m1\"></video><p>Video caption</p>", result.Text);
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
            CedarToTelegramHtmlRenderer.Render(json).Text);
    }
}