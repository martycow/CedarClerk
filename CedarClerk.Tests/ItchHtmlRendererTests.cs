using CedarClerk.Core;

namespace CedarClerk.Tests;

public class ItchHtmlRendererTests
{
    private const string Base = "https://cedarclerk.mooexe.dev";

    // --- Inline marks ---

    [Fact]
    public void Renders_bold_paragraph()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"Привет, "},
                       {"type":"text","text":"мир","marks":[{"type":"bold"}]}
                   ]}]}
                   """;
        Assert.Equal("<p>Привет, <strong>мир</strong></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_italic_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><em>x</em></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_underline_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"underline"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><u>x</u></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_strike_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"strike"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><s>x</s></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_inline_code_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"code"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><code>x</code></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_link_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.com"}}]}
                   ]}]}
                   """;
        Assert.Equal("<p><a href=\"https://a.com\">x</a></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Javascript_scheme_link_degrades_to_plain_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"click","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]}
                   ]}]}
                   """;
        Assert.Equal("<p>click</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Spoiler_mark_degrades_to_text_marker()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"secret","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        Assert.Equal("<p>(spoiler: secret)</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Nested_marks_close_in_reverse_order()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><strong><em>x</em></strong></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    // --- Escaping (invariant 1) ---

    [Fact]
    public void Escapes_angle_brackets_and_ampersand_in_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a < b & <script>alert(1)</script>"}
                   ]}]}
                   """;
        Assert.Equal("<p>a &lt; b &amp; &lt;script&gt;alert(1)&lt;/script&gt;</p>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Escapes_quotes_in_link_href()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.com/?q=\"evil\""}}]}
                   ]}]}
                   """;
        Assert.Equal("<p><a href=\"https://a.com/?q=&quot;evil&quot;\">x</a></p>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Escapes_html_in_code_block_content()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","content":[
                       {"type":"text","text":"<b>&</b>"}
                   ]}]}
                   """;
        Assert.Equal("<pre><code>&lt;b&gt;&amp;&lt;/b&gt;</code></pre>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Escapes_quotes_in_image_src()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"https://x.com/a\"b.png"}}]}
                   """;
        Assert.Equal("<img src=\"https://x.com/a&quot;b.png\">", CedarToItchHtmlRenderer.Render(json, Base));
    }

    // --- Blocks ---

    [Fact]
    public void Heading_one_demotes_to_h2()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[
                       {"type":"text","text":"Title"}
                   ]}]}
                   """;
        Assert.Equal("<h2>Title</h2>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Heading_three_stays_h3_and_deeper_clamps_to_h3()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"A"}]},
                       {"type":"heading","attrs":{"level":5},"content":[{"type":"text","text":"B"}]}
                   ]}
                   """;
        Assert.Equal("<h3>A</h3><h3>B</h3>", CedarToItchHtmlRenderer.Render(json, Base));
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
        Assert.Equal("<ul><li><p>a</p></li><li><p>b</p></li></ul>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_ordered_list()
    {
        var json = """
                   {"type":"doc","content":[{"type":"orderedList","content":[
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]}
                   ]}]}
                   """;
        Assert.Equal("<ol><li><p>a</p></li></ol>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Task_list_degrades_to_text_checkboxes()
    {
        var json = """
                   {"type":"doc","content":[{"type":"taskList","content":[
                       {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"done"}]}]},
                       {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"todo"}]}]}
                   ]}]}
                   """;
        Assert.Equal("<ul><li>☑ <p>done</p></li><li>☐ <p>todo</p></li></ul>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_code_block()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"csharp"},"content":[
                       {"type":"text","text":"var x = 1;"}
                   ]}]}
                   """;
        Assert.Equal("<pre><code>var x = 1;</code></pre>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_blockquote()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockquote","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"q"}]}
                   ]}]}
                   """;
        Assert.Equal("<blockquote><p>q</p></blockquote>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_horizontal_rule()
    {
        var json = """
                   {"type":"doc","content":[{"type":"horizontalRule"}]}
                   """;
        Assert.Equal("<hr>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_hard_break()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a"},
                       {"type":"hardBreak"},
                       {"type":"text","text":"b"}
                   ]}]}
                   """;
        Assert.Equal("<p>a<br>b</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    // --- Media ---

    [Fact]
    public void Renders_image_with_absolute_url_and_caption()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/a.png","caption":"cap"}}]}
                   """;
        Assert.Equal($"<img src=\"{Base}/media/a.png\"><p><em>cap</em></p>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Gif_video_renders_as_image()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/a.gif"}}]}
                   """;
        Assert.Equal($"<img src=\"{Base}/media/a.gif\">", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Video_degrades_to_link()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"https://x.com/v.mp4"}}]}
                   """;
        Assert.Equal("<p><a href=\"https://x.com/v.mp4\">▶ Video</a></p>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Audio_degrades_to_link_with_title()
    {
        var json = """
                   {"type":"doc","content":[{"type":"audio","attrs":{"src":"/media/a.mp3","title":"Song"}}]}
                   """;
        Assert.Equal($"<p><a href=\"{Base}/media/a.mp3\">Song</a></p>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Youtube_degrades_to_watch_link()
    {
        var json = """
                   {"type":"doc","content":[{"type":"youtube","attrs":{"videoId":"dQw4w9WgXcQ"}}]}
                   """;
        Assert.Equal("<p><a href=\"https://www.youtube.com/watch?v=dQw4w9WgXcQ\">▶ Watch on YouTube</a></p>",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Carousel_renders_sequential_images()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/1.png","/media/2.png"]}}]}
                   """;
        Assert.Equal($"<img src=\"{Base}/media/1.png\"><img src=\"{Base}/media/2.png\">",
            CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Empty_carousel_is_dropped_entirely()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"a"}]},
                       {"type":"carousel","attrs":{"images":[]}},
                       {"type":"paragraph","content":[{"type":"text","text":"b"}]}
                   ]}
                   """;
        Assert.Equal("<p>a</p><p>b</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Empty_collage_is_dropped_entirely()
    {
        var json = """
                   {"type":"doc","content":[{"type":"collage","attrs":{"images":[]}}]}
                   """;
        Assert.Equal("", CedarToItchHtmlRenderer.Render(json, Base));
    }

    // --- Degraded blocks ---

    [Fact]
    public void Table_degrades_to_row_paragraphs()
    {
        var json = """
                   {"type":"doc","content":[{"type":"table","content":[
                       {"type":"tableRow","content":[
                           {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]},
                           {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"B"}]}]}
                       ]},
                       {"type":"tableRow","content":[
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"1"}]}]},
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"2"}]}]}
                       ]}
                   ]}]}
                   """;
        Assert.Equal("<p>A | B</p><p>1 | 2</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_block_math_as_code_block()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockMath","attrs":{"latex":"E=mc^2"}}]}
                   """;
        Assert.Equal("<pre><code>E=mc^2</code></pre>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_inline_math_as_code()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"inlineMath","attrs":{"latex":"x<y"}}
                   ]}]}
                   """;
        Assert.Equal("<p><code>x&lt;y</code></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Toggle_degrades_to_bold_summary_plus_content()
    {
        var json = """
                   {"type":"doc","content":[{"type":"toggle","attrs":{"summary":"More"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"hidden"}]}
                   ]}]}
                   """;
        Assert.Equal("<p><strong>More</strong></p><p>hidden</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_datetime_as_utc_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1755000000,"format":"DT"}}
                   ]}]}
                   """;
        Assert.Equal("<p>12 Aug 2025 12:00 UTC</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Footnote_uses_parenthesized_markers_and_footer()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a"},
                       {"type":"footnote","attrs":{"text":"note <b>"}}
                   ]}]}
                   """;
        Assert.Equal("<p>a(1)</p><hr><p>(1) note &lt;b&gt;</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Wikilink_renders_as_plain_escaped_label()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"wikilink","attrs":{"draftId":"00000000-0000-0000-0000-000000000001","label":"<Home>"}}
                   ]}]}
                   """;
        Assert.Equal("<p>&lt;Home&gt;</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Poll_degrades_to_text_placeholder()
    {
        var json = """
                   {"type":"doc","content":[{"type":"poll","attrs":{"id":"p1","question":"Best?","options":["a","b"]}}]}
                   """;
        Assert.Equal("<p><em>Poll: Best?</em></p>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Poll_without_options_is_dropped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"poll","attrs":{"id":"p1","question":"Best?","options":[]}}]}
                   """;
        Assert.Equal("", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Table_of_contents_is_omitted()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"tableOfContents"},
                       {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Intro"}]}
                   ]}
                   """;
        Assert.Equal("<h2>Intro</h2>", CedarToItchHtmlRenderer.Render(json, Base));
    }

    [Fact]
    public void Unknown_block_renders_children_unwrapped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"annotation","attrs":{"id":"a1"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"x"}]}
                   ]}]}
                   """;
        Assert.Equal("<p>x</p>", CedarToItchHtmlRenderer.Render(json, Base));
    }
}
