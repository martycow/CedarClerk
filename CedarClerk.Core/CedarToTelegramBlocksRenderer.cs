using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// Telegram Bot API 10.2 structured Rich Message renderer (InputRichMessage.Blocks). Verified
// 16.07.2026 against @testingandfun: this is the ONLY mechanism that reliably embeds media (the
// text-based Markdown/Html modes + InputRichMessage.Media/tg://{kind}?id= references silently drop
// the media) and the only one that gives photo/video/audio a real, natively-styled caption —
// CedarToTelegramMarkdownRenderer/CedarToTelegramHtmlRenderer are no longer used for sending to
// Telegram (see PostEndpoints.PublishAsync) but are kept as-is; see docs/DECISIONS.md.
public static class CedarToTelegramBlocksRenderer
{
    private sealed class RenderContext
    {
        public string? MediaBaseUrl;
        public List<string> Footnotes { get; } = [];
    }

    public static IReadOnlyList<CedarRichBlock> Render(string cedarJson, string? mediaBaseUrl = null)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid cedar JSON");
        var doc = root["doc"] ?? root;
        var ctx = new RenderContext { MediaBaseUrl = mediaBaseUrl };
        var blocks = RenderBlocks(doc["content"]?.AsArray(), ctx).ToList();

        if (ctx.Footnotes.Count > 0)
            blocks.AddRange(ctx.Footnotes.Select(t => new RichFooterBlock(new RichRunText(t))));

