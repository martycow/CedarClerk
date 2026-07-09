using CedarClerk.Core;

namespace CedarClerk.Tests;

public class BlogHtmlRendererTests
{
    private const string Base = "https://cedarclerk.mooexe.dev";

    [Fact]
    public void Renders_bold_paragraph()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"Привет, "},
                       {"type":"text","text":"мир","marks":[{"type":"bold"}]}
                   ]}]}
                   """;
        Assert.Equal("<p>Привет, <strong>мир</strong></p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Escapes_angle_brackets_and_ampersand_in_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a < b & <script>"}
                   ]}]}
                   """;
        Assert.Equal("<p>a &lt; b &amp; &lt;script&gt;</p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Escapes_quotes_in_link_href()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.com/?q=\"evil\""}}]}
                   ]}]}
                   """;
        Assert.Equal(
            "<p><a href=\"https://a.com/?q=&quot;evil&quot;\" rel=\"noopener noreferrer\">x</a></p>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Nested_marks_close_in_reverse_order()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><strong><em>x</em></strong></p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_underline_strike_and_code_marks()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"u","marks":[{"type":"underline"}]},
                       {"type":"text","text":" "},
                       {"type":"text","text":"s","marks":[{"type":"strike"}]},
                       {"type":"text","text":" "},
                       {"type":"text","text":"c","marks":[{"type":"code"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><u>u</u> <s>s</s> <code>c</code></p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_spoiler_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"secret","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><span class=\"spoiler\">secret</span></p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_heading_levels()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[
                       {"type":"text","text":"Title"}
                   ]}]}
                   """;
        Assert.Equal("<h2>Title</h2>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_bullet_list()
    {
        var json = """
                   {"type":"doc","content":[{"type":"bulletList","content":[
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]},
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}
                   ]}]}
                   """;
        Assert.Equal("<ul><li><p>a</p></li><li><p>b</p></li></ul>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_code_block_with_language()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"csharp"},"content":[
                       {"type":"text","text":"var x = 1;"}
                   ]}]}
                   """;
        Assert.Equal("<pre><code class=\"language-csharp\">var x = 1;</code></pre>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_blockquote()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockquote","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"quoted"}]}
                   ]}]}
                   """;
        Assert.Equal("<blockquote><p>quoted</p></blockquote>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_image_with_absolute_media_base_url()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}
                   """;
        Assert.Equal(
            "<img loading=\"lazy\" src=\"https://cedarclerk.mooexe.dev/media/pic.jpg\">",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_image_with_caption_as_figure()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg","caption":"a cat"}}]}
                   """;
        Assert.Equal(
            "<figure><img loading=\"lazy\" src=\"https://cedarclerk.mooexe.dev/media/pic.jpg\"><figcaption>a cat</figcaption></figure>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_video_with_controls()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4"}}]}
                   """;
        Assert.Equal(
            "<video controls src=\"https://cedarclerk.mooexe.dev/media/clip.mp4\"></video>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_audio_with_controls()
    {
        var json = """
                   {"type":"doc","content":[{"type":"audio","attrs":{"src":"/media/clip.mp3"}}]}
                   """;
        Assert.Equal(
            "<audio controls src=\"https://cedarclerk.mooexe.dev/media/clip.mp3\"></audio>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_carousel_as_slideshow_with_controls_for_multiple_images()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        Assert.Equal(
            "<div class=\"carousel\">" +
            "<div class=\"carousel-viewport\">" +
            "<img loading=\"lazy\" src=\"https://cedarclerk.mooexe.dev/media/a.jpg\">" +
            "<img loading=\"lazy\" src=\"https://cedarclerk.mooexe.dev/media/b.jpg\">" +
            "</div>" +
            "<button type=\"button\" class=\"carousel-prev\" aria-label=\"Previous\">&#8249;</button>" +
            "<button type=\"button\" class=\"carousel-next\" aria-label=\"Next\">&#8250;</button>" +
            "<div class=\"carousel-dots\">" +
            "<button type=\"button\" class=\"carousel-dot\" aria-label=\"Slide 1\"></button>" +
            "<button type=\"button\" class=\"carousel-dot\" aria-label=\"Slide 2\"></button>" +
            "</div>" +
            "</div>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_carousel_without_controls_for_single_image()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/a.jpg"]}}]}
                   """;
        Assert.Equal(
            "<div class=\"carousel\">" +
            "<div class=\"carousel-viewport\">" +
            "<img loading=\"lazy\" src=\"https://cedarclerk.mooexe.dev/media/a.jpg\">" +
            "</div>" +
            "</div>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_collage_as_div_of_images()
    {
        var json = """
                   {"type":"doc","content":[{"type":"collage","attrs":{"images":["/media/a.jpg"]}}]}
                   """;
        Assert.Equal(
            "<div class=\"collage\"><img loading=\"lazy\" src=\"https://cedarclerk.mooexe.dev/media/a.jpg\"></div>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_table_with_colspan()
    {
        var json = """
                   {"type":"doc","content":[{"type":"table","content":[
                       {"type":"tableRow","content":[
                           {"type":"tableHeader","attrs":{"colspan":2},"content":[{"type":"paragraph","content":[{"type":"text","text":"h"}]}]}
                       ]},
                       {"type":"tableRow","content":[
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"c"}]}]}
                       ]}
                   ]}]}
                   """;
        Assert.Equal(
            "<table><tr><th colspan=\"2\">h</th></tr><tr><td>c</td></tr></table>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_task_list_with_checked_and_unchecked_items()
    {
        var json = """
                   {"type":"doc","content":[{"type":"taskList","content":[
                       {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"done"}]}]},
                       {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"todo"}]}]}
                   ]}]}
                   """;
        Assert.Equal(
            "<ul class=\"task-list\">" +
            "<li><input type=\"checkbox\" disabled checked> <p>done</p></li>" +
            "<li><input type=\"checkbox\" disabled> <p>todo</p></li>" +
            "</ul>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_toggle_block_as_details()
    {
        var json = """
                   {"type":"doc","content":[{"type":"toggle","attrs":{"summary":"More"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"hidden"}]}
                   ]}]}
                   """;
        Assert.Equal("<details><summary>More</summary><p>hidden</p></details>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_block_math_as_katex_target_div()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockMath","attrs":{"latex":"a < b"}}]}
                   """;
        Assert.Equal(
            "<div class=\"math-tex\" data-display=\"true\">a &lt; b</div>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_inline_math_as_katex_target_span()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"inlineMath","attrs":{"latex":"x^2"}}
                   ]}]}
                   """;
        Assert.Equal(
            "<p><span class=\"math-tex\" data-display=\"false\">x^2</span></p>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_datetime_as_time_element()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1700000000,"format":"D"}}
                   ]}]}
                   """;
        var result = CedarToBlogHtmlRenderer.Render(json, Base);
        Assert.Contains("<time datetime=\"2023-11-14T22:13:20Z\">", result);
        Assert.Contains("14 Nov 2023", result);
    }

    [Fact]
    public void Renders_footnote_references_and_collected_footer()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a"},
                       {"type":"footnote","attrs":{"text":"first note"}},
                       {"type":"text","text":"b"},
                       {"type":"footnote","attrs":{"text":"second note"}}
                   ]}]}
                   """;
        Assert.Equal(
            "<p>a<sup><a href=\"#fn-1\">[1]</a></sup>b<sup><a href=\"#fn-2\">[2]</a></sup></p>" +
            "<section class=\"footnotes\"><hr><ol><li id=\"fn-1\">first note</li><li id=\"fn-2\">second note</li></ol></section>",
            CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Separates_sibling_blocks_with_no_extra_whitespace()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"one"}]},
                       {"type":"paragraph","content":[{"type":"text","text":"two"}]}
                   ]}
                   """;
        Assert.Equal("<p>one</p><p>two</p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_annotation_wrapper_with_id_and_children()
    {
        var json = """
                   {"type":"doc","content":[{"type":"annotation","attrs":{"id":"abc-123"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"marked text"}]}
                   ]}]}
                   """;
        var result = CedarToBlogHtmlRenderer.Render(json, Base);
        Assert.Contains("<div class=\"annotation\" data-annotation-id=\"abc-123\">", result);
        Assert.Contains("<p>marked text</p>", result);
        Assert.Contains("class=\"annotation-controls\"", result);
        Assert.Contains("data-kind=\"like\"", result);
        Assert.Contains("data-kind=\"dislike\"", result);
    }

    [Fact]
    public void Escapes_annotation_id_in_attribute()
    {
        var json = """
                   {"type":"doc","content":[{"type":"annotation","attrs":{"id":"a\"b"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"x"}]}
                   ]}]}
                   """;
        Assert.Contains("data-annotation-id=\"a&quot;b\"", CedarToBlogHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_horizontal_rule_and_hard_break()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"horizontalRule"},
                       {"type":"paragraph","content":[{"type":"text","text":"a"},{"type":"hardBreak"},{"type":"text","text":"b"}]}
                   ]}
                   """;
        Assert.Equal("<hr><p>a<br>b</p>", CedarToBlogHtmlRenderer.Render(json, Base));
    }
}
