using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

public static class CedarToTelegramHtmlRenderer
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
        var sb = new StringBuilder();
        var ctx = new RenderContext { MediaBaseUrl = mediaBaseUrl };
        RenderNodes(doc["content"]?.AsArray(), sb, ctx);
        AppendFootnotes(sb, ctx);
        return sb.ToString();
    }

    // Best-effort fallback (no confirmed dedicated Telegram tag for footnotes in HTML mode):
    // a plain numbered list degrades reasonably even without special footnote semantics.
    private static void AppendFootnotes(StringBuilder sb, RenderContext ctx)
    {
        if (ctx.Footnotes.Count == 0)
            return;

        sb.Append("<hr><ol>");
        foreach (var text in ctx.Footnotes)
            sb.Append("<li>").Append(Escape(text)).Append("</li>");
        sb.Append("</ol>");
    }

    private static void RenderNodes(JsonArray? nodes, StringBuilder sb, RenderContext ctx)
    {
        if (nodes is null)
            return;

        foreach (var n in nodes)
            RenderNode(n!, sb, ctx);
    }

    private static void RenderNode(JsonNode node, StringBuilder sb, RenderContext ctx)
    {
        switch ((string?)node["type"])
        {
            case "paragraph":
                sb.Append("<p>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</p>");
                break;

            case "heading":
                var level = (int?)node["attrs"]?["level"] ?? 1;
                level = Math.Clamp(level, 1, 6);
                sb.Append($"<h{level}>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append($"</h{level}>");
                break;

            case "bulletList":
                sb.Append("<ul>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ul>");
                break;

            case "orderedList":
                sb.Append("<ol>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ol>");
                break;

            case "listItem":
                sb.Append("<li>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</li>");
                break;

            case "codeBlock":
                var lang = (string?)node["attrs"]?["language"];
                sb.Append(lang is null ? "<pre>" : $"<pre><code class=\"language-{lang}\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append(lang is null ? "</pre>" : "</code></pre>");
                break;

            case "blockquote":
                sb.Append("<blockquote>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</blockquote>");
                break;

            case "horizontalRule":
                sb.Append("<hr>");
                break;

            case "hardBreak":
                sb.Append("<br>");
                break;

            case "text":
                RenderText(node, sb);
                break;

            case "image":
                // Rich Message HTML has no <img> tag — RichBlockType.Photo corresponds to <photo>.
                var src = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                AppendMedia(sb, "photo", src, (string?)node["attrs"]?["caption"], isVoid: true);
                break;

            case "video":
                var videoSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                // video/audio — не void-теги: без закрытия парсер вложит в них весь дальнейший контент,
                // и Telegram перенесёт медиа в конец сообщения
                AppendMedia(sb, "video", videoSrc, (string?)node["attrs"]?["caption"], isVoid: false);
                break;

            case "audio":
                var audioSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                AppendMedia(sb, "audio", audioSrc, (string?)node["attrs"]?["caption"], isVoid: false);
                break;

            case "carousel":
                sb.Append("<tg-slideshow>");
                if (node["attrs"]?["images"]?.AsArray() is { } images)
                {
                    foreach (var img in images)
                    {
                        var imgSrc = ResolveUrl((string?)img, ctx.MediaBaseUrl);
                        sb.Append($"<photo src=\"{Escape(imgSrc)}\">");
                    }
                }
                sb.Append("</tg-slideshow>");
                break;

            case "collage":
                sb.Append("<tg-collage>");
                if (node["attrs"]?["images"]?.AsArray() is { } collageImages)
                {
                    foreach (var img in collageImages)
                    {
                        var imgSrc = ResolveUrl((string?)img, ctx.MediaBaseUrl);
                        sb.Append($"<photo src=\"{Escape(imgSrc)}\">");
                    }
                }
                sb.Append("</tg-collage>");
                break;

            case "table":
                sb.Append("<table>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</table>");
                break;

            case "tableRow":
                sb.Append("<tr>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</tr>");
                break;

            case "tableHeader":
            case "tableCell":
                var tag = (string?)node["type"] == "tableHeader" ? "th" : "td";
                var span = "";
                if ((int?)node["attrs"]?["colspan"] is { } colspan && colspan > 1)
                    span += $" colspan=\"{colspan}\"";
                if ((int?)node["attrs"]?["rowspan"] is { } rowspan && rowspan > 1)
                    span += $" rowspan=\"{rowspan}\"";
                sb.Append($"<{tag}{span}>");
                RenderCellContent(node["content"]?.AsArray(), sb, ctx);
                sb.Append($"</{tag}>");
                break;

            case "taskList":
                sb.Append("<ul>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ul>");
                break;

            case "taskItem":
                var isChecked = (bool?)node["attrs"]?["checked"] ?? false;
                sb.Append(isChecked ? "<li><input type=\"checkbox\" checked>" : "<li><input type=\"checkbox\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</li>");
                break;

            case "blockMath":
                var blockLatex = Escape((string?)node["attrs"]?["latex"] ?? "");
                sb.Append($"<tg-math-block>{blockLatex}</tg-math-block>");
                break;

            case "inlineMath":
                var inlineLatex = Escape((string?)node["attrs"]?["latex"] ?? "");
                sb.Append($"<tg-math>{inlineLatex}</tg-math>");
                break;

            case "toggle":
                var summary = Escape((string?)node["attrs"]?["summary"] ?? "");
                sb.Append($"<details open><summary>{summary}</summary>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</details>");
                break;

            case "datetime":
                var unix = (long?)node["attrs"]?["unix"] ?? 0;
                var format = Escape((string?)node["attrs"]?["format"] ?? "wDT");
                sb.Append($"<img src=\"tg://time?unix={unix}&amp;format={format}\">");
                break;

            case "footnote":
                ctx.Footnotes.Add((string?)node["attrs"]?["text"] ?? "");
                sb.Append($"<sup>[{ctx.Footnotes.Count}]</sup>");
                break;

            default:
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                break;
        }
    }

    // Wraps void (<img>) and non-void (<video>/<audio>) media tags in <figure>/<figcaption>
    // when a caption is set — best-effort guess, no confirmed dedicated Telegram tag.
    private static void AppendMedia(StringBuilder sb, string tag, string src, string? caption, bool isVoid)
    {
        var mediaHtml = isVoid ? $"<{tag} src=\"{Escape(src)}\">" : $"<{tag} src=\"{Escape(src)}\"></{tag}>";
        if (string.IsNullOrEmpty(caption))
        {
            sb.Append(mediaHtml);
        }
        else
        {
            sb.Append("<figure>").Append(mediaHtml)
              .Append("<figcaption>").Append(Escape(caption)).Append("</figcaption></figure>");
        }
    }

    private static string ResolveUrl(string? src, string? mediaBaseUrl)
    {
        src ??= "";
        if (src.StartsWith('/') && mediaBaseUrl is not null)
            src = mediaBaseUrl.TrimEnd('/') + src;
        return src;
    }

    // Telegram table cells hold plain formatted text, not block content - a single
    // wrapping <p> is unwrapped rather than nested inside <td>/<th>.
    private static void RenderCellContent(JsonArray? nodes, StringBuilder sb, RenderContext ctx)
    {
        if (nodes is null)
            return;

        foreach (var n in nodes)
        {
            if ((string?)n!["type"] == "paragraph")
                RenderNodes(n["content"]?.AsArray(), sb, ctx);
            else
                RenderNode(n, sb, ctx);
        }
    }

    private static void RenderText(JsonNode node, StringBuilder sb)
    {
        var text = Escape((string?)node["text"] ?? "");
        var open = new StringBuilder();
        var close = new StringBuilder();

        if (node["marks"]?.AsArray() is { } marks)
        {
            foreach (var m in marks)
            {
                switch ((string?)m!["type"])
                {
                    case "bold":
                        open.Append("<b>");
                        close.Insert(0, "</b>");
                        break;
                    case "italic":
                        open.Append("<i>");
                        close.Insert(0, "</i>");
                        break;
                    case "underline":
                        open.Append("<u>");
                        close.Insert(0, "</u>");
                        break;
                    case "strike":
                        open.Append("<s>");
                        close.Insert(0, "</s>");
                        break;
                    case "code":
                        open.Append("<code>");
                        close.Insert(0, "</code>");
                        break;
                    case "link":
                        var href = Escape((string?)m["attrs"]?["href"] ?? "");
                        open.Append($"<a href=\"{href}\">");
                        close.Insert(0, "</a>");
                        break;
                    case "spoiler":
                        open.Append("<tg-spoiler>");
                        close.Insert(0, "</tg-spoiler>");
                        break;
                }
            }
        }

        sb.Append(open).Append(text).Append(close);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
