using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CedarClerk.Core;

// Renders a Cedar document (TipTap JSON) to Steam announcement BBCode (news hub markup:
// [h1]-[h3], [b]/[i]/[u]/[strike]/[spoiler], [url=], [list]/[olist], [quote], [code],
// [table], [img], [hr][/hr], [previewyoutube]). Output is meant for copy-paste into the
// Steam announcement editor, not for any API call.
//
// The BBCode analog of renderers.md invariant 1: user text containing "[" is wrapped in
// [noparse]...[/noparse], and any literal "[/noparse]" inside it is split so it can never
// close the wrapper. Inside [code] content the same treatment applies to a literal "[/code]".
// URL values percent-escape "[" and "]" (a "]" would otherwise terminate the tag argument).
public static class CedarToSteamBbcodeRenderer
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

        if (ctx.Footnotes.Count > 0)
        {
            // "(n)" markers, not "[n]": a renderer-emitted "[1]" would itself need noparse.
            var notes = string.Join("\n", ctx.Footnotes.Select((t, i) => $"({i + 1}) {EscapeBbcode(t)}"));
            body = body.Length == 0 ? notes : body + "\n\n" + notes;
        }

        return body;
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
                // Steam only has [h1]-[h3]; deeper levels clamp to [h3].
                var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 1, 3);
                return $"[h{level}]{RenderInline(node["content"]?.AsArray(), ctx)}[/h{level}]";

            // BBCode has no in-page anchors, so a table of contents has nothing to link to.
            case "tableOfContents":
                return "";

            case "bulletList":
                return RenderList(node["content"]?.AsArray(), ordered: false, ctx);

            case "orderedList":
                return RenderList(node["content"]?.AsArray(), ordered: true, ctx);

            case "taskList":
                return RenderTaskList(node["content"]?.AsArray(), ctx);

            case "codeBlock":
                var code = RenderRawText(node["content"]?.AsArray());
                return $"[code]\n{EscapeCodeContent(code)}\n[/code]";

            case "blockquote":
                var quoted = RenderBlocks(node["content"]?.AsArray(), ctx);
                return quoted.Length == 0 ? "" : $"[quote]\n{quoted}\n[/quote]";

            case "horizontalRule":
                return "[hr][/hr]";

            case "image":
                var imgUrl = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                if (imgUrl.Length == 0)
                    return "";
                return WithCaptionLine($"[img]{EscapeUrl(imgUrl)}[/img]", (string?)node["attrs"]?["caption"]);

            // GIFs stored as video nodes render fine through [img]; anything else has no Steam
            // BBCode embed, so it degrades to a link.
            case "video":
                var videoUrl = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                if (videoUrl.Length == 0)
                    return "";
                if (IsGifSrc(videoUrl))
                    return WithCaptionLine($"[img]{EscapeUrl(videoUrl)}[/img]", (string?)node["attrs"]?["caption"]);
                return WithCaptionLine($"[url={EscapeUrl(videoUrl)}]▶ Video[/url]", (string?)node["attrs"]?["caption"]);

            case "audio":
                var audioUrl = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                if (audioUrl.Length == 0)
                    return "";
                var audioName = ((string?)node["attrs"]?["title"])?.Trim();
                var audioLabel = string.IsNullOrEmpty(audioName) ? "♪ Audio" : EscapeBbcode(audioName);
                return WithCaptionLine($"[url={EscapeUrl(audioUrl)}]{audioLabel}[/url]", (string?)node["attrs"]?["caption"]);

            case "youtube":
                var videoId = (string?)node["attrs"]?["videoId"];
                // The id lands inside a tag argument, so anything outside YouTube's own id
                // alphabet is refused outright rather than escaped.
                if (string.IsNullOrEmpty(videoId) || !Regex.IsMatch(videoId, "^[A-Za-z0-9_-]+$"))
                    return "";
                return WithCaptionLine($"[previewyoutube={videoId};full][/previewyoutube]", (string?)node["attrs"]?["caption"]);

            // Same rule as the Blocks renderer (ADR-019): an empty slideshow/collage is an
            // editor artifact — drop it instead of emitting an empty gallery.
            case "carousel":
            case "collage":
                var urls = node["attrs"]?["images"]?.AsArray()
                    ?.Select(img => ResolveUrl((string?)img, ctx.MediaBaseUrl))
                    .Where(u => u.Length > 0).ToList() ?? [];
                if (urls.Count == 0)
                    return "";
                return string.Join("\n", urls.Select(u => $"[img]{EscapeUrl(u)}[/img]"));

            case "table":
                return RenderTable(node["content"]?.AsArray(), ctx);

            case "blockMath":
                return $"[code]\n{EscapeCodeContent((string?)node["attrs"]?["latex"] ?? "")}\n[/code]";

            case "toggle":
                // No collapsible element in Steam BBCode — bold summary line, content below.
                var summary = (string?)node["attrs"]?["summary"] ?? "";
                var toggleBody = RenderBlocks(node["content"]?.AsArray(), ctx);
                if (toggleBody.Length == 0)
                    return "";
                return $"[b]{EscapeBbcode(summary)}[/b]\n\n{toggleBody}";

            // Polls are blog-only (NF5); a text placeholder keeps the reader aware one exists.
            case "poll":
                var question = (string?)node["attrs"]?["question"] ?? "";
                var options = (node["attrs"]?["options"] as JsonArray)?
                    .Select(o => (string?)o).Where(o => !string.IsNullOrWhiteSpace(o)).ToList() ?? [];
                if (options.Count == 0)
                    return "";
                return $"[i]Poll: {EscapeBbcode(question)}[/i]";

            default:
                // Unknown block type (e.g. blog-only "annotation") — render children unwrapped,
                // matching the other renderers' fallback behavior.
                return RenderBlocks(node["content"]?.AsArray(), ctx);
        }
    }

    private static string WithCaptionLine(string block, string? caption) =>
        string.IsNullOrEmpty(caption) ? block : block + "\n" + EscapeBbcode(caption);

    private static string RenderList(JsonArray? items, bool ordered, RenderContext ctx)
    {
        if (items is null || items.Count == 0)
            return "";

        var sb = new StringBuilder(ordered ? "[olist]" : "[list]");
        foreach (var item in items)
            sb.Append("\n[*]").Append(RenderListItem(item!, ctx));
        sb.Append('\n').Append(ordered ? "[/olist]" : "[/list]");
        return sb.ToString();
    }

    private static string RenderTaskList(JsonArray? items, RenderContext ctx)
    {
        if (items is null || items.Count == 0)
            return "";

        var sb = new StringBuilder("[list]");
        foreach (var item in items)
        {
            var isChecked = (bool?)item!["attrs"]?["checked"] ?? false;
            sb.Append("\n[*]").Append(isChecked ? "☑ " : "☐ ").Append(RenderListItem(item, ctx));
        }
        sb.Append("\n[/list]");
        return sb.ToString();
    }

    private static string RenderListItem(JsonNode item, RenderContext ctx)
    {
        var content = item["content"]?.AsArray();
        if (content is null || content.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var child in content)
        {
            var rendered = (string?)child!["type"] == "paragraph"
                ? RenderInline(child["content"]?.AsArray(), ctx)
                : RenderBlock(child, ctx);
            if (rendered.Length > 0)
                parts.Add(rendered);
        }
        return string.Join("\n", parts);
    }

    // Steam tables have no colspan/rowspan — spans are dropped, cells rendered plainly.
    private static string RenderTable(JsonArray? rows, RenderContext ctx)
    {
        if (rows is null || rows.Count == 0)
            return "";

        var sb = new StringBuilder("[table]");
        foreach (var row in rows)
        {
            sb.Append("\n[tr]");
            foreach (var cell in row!["content"]?.AsArray() ?? [])
            {
                var tag = (string?)cell!["type"] == "tableHeader" ? "th" : "td";
                sb.Append($"[{tag}]").Append(RenderCellInline(cell["content"]?.AsArray(), ctx)).Append($"[/{tag}]");
            }
            sb.Append("[/tr]");
        }
        sb.Append("\n[/table]");
        return sb.ToString();
    }

    private static string RenderCellInline(JsonArray? nodes, RenderContext ctx)
    {
        if (nodes is null)
            return "";

        var parts = nodes.Select(n => (string?)n!["type"] == "paragraph"
            ? RenderInline(n["content"]?.AsArray(), ctx)
            : RenderBlock(n, ctx));
        return string.Join(" ", parts.Where(p => p.Length > 0));
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
                    sb.Append(EscapeBbcode($"${(string?)n["attrs"]?["latex"] ?? ""}$"));
                    break;
                case "datetime":
                    var unix = (long?)n["attrs"]?["unix"] ?? 0;
                    var format = (string?)n["attrs"]?["format"] ?? "wDT";
                    sb.Append(FormatDateTime(unix, format));
                    break;
                case "footnote":
                    ctx.Footnotes.Add((string?)n["attrs"]?["text"] ?? "");
                    sb.Append($"({ctx.Footnotes.Count})");
                    break;
                // Internal document link — nothing on Steam to point it at, label travels as text.
                case "wikilink":
                    sb.Append(EscapeBbcode((string?)n["attrs"]?["label"] ?? ""));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string RenderTextNode(JsonNode node)
    {
        var raw = (string?)node["text"] ?? "";
        var open = new StringBuilder();
        var close = new StringBuilder();
        var isCode = false;

        if (node["marks"]?.AsArray() is { } marks)
        {
            foreach (var m in marks)
            {
                switch ((string?)m!["type"])
                {
                    case "bold":
                        open.Append("[b]");
                        close.Insert(0, "[/b]");
                        break;
                    case "italic":
                        open.Append("[i]");
                        close.Insert(0, "[/i]");
                        break;
                    case "underline":
                        open.Append("[u]");
                        close.Insert(0, "[/u]");
                        break;
                    case "strike":
                        open.Append("[strike]");
                        close.Insert(0, "[/strike]");
                        break;
                    case "spoiler":
                        open.Append("[spoiler]");
                        close.Insert(0, "[/spoiler]");
                        break;
                    case "code":
                        open.Append("[code]");
                        close.Insert(0, "[/code]");
                        isCode = true;
                        break;
                    case "link":
                        var href = (string?)m["attrs"]?["href"] ?? "";
                        // Only web/mail schemes make a link; anything else (javascript:, data:,
                        // a relative path) renders as plain text — same call for both targets.
                        if (!IsSafeHref(href))
                            break;
                        open.Append($"[url={EscapeUrl(href)}]");
                        close.Insert(0, "[/url]");
                        break;
                }
            }
        }

        var text = isCode ? EscapeCodeContent(raw) : EscapeBbcode(raw);
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

    // "]" would end the surrounding tag's argument, "[" would open a new tag mid-URL.
    private static string EscapeUrl(string url) =>
        url.Replace("[", "%5B").Replace("]", "%5D");

    // Any "[" in user text goes inside [noparse]. A literal "[/noparse]" in the text would close
    // that wrapper, so it is split: its "[" gets a one-character noparse of its own, and the
    // bracket-free remainder ("/noparse]") is emitted raw.
    private static string EscapeBbcode(string s)
    {
        if (!s.Contains('['))
            return s;

        var sb = new StringBuilder();
        var idx = 0;
        while (true)
        {
            var hit = s.IndexOf("[/noparse]", idx, StringComparison.OrdinalIgnoreCase);
            AppendNoparsed(sb, hit < 0 ? s[idx..] : s[idx..hit]);
            if (hit < 0)
                break;
            sb.Append("[noparse][[/noparse]").Append(s.AsSpan(hit + 1, 9));
            idx = hit + 10;
        }
        return sb.ToString();
    }

    private static void AppendNoparsed(StringBuilder sb, string segment)
    {
        if (segment.Length == 0)
            return;
        if (!segment.Contains('['))
        {
            sb.Append(segment);
            return;
        }
        sb.Append("[noparse]").Append(segment).Append("[/noparse]");
    }

    // Inside [code] everything is literal except its own closing tag. A literal "[/code]" closes
    // the surrounding block, gets shown via [noparse], and the block reopens after it.
    private static string EscapeCodeContent(string s)
    {
        if (s.IndexOf("[/code]", StringComparison.OrdinalIgnoreCase) < 0)
            return s;

        var sb = new StringBuilder();
        var idx = 0;
        while (true)
        {
            var hit = s.IndexOf("[/code]", idx, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                sb.Append(s[idx..]);
                break;
            }
            sb.Append(s[idx..hit])
              .Append("[/code][noparse]").Append(s.AsSpan(hit, 7)).Append("[/noparse][code]");
            idx = hit + 7;
        }
        return sb.ToString();
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
}
