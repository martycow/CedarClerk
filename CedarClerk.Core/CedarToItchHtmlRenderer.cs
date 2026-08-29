using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CedarClerk.Core;

// Renders a Cedar document (TipTap JSON) to the HTML subset itch.io devlogs keep after
// sanitization: p, h2/h3, strong/em/u/s, a, code/pre, ul/ol/li, blockquote, hr, img.
// Output is meant for copy-paste into the itch.io devlog editor, not for any API call.
// Invariant 1 (renderers.md) holds throughout: user text is escaped (< > &) before it is
// placed into markup, and attribute values additionally escape quotes. Blocks itch has no
// element for degrade to their plain-text content — never raw JSON.
public static class CedarToItchHtmlRenderer
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

        if (ctx.Footnotes.Count > 0)
        {
            sb.Append("<hr>");
            for (var i = 0; i < ctx.Footnotes.Count; i++)
                sb.Append($"<p>({i + 1}) ").Append(Escape(ctx.Footnotes[i])).Append("</p>");
        }

        return sb.ToString();
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
                // h1 is the devlog title's tag on itch — body headings demote to h2, and
                // levels below h3 clamp up to h3.
                var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 2, 3);
                sb.Append($"<h{level}>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append($"</h{level}>");
                break;

            // No heading anchors survive itch's sanitizer, so a table of contents has
            // nothing to link to.
            case "tableOfContents":
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

            // No input elements in the subset — checkbox state degrades to a text marker.
            case "taskList":
                sb.Append("<ul>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ul>");
                break;

            case "taskItem":
                var isChecked = (bool?)node["attrs"]?["checked"] ?? false;
                sb.Append(isChecked ? "<li>☑ " : "<li>☐ ");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</li>");
                break;

            case "codeBlock":
                sb.Append("<pre><code>");
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
                if (src.Length == 0)
                    break;
                AppendImage(sb, src, (string?)node["attrs"]?["caption"]);
                break;

            // GIFs stored as video nodes work as <img>; real video has no element in the
            // subset and degrades to a link.
            case "video":
                var videoSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                if (videoSrc.Length == 0)
                    break;
                if (IsGifSrc(videoSrc))
                    AppendImage(sb, videoSrc, (string?)node["attrs"]?["caption"]);
                else
                    AppendMediaLink(sb, videoSrc, "▶ Video", (string?)node["attrs"]?["caption"]);
                break;

            case "audio":
                var audioSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                if (audioSrc.Length == 0)
                    break;
                var audioName = ((string?)node["attrs"]?["title"])?.Trim();
                AppendMediaLink(sb, audioSrc, string.IsNullOrEmpty(audioName) ? "♪ Audio" : audioName,
                    (string?)node["attrs"]?["caption"]);
                break;

            // iframes never survive itch's sanitizer — a plain watch link does.
            case "youtube":
                var videoId = (string?)node["attrs"]?["videoId"];
                if (string.IsNullOrEmpty(videoId))
                    break;
                var watchUrl = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
                var youtubeCaption = (string?)node["attrs"]?["caption"];
                var linkText = string.IsNullOrEmpty(youtubeCaption) ? "▶ Watch on YouTube" : youtubeCaption;
                sb.Append($"<p><a href=\"{EscapeAttr(watchUrl)}\">").Append(Escape(linkText)).Append("</a></p>");
                break;

            // Same rule as the Blocks renderer (ADR-019): an empty slideshow/collage is an
            // editor artifact — drop it. Non-empty ones degrade to sequential images.
            case "carousel":
            case "collage":
                if (node["attrs"]?["images"]?.AsArray() is { } images)
                    foreach (var img in images)
                    {
                        var url = ResolveUrl((string?)img, ctx.MediaBaseUrl);
                        if (url.Length > 0)
                            sb.Append($"<img src=\"{EscapeAttr(url)}\">");
                    }
                break;

            // No table element in the subset — each row degrades to a paragraph with cells
            // joined by a separator; spans are dropped.
            case "table":
                if (node["content"]?.AsArray() is { } rows)
                    foreach (var row in rows)
                    {
                        var cells = new List<string>();
                        foreach (var cell in row!["content"]?.AsArray() ?? [])
                        {
                            var cellSb = new StringBuilder();
                            RenderCellContent(cell!["content"]?.AsArray(), cellSb, ctx);
                            cells.Add(cellSb.ToString());
                        }
                        if (cells.Count > 0)
                            sb.Append("<p>").Append(string.Join(" | ", cells)).Append("</p>");
                    }
                break;

            case "blockMath":
                sb.Append("<pre><code>").Append(Escape((string?)node["attrs"]?["latex"] ?? "")).Append("</code></pre>");
                break;

            case "inlineMath":
                sb.Append("<code>").Append(Escape((string?)node["attrs"]?["latex"] ?? "")).Append("</code>");
                break;

            // <details> is not in the subset — bold summary paragraph, content below.
            case "toggle":
                var summary = (string?)node["attrs"]?["summary"] ?? "";
                sb.Append("<p><strong>").Append(Escape(summary)).Append("</strong></p>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                break;

            case "datetime":
                var unix = (long?)node["attrs"]?["unix"] ?? 0;
                var format = (string?)node["attrs"]?["format"] ?? "wDT";
                sb.Append(Escape(FormatDateTime(unix, format)));
                break;

            case "footnote":
                ctx.Footnotes.Add((string?)node["attrs"]?["text"] ?? "");
                sb.Append($"({ctx.Footnotes.Count})");
                break;

            // Internal document link — nothing on itch to point it at, label travels as text.
            case "wikilink":
                sb.Append(Escape((string?)node["attrs"]?["label"] ?? ""));
                break;

            // Polls are blog-only (NF5); a text placeholder keeps the reader aware one exists.
            case "poll":
                var question = (string?)node["attrs"]?["question"] ?? "";
                var options = (node["attrs"]?["options"] as JsonArray)?
                    .Select(o => (string?)o).Where(o => !string.IsNullOrWhiteSpace(o)).ToList() ?? [];
                if (options.Count == 0)
                    break;
                sb.Append("<p><em>Poll: ").Append(Escape(question)).Append("</em></p>");
                break;

            default:
                // Unknown block type (e.g. blog-only "annotation") — render children unwrapped,
                // matching the other renderers' fallback behavior.
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                break;
        }
    }

    private static void AppendImage(StringBuilder sb, string src, string? caption)
    {
        sb.Append($"<img src=\"{EscapeAttr(src)}\">");
        if (!string.IsNullOrEmpty(caption))
            sb.Append("<p><em>").Append(Escape(caption)).Append("</em></p>");
    }

    private static void AppendMediaLink(StringBuilder sb, string href, string label, string? caption)
    {
        sb.Append($"<p><a href=\"{EscapeAttr(href)}\">").Append(Escape(label)).Append("</a></p>");
        if (!string.IsNullOrEmpty(caption))
            sb.Append("<p><em>").Append(Escape(caption)).Append("</em></p>");
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
                        var href = (string?)m["attrs"]?["href"] ?? "";
                        // Only web/mail schemes make a link; anything else (javascript:, data:,
                        // a relative path) renders as plain text — same call as the Steam side.
                        if (!IsSafeHref(href))
                            break;
                        open.Append($"<a href=\"{EscapeAttr(href)}\">");
                        close.Insert(0, "</a>");
                        break;
                    // No spoiler element in the subset — plain text with a marker.
                    case "spoiler":
                        open.Append("(spoiler: ");
                        close.Insert(0, ")");
                        break;
                }
            }
        }

        sb.Append(open).Append(text).Append(close);
    }

    private static bool IsSafeHref(string href) =>
        href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static bool IsGifSrc(string src) =>
        Regex.IsMatch(src, @"\.gif(?:[?#]|$)", RegexOptions.IgnoreCase);

    private static string ResolveUrl(string? src, string? mediaBaseUrl)
    {
        src ??= "";
        if (src.StartsWith('/') && mediaBaseUrl is not null)
            src = mediaBaseUrl.TrimEnd('/') + src;
        return src;
    }

    // Export text has no per-reader timezone, so the instant is shown in UTC and labeled as such.
    private static string FormatDateTime(long unix, string format)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        var parts = new List<string>();
        if (format.Contains('w')) parts.Add(dt.ToString("ddd", CultureInfo.InvariantCulture));
        if (format.Contains('D')) parts.Add(dt.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
        if (format.Contains('T')) parts.Add(dt.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC");
        return parts.Count > 0
            ? string.Join(' ', parts)
            : dt.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) => Escape(s).Replace("\"", "&quot;");
}