        return blocks;
    }

    private static IEnumerable<CedarRichBlock> RenderBlocks(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            yield break;

        foreach (var n in nodes)
        {
            var block = RenderBlock(n!, ctx);
            if (block is not null)
                yield return block;
        }
    }

    private static CedarRichBlock? RenderBlock(JsonNode node, RenderContext ctx)
    {
        switch ((string?)node["type"])
        {
            case "paragraph":
                return new RichParagraphBlock(RenderInline(node["content"]?.AsArray(), ctx));

            case "heading":
                var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 1, 6);
                return new RichHeadingBlock(level, RenderInline(node["content"]?.AsArray(), ctx));

            case "bulletList":
                return new RichListBlock(RenderListItems(node["content"]?.AsArray(), ctx, ordered: false));

            case "orderedList":
                return new RichListBlock(RenderListItems(node["content"]?.AsArray(), ctx, ordered: true));

            case "taskList":
                return new RichListBlock(RenderTaskItems(node["content"]?.AsArray(), ctx));

            case "codeBlock":
                var lang = (string?)node["attrs"]?["language"];
                return new RichCodeBlock(lang, RenderRawText(node["content"]?.AsArray()));

            case "blockquote":
                return new RichQuoteBlock(RenderBlocks(node["content"]?.AsArray(), ctx).ToList());

            case "horizontalRule":
                return new RichDividerBlock();

            case "image":
                var imgUrl = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                return new RichPhotoBlock(imgUrl, CaptionRun((string?)node["attrs"]?["caption"]));

            case "video":
                var videoUrl = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                return new RichVideoBlock(videoUrl, CaptionRun((string?)node["attrs"]?["caption"]));

            case "audio":
                var audioUrl = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                return new RichAudioBlock(audioUrl, CaptionRun((string?)node["attrs"]?["caption"]));

            case "carousel":
                var slideUrls = node["attrs"]?["images"]?.AsArray()
                    ?.Select(img => ResolveUrl((string?)img, ctx.MediaBaseUrl)).ToList() ?? [];
                return new RichSlideshowBlock(slideUrls);

            case "collage":
                var collageUrls = node["attrs"]?["images"]?.AsArray()
                    ?.Select(img => ResolveUrl((string?)img, ctx.MediaBaseUrl)).ToList() ?? [];
                return new RichCollageBlock(collageUrls);

            case "table":
                return RenderTable(node["content"]?.AsArray(), ctx);

            case "blockMath":
                return new RichMathBlock((string?)node["attrs"]?["latex"] ?? "");

            case "toggle":
                var summary = (string?)node["attrs"]?["summary"] ?? "";
                return new RichDetailsBlock(new RichRunText(summary), RenderBlocks(node["content"]?.AsArray(), ctx).ToList(), IsOpen: true);

            default:
                // Unknown block type (e.g. blog-only "annotation") — render children unwrapped,
                // matching the string renderers' fallback behavior.
                var children = RenderBlocks(node["content"]?.AsArray(), ctx).ToList();
                return children.Count switch
                {
                    0 => null,
                    1 => children[0],
                    _ => new RichQuoteBlock(children) // best-effort: no "fragment" block exists
                };
        }
    }

    private static RichRun? CaptionRun(string? caption) =>
        string.IsNullOrEmpty(caption) ? null : new RichRunText(caption);

    private static List<RichListItem> RenderListItems(JsonArray? items, RenderContext ctx, bool ordered)
    {
        if (items is null)
            return [];

        var result = new List<RichListItem>();
        var index = 1;
        foreach (var item in items)
        {
            result.Add(RenderListItem(item!, ctx, hasCheckbox: false, isChecked: false, orderValue: ordered ? index : null));
            index++;
        }
        return result;
    }

    private static List<RichListItem> RenderTaskItems(JsonArray? items, RenderContext ctx)
    {
        if (items is null)
            return [];

        return items.Select(item =>
        {
            var isChecked = (bool?)item!["attrs"]?["checked"] ?? false;
            return RenderListItem(item, ctx, hasCheckbox: true, isChecked, orderValue: null);
        }).ToList();
    }

    private static RichListItem RenderListItem(JsonNode item, RenderContext ctx, bool hasCheckbox, bool isChecked, int? orderValue) =>
        new(RenderBlocks(item["content"]?.AsArray(), ctx).ToList(), hasCheckbox, isChecked, orderValue);

    private static CedarRichBlock RenderTable(JsonArray? rows, RenderContext ctx)
    {
        var tableRows = new List<IReadOnlyList<RichTableCell>>();
        if (rows is not null)
        {
            foreach (var row in rows)
            {
                var rowCells = new List<RichTableCell>();
                foreach (var cell in row!["content"]?.AsArray() ?? [])
                {
                    var isHeader = (string?)cell!["type"] == "tableHeader";
                    var colspan = (int?)cell["attrs"]?["colspan"];
                    var rowspan = (int?)cell["attrs"]?["rowspan"];
                    rowCells.Add(new RichTableCell(RenderCellInline(cell["content"]?.AsArray(), ctx), isHeader, colspan, rowspan));
                }
                tableRows.Add(rowCells);
            }
        }
        return new RichTableBlock(tableRows);
    }

    // Table cells hold plain formatted text, not full block content — a single wrapping paragraph
    // is unwrapped, multiple paragraphs are joined with a line break.
    private static RichRun RenderCellInline(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            return new RichRunText("");

        var runs = nodes.Select(n => (string?)n!["type"] == "paragraph"
            ? RenderInline(n["content"]?.AsArray(), ctx)
            : new RichRunText(""))
            .ToList();
        return runs.Count == 1 ? runs[0] : new RichRunSequence(runs);
    }

    private static RichRun RenderInline(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            return new RichRunText("");

        var runs = new List<RichRun>();
        foreach (var n in nodes)
        {
            switch ((string?)n!["type"])
            {
                case "text":
                    runs.Add(RenderTextNode(n));
                    break;
                case "hardBreak":
                    runs.Add(new RichRunText("\n"));
                    break;
                case "inlineMath":
                    runs.Add(new RichRunMath((string?)n["attrs"]?["latex"] ?? ""));
                    break;
                case "datetime":
                    var unix = (long?)n["attrs"]?["unix"] ?? 0;
                    var format = (string?)n["attrs"]?["format"] ?? "wDT";
                    runs.Add(new RichRunDateTime(DateTimeOffset.FromUnixTimeSeconds(unix).ToString("u"), unix, format));
                    break;
                case "footnote":
                    ctx.Footnotes.Add((string?)n["attrs"]?["text"] ?? "");
                    runs.Add(new RichRunText($"[{ctx.Footnotes.Count}]"));
                    break;
            }
        }
        return runs.Count == 1 ? runs[0] : new RichRunSequence(runs);
    }

    private static RichRun RenderTextNode(JsonNode node)
    {
        RichRun run = new RichRunText((string?)node["text"] ?? "");

        if (node["marks"]?.AsArray() is { } marks)
        {
            // Applied in reverse so the FIRST mark in the array ends up outermost — matches the
            // nesting convention of the string-based renderers (bold,italic => <b><i>x</i></b>).
            foreach (var m in marks.Reverse())
            {
                run = (string?)m!["type"] switch
                {
                    "bold" => new RichRunBold(run),
                    "italic" => new RichRunItalic(run),
                    "underline" => new RichRunUnderline(run),
                    "strike" => new RichRunStrike(run),
                    "code" => new RichRunCode(run),
                    "spoiler" => new RichRunSpoiler(run),
                    "link" => new RichRunLink(run, (string?)m["attrs"]?["href"] ?? ""),
                    _ => run
                };
            }
        }

        return run;
    }

    private static string RenderRawText(JsonArray? nodes)
    {
        if (nodes is null)
            return "";

        var text = "";
        foreach (var n in nodes)
            if ((string?)n!["type"] == "text")
                text += (string?)n["text"] ?? "";
        return text;
    }

    private static string ResolveUrl(string? src, string? mediaBaseUrl)
    {
        src ??= "";
        if (src.StartsWith('/') && mediaBaseUrl is not null)
            src = mediaBaseUrl.TrimEnd('/') + src;
        return src;
    }
}
