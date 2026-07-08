using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// Telegram Bot API 10.1 "Rich Message" Markdown mode (InputRichMessage.Markdown) — NOT classic
// Markdown or MarkdownV2. Syntax mirrors Bot/RickTextFixture.cs, which was already sent successfully
// to a real channel via /richtest. Raw HTML tags (e.g. <u>, <tg-slideshow>) are valid mixed into
// this Markdown, per the fixture's "Nested Syntax" example.
public static class CedarToTelegramMarkdownRenderer
{
    private sealed class RenderContext
    {
        public string? MediaBaseUrl;
        public List<string> Footnotes { get; } = [];
    }

    public static string Render(string cedarJson, string? mediaBaseUrl = null)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid cedar JSON");
        var doc = root["doc"] ?? root;
        var ctx = new RenderContext { MediaBaseUrl = mediaBaseUrl };
        var body = RenderBlocks(doc["content"]?.AsArray(), ctx).Trim();

        if (ctx.Footnotes.Count == 0)
            return body;

        var footer = string.Join("\n", ctx.Footnotes.Select((text, i) => $"[^{i + 1}]: {text}"));
        return body + "\n\n" + footer;
    }

    private static string RenderBlocks(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            return "";

        var blocks = new List<string>();
        foreach (var n in nodes)
        {
            var block = RenderBlock(n!, ctx);
            if (block.Length > 0)
                blocks.Add(block);
        }
        return string.Join("\n\n", blocks);
    }

    private static string RenderBlock(JsonNode node, RenderContext ctx)
    {
        switch ((string?)node["type"])
        {
            case "paragraph":
                return RenderInline(node["content"]?.AsArray(), ctx);

            case "heading":
                var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 1, 6);
                return new string('#', level) + " " + RenderInline(node["content"]?.AsArray(), ctx);

            case "bulletList":
                return RenderList(node["content"]?.AsArray(), ordered: false, ctx);

            case "orderedList":
                return RenderList(node["content"]?.AsArray(), ordered: true, ctx);

            case "taskList":
                return RenderTaskList(node["content"]?.AsArray(), ctx);

            case "codeBlock":
                var lang = (string?)node["attrs"]?["language"];
                var code = RenderRawText(node["content"]?.AsArray());
                return lang is null ? $"```\n{code}\n```" : $"```{lang}\n{code}\n```";

            case "blockquote":
                var quoted = RenderBlocks(node["content"]?.AsArray(), ctx);
                return string.Join("\n", quoted.Split('\n').Select(l => "> " + l));

            case "horizontalRule":
                return "---";

            case "image":
            case "video":
            case "audio":
                var url = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                var caption = (string?)node["attrs"]?["caption"];
                return string.IsNullOrEmpty(caption) ? $"![]({url})" : $"![]({url} \"{EscapeCaption(caption)}\")";

            case "carousel":
                var images = node["attrs"]?["images"]?.AsArray()
                    ?.Select(img => $"![]({ResolveUrl((string?)img, ctx.MediaBaseUrl)})") ?? [];
                return "<tg-slideshow>\n\n" + string.Join("\n", images) + "\n\n</tg-slideshow>";

            case "collage":
                var collageImages = node["attrs"]?["images"]?.AsArray()
                    ?.Select(img => $"![]({ResolveUrl((string?)img, ctx.MediaBaseUrl)})") ?? [];
                return "<tg-collage>\n\n" + string.Join("\n", collageImages) + "\n\n</tg-collage>";

            case "table":
                return RenderTable(node["content"]?.AsArray(), ctx);

            case "blockMath":
                return $"```math\n{(string?)node["attrs"]?["latex"] ?? ""}\n```";

            case "toggle":
                var summary = (string?)node["attrs"]?["summary"] ?? "";
                var body = RenderBlocks(node["content"]?.AsArray(), ctx);
                return $"<details open><summary>{summary}</summary>\n\n{body}\n\n</details>";

            default:
                return RenderBlocks(node["content"]?.AsArray(), ctx);
        }
    }

    // Markdown list markers give block-level structure for free, so lists/tasks are rendered
    // directly rather than dispatched through RenderBlock like the HTML renderer does.
    private static string RenderList(JsonArray? items, bool ordered, RenderContext ctx)
    {
        if (items is null)
            return "";

        var lines = new List<string>();
        var index = 1;
        foreach (var item in items)
        {
            var marker = ordered ? $"{index}. " : "- ";
            lines.Add(RenderListItem(item!, marker, ctx));
            index++;
        }
        return string.Join("\n", lines);
    }

    private static string RenderTaskList(JsonArray? items, RenderContext ctx)
    {
        if (items is null)
            return "";

        var lines = new List<string>();
        foreach (var item in items)
        {
            var isChecked = (bool?)item!["attrs"]?["checked"] ?? false;
            var marker = isChecked ? "- [x] " : "- [ ] ";
            lines.Add(RenderListItem(item, marker, ctx));
        }
        return string.Join("\n", lines);
    }

    private static string RenderListItem(JsonNode item, string marker, RenderContext ctx)
    {
        var content = item["content"]?.AsArray();
        if (content is null || content.Count == 0)
            return marker.TrimEnd();

        var first = content[0]!;
        var firstText = (string?)first["type"] == "paragraph"
            ? RenderInline(first["content"]?.AsArray(), ctx)
            : RenderBlock(first, ctx);

        var sb = new StringBuilder(marker).Append(firstText);
        for (var i = 1; i < content.Count; i++)
        {
            var rendered = RenderBlock(content[i]!, ctx);
            var indented = string.Join("\n", rendered.Split('\n').Select(l => "  " + l));
            sb.Append('\n').Append(indented);
        }
        return sb.ToString();
    }

    // Markdown tables have no colspan/rowspan concept, unlike the HTML renderer's <th>/<td> —
    // spans are dropped and every cell is rendered plainly.
    private static string RenderTable(JsonArray? rows, RenderContext ctx)
    {
        if (rows is null || rows.Count == 0)
            return "";

        var rendered = rows
            .Select(row => (row!["content"]?.AsArray() ?? [])
                .Select(cell => RenderCellInline(cell!["content"]?.AsArray(), ctx))
                .ToList())
            .ToList();

        var colCount = rendered[0].Count;
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", rendered[0])).Append(" |\n");
        sb.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", colCount)));
        for (var i = 1; i < rendered.Count; i++)
            sb.Append('\n').Append("| ").Append(string.Join(" | ", rendered[i])).Append(" |");
        return sb.ToString();
    }

    // Telegram table cells hold plain formatted text; a single wrapping paragraph is unwrapped,
    // multiple paragraphs are joined with a space since Markdown table cells can't span lines.
    private static string RenderCellInline(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            return "";

        var parts = nodes.Select(n => (string?)n!["type"] == "paragraph"
            ? RenderInline(n["content"]?.AsArray(), ctx)
            : RenderBlock(n, ctx));
        return string.Join(" ", parts);
    }

    private static string RenderInline(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            return "";

        var sb = new StringBuilder();
        foreach (var n in nodes)
        {
            switch ((string?)n!["type"])
            {
                case "text":
                    sb.Append(RenderTextNode(n));
                    break;
                case "hardBreak":
                    sb.Append('\n');
                    break;
                case "inlineMath":
                    sb.Append('$').Append((string?)n["attrs"]?["latex"] ?? "").Append('$');
                    break;
                case "datetime":
                    var unix = (long?)n["attrs"]?["unix"] ?? 0;
                    var format = (string?)n["attrs"]?["format"] ?? "wDT";
                    sb.Append($"![](tg://time?unix={unix}&format={format})");
                    break;
                case "footnote":
                    ctx.Footnotes.Add((string?)n["attrs"]?["text"] ?? "");
                    sb.Append($"[^{ctx.Footnotes.Count}]");
                    break;
            }
        }
        return sb.ToString();
    }

    private static string RenderTextNode(JsonNode node)
    {
        var text = EscapeMarkdown((string?)node["text"] ?? "");
        var open = new StringBuilder();
        var close = new StringBuilder();

        if (node["marks"]?.AsArray() is { } marks)
        {
            foreach (var m in marks)
            {
                switch ((string?)m!["type"])
                {
                    case "bold":
                        open.Append("**");
                        close.Insert(0, "**");
                        break;
                    case "italic":
                        open.Append('_');
                        close.Insert(0, "_");
                        break;
                    case "underline":
                        open.Append("<u>");
                        close.Insert(0, "</u>");
                        break;
                    case "strike":
                        open.Append("~~");
                        close.Insert(0, "~~");
                        break;
                    case "code":
                        open.Append('`');
                        close.Insert(0, "`");
                        break;
                    case "link":
                        var href = (string?)m["attrs"]?["href"] ?? "";
                        open.Append('[');
                        close.Insert(0, $"]({href})");
                        break;
                    case "spoiler":
                        open.Append("||");
                        close.Insert(0, "||");
                        break;
                }
            }
        }

        return open.ToString() + text + close;
    }

    private static string RenderRawText(JsonArray? nodes)
    {
        if (nodes is null)
            return "";

        var sb = new StringBuilder();
        foreach (var n in nodes)
            if ((string?)n!["type"] == "text")
                sb.Append((string?)n["text"] ?? "");
        return sb.ToString();
    }

    private static string ResolveUrl(string? src, string? mediaBaseUrl)
    {
        src ??= "";
        if (src.StartsWith('/') && mediaBaseUrl is not null)
            src = mediaBaseUrl.TrimEnd('/') + src;
        return src;
    }

    // Escapes characters that are structurally significant to the syntax we emit above, so that
    // plain user-typed text can't accidentally break or inject markdown structure. Does not
    // attempt Telegram's auto-entity-detection escaping (e.g. '#' before hashtags) since this
    // renderer only ever emits marks/nodes it explicitly controls.
    private static string EscapeMarkdown(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("*", "\\*")
         .Replace("_", "\\_")
         .Replace("~", "\\~")
         .Replace("|", "\\|")
         .Replace("`", "\\`")
         .Replace("[", "\\[")
         .Replace("]", "\\]");

    // Same as EscapeMarkdown plus escaping the double quote that would otherwise terminate the
    // enclosing ![](url "caption") title early.
    private static string EscapeCaption(string s) =>
        EscapeMarkdown(s).Replace("\"", "\\\"");
}
