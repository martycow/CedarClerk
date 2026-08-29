using CedarClerk.Core;

namespace CedarClerk.Tests;

public class SteamBbcodeRendererTests
{
    private const string Base = "https://cedarclerk.mooexe.dev";

    // --- Inline marks ---

    [Fact]
    public void Renders_bold_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"Привет, "},
                       {"type":"text","text":"мир","marks":[{"type":"bold"}]}
                   ]}]}
                   """;
        Assert.Equal("Привет, [b]мир[/b]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_italic_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("[i]x[/i]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_underline_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"underline"}]}
                   ]}]}
                   """;
        Assert.Equal("[u]x[/u]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_strike_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"strike"}]}
                   ]}]}
                   """;
        Assert.Equal("[strike]x[/strike]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_inline_code_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"code"}]}
                   ]}]}
                   """;
        Assert.Equal("[code]x[/code]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_spoiler_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"secret","marks":[{"type":"spoiler"}]}
                   ]}]}
                   """;
        Assert.Equal("[spoiler]secret[/spoiler]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_link_mark()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.com"}}]}
                   ]}]}
                   """;
        Assert.Equal("[url=https://a.com]x[/url]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Javascript_scheme_link_degrades_to_plain_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"click","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]}
                   ]}]}
                   """;
        Assert.Equal("click", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Link_href_brackets_are_percent_escaped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.com/?q=[1]"}}]}
                   ]}]}
                   """;
        Assert.Equal("[url=https://a.com/?q=%5B1%5D]x[/url]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Nested_marks_first_mark_outermost()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"x","marks":[{"type":"bold"},{"type":"italic"}]}
                   ]}]}
                   """;
        Assert.Equal("[b][i]x[/i][/b]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    // --- BBCode neutralization (escaping invariant) ---

    [Fact]
    public void User_text_with_brackets_is_wrapped_in_noparse()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"[b]not bold[/b]"}
                   ]}]}
                   """;
        Assert.Equal("[noparse][b]not bold[/b][/noparse]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Literal_close_noparse_cannot_break_out()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a[/noparse][b]x"}
                   ]}]}
                   """;
        // The user's "[/noparse]" is split: its "[" gets a one-char noparse, the rest is raw,
        // and the following "[b]x" lands in a fresh noparse — nothing user-typed is ever live.
        Assert.Equal("a[noparse][[/noparse]/noparse][noparse][b]x[/noparse]",
            CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Text_without_open_bracket_is_untouched()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a] plain b"}
                   ]}]}
                   """;
        Assert.Equal("a] plain b", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Bracket_text_inside_marks_stays_neutralized()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"[img]x[/img]","marks":[{"type":"bold"}]}
                   ]}]}
                   """;
        Assert.Equal("[b][noparse][img]x[/img][/noparse][/b]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    // --- Blocks ---

    [Fact]
    public void Renders_heading_levels()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[
                       {"type":"text","text":"Title"}
                   ]}]}
                   """;
        Assert.Equal("[h2]Title[/h2]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Heading_below_h3_clamps_to_h3()
    {
        var json = """
                   {"type":"doc","content":[{"type":"heading","attrs":{"level":5},"content":[
                       {"type":"text","text":"Deep"}
                   ]}]}
                   """;
        Assert.Equal("[h3]Deep[/h3]", CedarToSteamBbcodeRenderer.Render(json, Base));
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
        Assert.Equal("[list]\n[*]a\n[*]b\n[/list]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_ordered_list()
    {
        var json = """
                   {"type":"doc","content":[{"type":"orderedList","content":[
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]}]},
                       {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}
                   ]}]}
                   """;
        Assert.Equal("[olist]\n[*]a\n[*]b\n[/olist]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_task_list_with_text_checkboxes()
    {
        var json = """
                   {"type":"doc","content":[{"type":"taskList","content":[
                       {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"done"}]}]},
                       {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"todo"}]}]}
                   ]}]}
                   """;
        Assert.Equal("[list]\n[*]☑ done\n[*]☐ todo\n[/list]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_code_block()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","attrs":{"language":"csharp"},"content":[
                       {"type":"text","text":"var x = 1;"}
                   ]}]}
                   """;
        Assert.Equal("[code]\nvar x = 1;\n[/code]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Code_block_close_tag_breakout_is_neutralized()
    {
        var json = """
                   {"type":"doc","content":[{"type":"codeBlock","content":[
                       {"type":"text","text":"a[/code][b]b"}
                   ]}]}
                   """;
        Assert.Equal("[code]\na[/code][noparse][/code][/noparse][code][b]b\n[/code]",
            CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_blockquote()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockquote","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"q"}]}
                   ]}]}
                   """;
        Assert.Equal("[quote]\nq\n[/quote]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_horizontal_rule()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"paragraph","content":[{"type":"text","text":"a"}]},
                       {"type":"horizontalRule"},
                       {"type":"paragraph","content":[{"type":"text","text":"b"}]}
                   ]}
                   """;
        Assert.Equal("a\n\n[hr][/hr]\n\nb", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_hard_break_as_newline()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a"},
                       {"type":"hardBreak"},
                       {"type":"text","text":"b"}
                   ]}]}
                   """;
        Assert.Equal("a\nb", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    // --- Media ---

    [Fact]
    public void Renders_image_with_absolute_url_and_caption_line()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"/media/a.png","caption":"cap"}}]}
                   """;
        Assert.Equal($"[img]{Base}/media/a.png[/img]\ncap", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Image_url_brackets_are_percent_escaped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"image","attrs":{"src":"https://x.com/a[1].png"}}]}
                   """;
        Assert.Equal("[img]https://x.com/a%5B1%5D.png[/img]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Video_degrades_to_link()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"https://x.com/v.mp4"}}]}
                   """;
        Assert.Equal("[url=https://x.com/v.mp4]▶ Video[/url]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Gif_video_renders_as_image()
    {
        var json = """
                   {"type":"doc","content":[{"type":"video","attrs":{"src":"/media/a.gif"}}]}
                   """;
        Assert.Equal($"[img]{Base}/media/a.gif[/img]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Audio_degrades_to_link_with_title()
    {
        var json = """
                   {"type":"doc","content":[{"type":"audio","attrs":{"src":"/media/a.mp3","title":"Song"}}]}
                   """;
        Assert.Equal($"[url={Base}/media/a.mp3]Song[/url]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_youtube_as_previewyoutube()
    {
        var json = """
                   {"type":"doc","content":[{"type":"youtube","attrs":{"videoId":"dQw4w9WgXcQ"}}]}
                   """;
        Assert.Equal("[previewyoutube=dQw4w9WgXcQ;full][/previewyoutube]",
            CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Youtube_with_malicious_video_id_is_dropped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"youtube","attrs":{"videoId":"abc;full][img]x"}}]}
                   """;
        Assert.Equal("", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Carousel_renders_sequential_images()
    {
        var json = """
                   {"type":"doc","content":[{"type":"carousel","attrs":{"images":["/media/1.png","/media/2.png"]}}]}
                   """;
        Assert.Equal($"[img]{Base}/media/1.png[/img]\n[img]{Base}/media/2.png[/img]",
            CedarToSteamBbcodeRenderer.Render(json, Base));
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
        Assert.Equal("a\n\nb", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Empty_collage_is_dropped_entirely()
    {
        var json = """
                   {"type":"doc","content":[{"type":"collage","attrs":{"images":[]}}]}
                   """;
        Assert.Equal("", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_table_with_header_and_data_cells()
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
        Assert.Equal("[table]\n[tr][th]A[/th][th]B[/th][/tr]\n[tr][td]1[/td][td]2[/td][/tr]\n[/table]",
            CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    // --- Degraded blocks ---

    [Fact]
    public void Toggle_degrades_to_bold_summary_plus_content()
    {
        var json = """
                   {"type":"doc","content":[{"type":"toggle","attrs":{"summary":"More"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"hidden"}]}
                   ]}]}
                   """;
        Assert.Equal("[b]More[/b]\n\nhidden", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_block_math_as_code()
    {
        var json = """
                   {"type":"doc","content":[{"type":"blockMath","attrs":{"latex":"E=mc^2"}}]}
                   """;
        Assert.Equal("[code]\nE=mc^2\n[/code]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_inline_math_as_dollar_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"inlineMath","attrs":{"latex":"x^2"}}
                   ]}]}
                   """;
        Assert.Equal("$x^2$", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Renders_datetime_as_utc_text()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"datetime","attrs":{"unix":1755000000,"format":"DT"}}
                   ]}]}
                   """;
        Assert.Equal("12 Aug 2025 12:00 UTC", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Footnote_uses_parenthesized_markers_and_footer()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"text","text":"a"},
                       {"type":"footnote","attrs":{"text":"note"}}
                   ]}]}
                   """;
        Assert.Equal("a(1)\n\n(1) note", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Wikilink_renders_as_neutralized_plain_label()
    {
        var json = """
                   {"type":"doc","content":[{"type":"paragraph","content":[
                       {"type":"wikilink","attrs":{"draftId":"00000000-0000-0000-0000-000000000001","label":"[Home]"}}
                   ]}]}
                   """;
        Assert.Equal("[noparse][Home][/noparse]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Poll_degrades_to_text_placeholder()
    {
        var json = """
                   {"type":"doc","content":[{"type":"poll","attrs":{"id":"p1","question":"Best?","options":["a","b"]}}]}
                   """;
        Assert.Equal("[i]Poll: Best?[/i]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Poll_without_options_is_dropped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"poll","attrs":{"id":"p1","question":"Best?","options":[]}}]}
                   """;
        Assert.Equal("", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Table_of_contents_is_omitted()
    {
        var json = """
                   {"type":"doc","content":[
                       {"type":"tableOfContents"},
                       {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Intro"}]}
                   ]}
                   """;
        Assert.Equal("[h1]Intro[/h1]", CedarToSteamBbcodeRenderer.Render(json, Base));
    }

    [Fact]
    public void Unknown_block_renders_children_unwrapped()
    {
        var json = """
                   {"type":"doc","content":[{"type":"annotation","attrs":{"id":"a1"},"content":[
                       {"type":"paragraph","content":[{"type":"text","text":"x"}]}
                   ]}]}
                   """;
        Assert.Equal("x", CedarToSteamBbcodeRenderer.Render(json, Base));
    }
}
