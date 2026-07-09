using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// Renders a Cedar document (TipTap JSON) to a public-facing HTML fragment for the blog.
// Unlike CedarToTelegramHtmlRenderer, output goes straight to browsers, so attribute values
// are escaped for quotes too (not just text content).
public static class CedarToBlogHtmlRenderer
{
    private sealed class RenderContext
    {
        public required string MediaBaseUrl;
        public List<string> Footnotes { get; } = [];
    }

    public static string Render(string cedarJson, string mediaBaseUrl)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid cedar JSON");
        var doc = root["doc"] ?? root;
        var sb = new StringBuilder();
        var ctx = new RenderContext { MediaBaseUrl = mediaBaseUrl };
        RenderNodes(doc["content"]?.AsArray(), sb, ctx);
        AppendFootnotes(sb, ctx);
        return sb.ToString();
    }

    private static void AppendFootnotes(StringBuilder sb, RenderContext ctx)
    {
        if (ctx.Footnotes.Count == 0)
            return;

        sb.Append("<section class=\"footnotes\"><hr><ol>");
        for (var i = 0; i < ctx.Footnotes.Count; i++)
            sb.Append($"<li id=\"fn-{i + 1}\">").Append(Escape(ctx.Footnotes[i])).Append("</li>");
        sb.Append("</ol></section>");
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
                var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 1, 6);
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
                sb.Append(lang is null ? "<pre><code>" : $"<pre><code class=\"language-{EscapeAttr(lang)}\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</code></pre>");
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
                var src = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                AppendMedia(sb, "img", src, (string?)node["attrs"]?["caption"], isVoid: true);
                break;

            case "video":
                var videoSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                AppendMedia(sb, "video", videoSrc, (string?)node["attrs"]?["caption"], isVoid: false);
                break;

            case "audio":
                var audioSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                AppendMedia(sb, "audio", audioSrc, (string?)node["attrs"]?["caption"], isVoid: false);
                break;

            case "carousel":
                RenderCarousel(node["attrs"]?["images"]?.AsArray(), sb, ctx);
                break;

            case "collage":
                sb.Append("<div class=\"collage\">");
                if (node["attrs"]?["images"]?.AsArray() is { } collageImages)
                    foreach (var img in collageImages)
                        sb.Append($"<img loading=\"lazy\" src=\"{EscapeAttr(ResolveUrl((string?)img, ctx.MediaBaseUrl))}\">");
                sb.Append("</div>");
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
                sb.Append("<ul class=\"task-list\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ul>");
                break;

            case "taskItem":
                var isChecked = (bool?)node["attrs"]?["checked"] ?? false;
                sb.Append(isChecked ? "<li><input type=\"checkbox\" disabled checked> " : "<li><input type=\"checkbox\" disabled> ");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</li>");
                break;

            case "blockMath":
                sb.Append($"<div class=\"math-tex\" data-display=\"true\">{Escape((string?)node["attrs"]?["latex"] ?? "")}</div>");
                break;

            case "inlineMath":
                sb.Append($"<span class=\"math-tex\" data-display=\"false\">{Escape((string?)node["attrs"]?["latex"] ?? "")}</span>");
                break;

            case "toggle":
                var summary = Escape((string?)node["attrs"]?["summary"] ?? "Details");
                sb.Append($"<details><summary>{summary}</summary>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</details>");
                break;

            case "datetime":
                var unix = (long?)node["attrs"]?["unix"] ?? 0;
                var format = (string?)node["attrs"]?["format"] ?? "wDT";
                var dt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                sb.Append($"<time datetime=\"{dt:yyyy-MM-ddTHH:mm:ssZ}\">{Escape(FormatDateTime(dt, format))}</time>");
                break;

            case "footnote":
                ctx.Footnotes.Add((string?)node["attrs"]?["text"] ?? "");
                sb.Append($"<sup><a href=\"#fn-{ctx.Footnotes.Count}\">[{ctx.Footnotes.Count}]</a></sup>");
                break;

            // Intentionally not handled by CedarToTelegramHtmlRenderer/CedarToTelegramMarkdownRenderer —
            // both fall through to their own "unknown type" default (render children only), which is
            // exactly the desired behavior there: Telegram has no concept of anchored reactions/comments.
            case "annotation":
                var aid = (string?)node["attrs"]?["id"] ?? "";
                sb.Append($"<div class=\"annotation\" data-annotation-id=\"{EscapeAttr(aid)}\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append(AnnotationControlsHtml());
                sb.Append("</div>");
                break;

            default:
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                break;
        }
    }

    // Zero-count placeholder markup for the like/dislike/comment controls on an anchored region —
    // hydrated client-side (see BlogEndpoints' page script). Also reused as-is by BlogEndpoints for
    // the whole-article reaction/comment block, wrapped in the same ".annotation" div with an empty id.
    // Comments are shown by default (no expand/collapse) — client script paginates the list.
    public static string AnnotationControlsHtml() => """
        <div class="annotation-controls">
        <button type="button" class="react-btn" data-kind="like">&#128077; <span class="count" data-kind-count="like">0</span></button>
        <button type="button" class="react-btn" data-kind="dislike">&#128078; <span class="count" data-kind-count="dislike">0</span></button>
        <span class="comment-count-label">&#128172; <span class="comment-count">0</span></span>
        </div>
        <div class="comment-box">
        <div class="comment-list"></div>
        <button type="button" class="comment-load-more" hidden>Show more comments</button>
        <form class="comment-form">
        <input type="text" class="comment-author" placeholder="Name (optional)" maxlength="60">
        <textarea class="comment-text" placeholder="Write a comment..." maxlength="2000" required></textarea>
        <button type="submit">Post</button>
        </form>
        </div>
        """;

    private static string FormatDateTime(DateTime dt, string format)
    {
        var parts = new List<string>();
        if (format.Contains('w')) parts.Add(dt.ToString("ddd", CultureInfo.InvariantCulture));
        if (format.Contains('D')) parts.Add(dt.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
        if (format.Contains('T')) parts.Add(dt.ToString("HH:mm", CultureInfo.InvariantCulture));
        return parts.Count > 0 ? string.Join(' ', parts) : dt.ToString("g", CultureInfo.InvariantCulture);
    }

    private static void AppendMedia(StringBuilder sb, string tag, string src, string? caption, bool isVoid)
    {
        var mediaHtml = isVoid
            ? $"<{tag} loading=\"lazy\" src=\"{EscapeAttr(src)}\">"
            : $"<{tag} controls src=\"{EscapeAttr(src)}\"></{tag}>";
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

    // Interactive slideshow (prev/next + dots) — behavior wired up client-side by a small
    // script in the page shell (BlogEndpoints.PageShell) querying for the .carousel class.
    private static void RenderCarousel(JsonArray? images, StringBuilder sb, RenderContext ctx)
    {
        var urls = (images ?? []).Select(img => ResolveUrl((string?)img, ctx.MediaBaseUrl)).ToList();
        if (urls.Count == 0)
            return;

        sb.Append("<div class=\"carousel\">");
        sb.Append("<div class=\"carousel-viewport\">");
        foreach (var url in urls)
            sb.Append($"<img loading=\"lazy\" src=\"{EscapeAttr(url)}\">");
        sb.Append("</div>");

        if (urls.Count > 1)
        {
            sb.Append("<button type=\"button\" class=\"carousel-prev\" aria-label=\"Previous\">&#8249;</button>");
            sb.Append("<button type=\"button\" class=\"carousel-next\" aria-label=\"Next\">&#8250;</button>");
            sb.Append("<div class=\"carousel-dots\">");
            for (var i = 0; i < urls.Count; i++)
                sb.Append($"<button type=\"button\" class=\"carousel-dot\" aria-label=\"Slide {i + 1}\"></button>");
            sb.Append("</div>");
        }

        sb.Append("</div>");
    }

    private static string ResolveUrl(string? src, string mediaBaseUrl)
    {
        src ??= "";
        if (src.StartsWith('/'))
            src = mediaBaseUrl.TrimEnd('/') + src;
        return src;
    }

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
                        open.Append("<strong>");
                        close.Insert(0, "</strong>");
                        break;
                    case "italic":
                        open.Append("<em>");
                        close.Insert(0, "</em>");
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
                        var href = EscapeAttr((string?)m["attrs"]?["href"] ?? "");
                        open.Append($"<a href=\"{href}\" rel=\"noopener noreferrer\">");
                        close.Insert(0, "</a>");
                        break;
                    case "spoiler":
                        open.Append("<span class=\"spoiler\">");
                        close.Insert(0, "</span>");
                        break;
                }
            }
        }

        sb.Append(open).Append(text).Append(close);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) => Escape(s).Replace("\"", "&quot;");
}
