using CedarClerk.Core;

namespace CedarClerk.Tests;

public class MarkdownRendererTests
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
        Assert.Equal("Привет, **мир**", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Does_not_escape_angle_brackets_or_ampersand()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a < b & <script>"}
                   ]}]}
                   """;
        Assert.Equal("a < b & <script>", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Escapes_markdown_special_characters_in_plain_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a*b_c~d|e`f[g]h"}
                   ]}]}
                   """;
        Assert.Equal("""a\*b\_c\~d\|e\`f\[g\]h""", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Nested_marks_close_in_reverse_order()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("**_x_**", CedarToTelegramMarkdownRenderer.Render(json).Text);
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
        Assert.Equal("<u>u</u> ~~s~~ `c`", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_link_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"click here","marks":[{"type":"link","attrs":{"href":"https://example.com"}}]}
                   ]}]}
                   """;
        Assert.Equal("[click here](https://example.com)", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_code_block_with_language()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"csharp"},"content":[
                       {"type":"text","text":"var x = 1;"}
                   ]}]}
                   """;
        Assert.Equal("```csharp\nvar x = 1;\n```", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_heading_as_hashes()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[
                       {"type":"text","text":"Заголовок"}
                   ]}]}
                   """;
        Assert.Equal("## Заголовок", CedarToTelegramMarkdownRenderer.Render(json).Text);
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
        Assert.Equal("- раз\n- два", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_nested_bullet_list_with_indentation()
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
        Assert.Equal("- раз\n  - раз.один", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_image_with_absolute_media_base_url()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("![](tg://photo?id=m1)", result.Text);
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
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("- ![](tg://photo?id=m1)", result.Text);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/pic.jpg", Assert.Single(result.Media).Url);
    }

    [Fact]
    public void Renders_video()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4"}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("![](tg://video?id=m1)", result.Text);
        var media = Assert.Single(result.Media);
        Assert.Equal(RichMediaKind.Video, media.Kind);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/clip.mp4", media.Url);
    }

    [Fact]
    public void Renders_audio()
    {
        var json = """
                   {"type":"doc","content":[{"type":"audio","attrs":{"src":"/media/sound.mp3"}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("![](tg://audio?id=m1)", result.Text);
        var media = Assert.Single(result.Media);
        Assert.Equal(RichMediaKind.Audio, media.Kind);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/sound.mp3", media.Url);
    }

    [Fact]
    public void Renders_carousel_as_slideshow()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal(
            "<tg-slideshow>\n\n![](tg://photo?id=m1)\n![](tg://photo?id=m2)\n\n</tg-slideshow>",
            result.Text);
        Assert.Equal(2, result.Media.Count);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/a.jpg", result.Media[0].Url);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/b.jpg", result.Media[1].Url);
    }

    [Fact]
    public void Renders_table()
    {
        var json = """
                   {"type":"doc","content":[{"type":"table","content":[
                       {"type":"tableRow","content":[
                           {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]},
                           {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}
                       ]},
                       {"type":"tableRow","content":[
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"1"}]}]},
                           {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"2"}]}]}
                       ]}
                   ]}]}
                   """;
        Assert.Equal("| a | b |\n| --- | --- |\n| 1 | 2 |", CedarToTelegramMarkdownRenderer.Render(json).Text);
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
        Assert.Equal("- [x] сделано\n- [ ] не сделано", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_block_math_expression()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockMath","attrs":{"latex":"E = mc^2"}}]}
                   """;
        Assert.Equal("```math\nE = mc^2\n```", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_inline_math_expression_without_escaping_latex()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"формула: "},
                       {"type":"inlineMath","attrs":{"latex":"a < b"}}
                   ]}]}
                   """;
        Assert.Equal("формула: $a < b$", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Separates_paragraphs_with_a_blank_line()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"first"}]},
                       {"type":"paragraph","content":[{"type":"text","text":"second"}]}
                   ]}
                   """;
        Assert.Equal("first\n\nsecond", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_blockquote_with_prefix_on_every_line()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockquote","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"line one"}]},
                       {"type":"paragraph","content":[{"type":"text","text":"line two"}]}
                   ]}]}
                   """;
        Assert.Equal("> line one\n> \n> line two", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_spoiler_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"secret","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        Assert.Equal("||secret||", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_collage_as_tg_collage()
    {
        var json = """
                   {"type":"doc","content":[{"type":"collage","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal(
            "<tg-collage>\n\n![](tg://photo?id=m1)\n![](tg://photo?id=m2)\n\n</tg-collage>",
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
        Assert.Equal("<details open><summary>More</summary>\n\nhidden\n\n</details>", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_datetime_reference()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1700000000,"format":"wDT"}}
                   ]}]}
                   """;
        Assert.Equal("![](tg://time?unix=1700000000&format=wDT)", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Renders_image_caption_as_plain_text_after_the_reference()
    {
        // Verified 16.07.2026 against @testingandfun: InputMediaPhoto.Caption is ignored for
        // inline (non-Blocks) media — the caption must be plain flowing text instead.
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg","caption":"A caption"}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("![](tg://photo?id=m1)\nA caption", result.Text);
    }

    [Fact]
    public void Renders_video_caption_as_plain_text_after_the_reference()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/clip.mp4","caption":"Video caption"}}]}
                   """;
        var result = CedarToTelegramMarkdownRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal("![](tg://video?id=m1)\nVideo caption", result.Text);
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
            "One[^1] Two[^2]\n\n[^1]: First\n[^2]: Second",
            CedarToTelegramMarkdownRenderer.Render(json).Text);
    }

    [Fact]
    public void Ignores_annotation_wrapper_and_renders_children()
    {
        // Telegram has no concept of anchored reactions/comments — an "annotation" node (used to
        // mark a region for likes/comments on the blog) has no case here and falls through to the
        // default "unknown type" handling, rendering its children as if unwrapped.
        var json = """
                   {"type":"doc","content":[{"type":"annotation","attrs":{"id":"abc"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"marked text"}]}
                   ]}]}
                   """;
        Assert.Equal("marked text", CedarToTelegramMarkdownRenderer.Render(json).Text);
    }
}
