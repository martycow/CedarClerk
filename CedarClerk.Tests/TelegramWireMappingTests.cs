using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Server.Publishing;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Tests;

// Found in production 01.08.2026: a post containing a table failed with
//   Publish failed: JsonException: Can't serialize value 0 for enum RichBlockTableCellAlign
// `RichBlockTableCell.Align` and `.Valign` are non-nullable enums whose members start at 1, so
// leaving them unset serialised as 0 and the client refused to send. Tables had therefore never
// published successfully — the renderer's unit tests covered the Core tree (renderers.md
// invariant 2) but nothing covered the step after it, the mapping onto Telegram's wire types.
//
// These tests close that gap for every block type by serialising with the client's OWN options
// (`JsonBotAPI.Options`), which is what turns "looks right" into "the library accepts it".
public class TelegramWireMappingTests
{
    private static string Serialize(CedarRichBlock block)
    {
        var message = new InputRichMessage { Blocks = [TelegramPublishTarget.ToInputRichBlock(block)] };
        return JsonSerializer.Serialize(message, JsonBotAPI.Options);
    }

    private static RichRun Text(string text) => new RichRunText(text);

    public static TheoryData<string, CedarRichBlock> EveryBlockType() => new()
    {
        { "paragraph", new RichParagraphBlock(Text("hello")) },
        { "heading", new RichHeadingBlock(2, Text("title")) },
        { "list", new RichListBlock([new RichListItem([new RichParagraphBlock(Text("item"))], false, false, null)]) },
        { "checklist", new RichListBlock([new RichListItem([new RichParagraphBlock(Text("done"))], true, true, null)]) },
        { "code", new RichCodeBlock("var x = 1;", "csharp") },
        { "quote", new RichQuoteBlock([new RichParagraphBlock(Text("quoted"))]) },
        { "divider", new RichDividerBlock() },
        { "photo", new RichPhotoBlock("https://example.com/a.jpg", Text("caption")) },
        { "video", new RichVideoBlock("https://example.com/a.mp4", Text("caption")) },
        { "audio", new RichAudioBlock("https://example.com/a.mp3", Text("caption"), "Track") },
        { "slideshow", new RichSlideshowBlock(["https://example.com/a.jpg", "https://example.com/b.jpg"]) },
        { "collage", new RichCollageBlock(["https://example.com/a.jpg", "https://example.com/b.jpg"]) },
        { "math", new RichMathBlock("x^2 + y^2 = z^2") },
        { "details", new RichDetailsBlock(Text("summary"), [new RichParagraphBlock(Text("body"))], true) },
        { "footer", new RichFooterBlock(Text("footer")) },
        { "anchor", new RichAnchorBlock("section-1") },
        // The one that actually broke.
        { "table", new RichTableBlock([[
            new RichTableCell(Text("Header"), true, null, null),
            new RichTableCell(Text("Cell"), false, 2, 1),
        ]]) },
    };

    [Theory]
    [MemberData(nameof(EveryBlockType))]
    public void Every_block_type_survives_the_client_serializer(string name, CedarRichBlock block)
    {
        var json = Serialize(block);

        Assert.False(string.IsNullOrWhiteSpace(json), $"{name} serialized to nothing");
        // A 0 where an enum member is expected is the exact shape of the production failure.
        Assert.DoesNotContain("\"align\":0", json);
        Assert.DoesNotContain("\"valign\":0", json);
    }

    [Fact]
    public void Table_cells_carry_a_real_alignment()
    {
        var json = Serialize(new RichTableBlock([[new RichTableCell(Text("Cell"), false, null, null)]]));

        Assert.Contains("left", json);
        Assert.Contains("middle", json);
    }

    [Fact]
    public void Inline_runs_survive_the_client_serializer()
    {
        RichRun[] runs =
        [
            new RichRunText("plain"),
            new RichRunBold(new RichRunText("bold")),
            new RichRunItalic(new RichRunText("italic")),
            new RichRunUnderline(new RichRunText("underline")),
            new RichRunStrike(new RichRunText("strike")),
            new RichRunCode(new RichRunText("code")),
            new RichRunSpoiler(new RichRunText("spoiler")),
            new RichRunLink(new RichRunText("link"), "https://example.com"),
            new RichRunMath("e^{i\\pi}"),
            new RichRunAnchorLink(new RichRunText("jump"), "section-1"),
            new RichRunDateTime("2026-08-01", 1_754_000_000, "date"),
            new RichRunSequence([new RichRunText("a"), new RichRunBold(new RichRunText("b"))]),
        ];

        foreach (var run in runs)
        {
            var json = Serialize(new RichParagraphBlock(run));
            Assert.False(string.IsNullOrWhiteSpace(json), $"{run.GetType().Name} serialized to nothing");
        }
    }
}
