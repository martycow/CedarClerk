using CedarClerk.Core;

namespace CedarClerk.Tests;

public class BlocksRendererTests
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
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichParagraphBlock(new RichRunSequence([
            new RichRunText("Привет, "),
            new RichRunBold(new RichRunText("мир"))
        ]));
        Assert.Equivalent(expected, Assert.Single(blocks), strict: true);
    }

    [Fact]
    public void Nested_marks_wrap_in_array_order_outermost_first()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichParagraphBlock(new RichRunBold(new RichRunItalic(new RichRunText("x"))));
        Assert.Equal(expected, Assert.Single(blocks));
    }

    [Fact]
    public void Renders_underline_strike_code_and_spoiler_marks()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"u","marks":[{"type":"underline"}]},
                       {"type":"text","text":"s","marks":[{"type":"strike"}]},
                       {"type":"text","text":"c","marks":[{"type":"code"}]},
                       {"type":"text","text":"p","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichParagraphBlock(new RichRunSequence([
            new RichRunUnderline(new RichRunText("u")),
            new RichRunStrike(new RichRunText("s")),
            new RichRunCode(new RichRunText("c")),
            new RichRunSpoiler(new RichRunText("p"))
        ]));
        Assert.Equivalent(expected, Assert.Single(blocks), strict: true);
    }

    [Fact]
    public void Renders_link_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"click here","marks":[{"type":"link","attrs":{"href":"https://example.com"}}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichParagraphBlock(new RichRunLink(new RichRunText("click here"), "https://example.com"));
        Assert.Equal(expected, Assert.Single(blocks));
    }

    [Fact]
    public void Renders_heading_with_level()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[
                       {"type":"text","text":"Заголовок"}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(new RichAnchorBlock("zagolovok"), blocks[0]);
        Assert.Equal(new RichHeadingBlock(2, new RichRunText("Заголовок")), blocks[1]);
    }

    [Fact]
    public void Renders_table_of_contents_as_a_list_of_anchor_links_matching_heading_anchors()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"tableOfContents"},
                       {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Intro"}]},
                       {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Details"}]}
                   ]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);

        // toc, anchor+heading, anchor+heading = 5 top-level blocks
        Assert.Equal(5, blocks.Count);
        var toc = Assert.IsType<RichListBlock>(blocks[0]);
        Assert.Equal(2, toc.Items.Count);
        var introLink = Assert.IsType<RichParagraphBlock>(Assert.Single(toc.Items[0].Blocks));
        Assert.Equal(new RichRunAnchorLink(new RichRunText("Intro"), "intro"), introLink.Text);
        var detailsLink = Assert.IsType<RichParagraphBlock>(Assert.Single(toc.Items[1].Blocks));
        Assert.Equal(new RichRunAnchorLink(new RichRunText("Details"), "details"), detailsLink.Text);

        Assert.Equal(new RichAnchorBlock("intro"), blocks[1]);
        Assert.Equal(new RichHeadingBlock(1, new RichRunText("Intro")), blocks[2]);
        Assert.Equal(new RichAnchorBlock("details"), blocks[3]);
        Assert.Equal(new RichHeadingBlock(2, new RichRunText("Details")), blocks[4]);
    }

    [Fact]
    public void Empty_table_of_contents_produces_no_block()
    {
        var json = """{"type":"doc","content":[{"type":"tableOfContents"},{"type":"paragraph","content":[{"type":"text","text":"No headings here"}]}]}""";
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(new RichParagraphBlock(new RichRunText("No headings here")), Assert.Single(blocks));
    }

    [Fact]
    public void Renders_bullet_list_with_no_order_value()
    {
        var json = """
                   {"type":"doc","content":[{"type":"bulletList","content":[
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"раз"}]}]},
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"два"}]}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichListBlock([
            new RichListItem([new RichParagraphBlock(new RichRunText("раз"))], false, false, null),
            new RichListItem([new RichParagraphBlock(new RichRunText("два"))], false, false, null)
        ]);
        Assert.Equivalent(expected, Assert.Single(blocks), strict: true);
    }

    [Fact]
    public void Renders_ordered_list_with_incrementing_order_value()
    {
        var json = """
                   {"type":"doc","content":[{"type":"orderedList","content":[
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"раз"}]}]},
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"два"}]}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var list = Assert.IsType<RichListBlock>(Assert.Single(blocks));
        Assert.Equal(1, list.Items[0].OrderValue);
        Assert.Equal(2, list.Items[1].OrderValue);
    }

    [Fact]
    public void Renders_task_list_with_checkbox_state()
    {
        var json = """
                   {"type":"doc","content":[{"type":"taskList","content":[
                       {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"сделано"}]}]},
                       {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"не сделано"}]}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var list = Assert.IsType<RichListBlock>(Assert.Single(blocks));
        Assert.True(list.Items[0].HasCheckbox);
        Assert.True(list.Items[0].IsChecked);
        Assert.True(list.Items[1].HasCheckbox);
        Assert.False(list.Items[1].IsChecked);
    }

    [Fact]
    public void Renders_code_block_with_language()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"csharp"},"content":[
                       {"type":"text","text":"var x = 1;"}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(new RichCodeBlock("csharp", "var x = 1;"), Assert.Single(blocks));
    }

    [Fact]
    public void Renders_blockquote_as_nested_blocks()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockquote","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"quoted"}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichQuoteBlock([new RichParagraphBlock(new RichRunText("quoted"))]);
        Assert.Equivalent(expected, Assert.Single(blocks), strict: true);
    }

    [Fact]
    public void Renders_horizontal_rule_as_divider()
    {
        var json = """{"type":"doc","content":[{"type":"horizontalRule"}]}""";
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.IsType<RichDividerBlock>(Assert.Single(blocks));
    }

    [Fact]
    public void Renders_image_with_absolute_media_base_url_and_no_caption()
    {
        var json = """{"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg"}}]}""";
        var blocks = CedarToTelegramBlocksRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        var photo = Assert.IsType<RichPhotoBlock>(Assert.Single(blocks));
        Assert.Equal("https://cedarclerk.mooexe.dev/media/pic.jpg", photo.Url);
        Assert.Null(photo.Caption);
    }

    [Fact]
    public void Renders_image_with_caption_as_a_real_caption_run()
    {
        var json = """{"type":"doc","content":[{"type":"image","attrs":{"src":"/media/pic.jpg","caption":"A caption"}}]}""";
        var blocks = CedarToTelegramBlocksRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        var photo = Assert.IsType<RichPhotoBlock>(Assert.Single(blocks));
        Assert.Equal(new RichRunText("A caption"), photo.Caption);
    }

    [Fact]
    public void Renders_video_and_audio_as_dedicated_blocks()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"video","attrs":{"src":"/media/clip.mp4","caption":"Video caption"}},
                       {"type":"audio","attrs":{"src":"/media/sound.mp3"}}
                   ]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        Assert.Equal(2, blocks.Count);
        var video = Assert.IsType<RichVideoBlock>(blocks[0]);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/clip.mp4", video.Url);
        Assert.Equal(new RichRunText("Video caption"), video.Caption);
        var audio = Assert.IsType<RichAudioBlock>(blocks[1]);
        Assert.Equal("https://cedarclerk.mooexe.dev/media/sound.mp3", audio.Url);
    }

    [Fact]
    public void Renders_carousel_as_slideshow_with_resolved_urls()
    {
        var json = """{"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}""";
        var blocks = CedarToTelegramBlocksRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        var slideshow = Assert.IsType<RichSlideshowBlock>(Assert.Single(blocks));
        Assert.Equal(["https://cedarclerk.mooexe.dev/media/a.jpg", "https://cedarclerk.mooexe.dev/media/b.jpg"], slideshow.Urls);
    }

    [Fact]
    public void Renders_collage_with_resolved_urls()
    {
        var json = """{"type":"doc","content":[{"type":"collage","attrs":{"images":["/media/a.jpg","/media/b.jpg"]}}]}""";
        var blocks = CedarToTelegramBlocksRenderer.Render(json, "https://cedarclerk.mooexe.dev");
        var collage = Assert.IsType<RichCollageBlock>(Assert.Single(blocks));
        Assert.Equal(["https://cedarclerk.mooexe.dev/media/a.jpg", "https://cedarclerk.mooexe.dev/media/b.jpg"], collage.Urls);
    }

    [Fact]
    public void Drops_empty_carousel_and_collage_blocks_entirely()
    {
        // Editor artifact (repeated insert/delete) can leave a carousel/collage with zero images —
        // Telegram rejects an empty slideshow/collage with RICH_MESSAGE_CONTENT_REQUIRED (verified
        // 16.07.2026 against @testingandfun), so these must not reach InputRichMessage.Blocks at all.
        var json = """
                   {"type":"doc","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"before"}]},
                       {"type":"carousel","attrs":{"images":[]}},
                       {"type":"collage","attrs":{"images":[]}},
                       {"type":"paragraph","content":[{"type":"text","text":"after"}]}
                   ]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(new RichParagraphBlock(new RichRunText("before")), blocks[0]);
        Assert.Equal(new RichParagraphBlock(new RichRunText("after")), blocks[1]);
    }

    [Fact]
    public void Renders_table_as_rows_of_cells_with_header_and_colspan()
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
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var table = Assert.IsType<RichTableBlock>(Assert.Single(blocks));
        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.Rows[0][0].IsHeader);
        Assert.Equal(2, table.Rows[0][0].Colspan);
        Assert.False(table.Rows[1][0].IsHeader);
        Assert.Equal(new RichRunText("a"), table.Rows[1][0].Text);
        Assert.Equal(new RichRunText("b"), table.Rows[1][1].Text);
    }

    [Fact]
    public void Renders_block_and_inline_math_expressions()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"blockMath","attrs":{"latex":"E = mc^2"}},
                       {"type":"paragraph","content":[
                           {"type":"text","text":"формула: "},
                           {"type":"inlineMath","attrs":{"latex":"a < b"}}
                       ]}
                   ]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(new RichMathBlock("E = mc^2"), blocks[0]);
        var expected = new RichParagraphBlock(new RichRunSequence([
            new RichRunText("формула: "),
            new RichRunMath("a < b")
        ]));
        Assert.Equivalent(expected, blocks[1], strict: true);
    }

    [Fact]
    public void Renders_toggle_as_open_details_block()
    {
        var json = """
                   {"type":"doc","content":[{"type":"toggle","attrs":{"summary":"More"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"hidden"}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var expected = new RichDetailsBlock(new RichRunText("More"), [new RichParagraphBlock(new RichRunText("hidden"))], IsOpen: true);
        Assert.Equivalent(expected, Assert.Single(blocks), strict: true);
    }

    [Fact]
    public void Renders_datetime_as_a_dedicated_rich_text_run()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1700000000,"format":"wDT"}}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        var paragraph = Assert.IsType<RichParagraphBlock>(Assert.Single(blocks));
        var dt = Assert.IsType<RichRunDateTime>(paragraph.Text);
        Assert.Equal(1700000000, dt.UnixSeconds);
        Assert.Equal("wDT", dt.Format);
    }

    [Fact]
    public void Renders_footnote_reference_inline_and_a_footer_block_at_the_end()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"One"},
                       {"type":"footnote","attrs":{"text":"First"}}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(2, blocks.Count);
        Assert.IsType<RichParagraphBlock>(blocks[0]);
        Assert.Equal(new RichFooterBlock(new RichRunText("First")), blocks[1]);
    }

    [Fact]
    public void Ignores_annotation_wrapper_and_renders_the_single_child_block()
    {
        var json = """
                   {"type":"doc","content":[{"type":"annotation","attrs":{"id":"abc"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"marked text"}]}
                   ]}]}
                   """;
        var blocks = CedarToTelegramBlocksRenderer.Render(json);
        Assert.Equal(new RichParagraphBlock(new RichRunText("marked text")), Assert.Single(blocks));
    }
}
