using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <param name="Length">Wire length — the escaped commentary in UTF-16 units, what LinkedIn measures.</param>
public sealed record LinkedInPost(string Text, int Length);

// One LinkedIn post (T-381). ADR-077 still holds — the author's override wins and the blog link
// survives truncation — but the fallback is the document itself rather than a teaser: LinkedIn
// takes 3,000 characters, and a feed post has no markup, so the layout is carried by what plain
// text carries (blank lines, bullets, numbering, quotes) plus the Mathematical Alphanumeric block
// for emphasis, the only bold and italic LinkedIn's feed ever renders. That block covers Latin
// letters and digits only; Cyrillic emphasis degrades to plain text rather than to a mixed face.
public static class LinkedInPostBuilder
{
    public const int MaxChars = 3000;

    private const string Ellipsis = "…";
    private const string Separator = "\n\n";
    private const string ListIndent = "    ";

    public static LinkedInPost Build(string? authorText, string cedarJson, string? blogUrl, bool styled = true)
    {
        var body = string.IsNullOrWhiteSpace(authorText) ? Render(cedarJson, styled) : authorText.Trim();

        if (string.IsNullOrWhiteSpace(blogUrl))
        {
            var alone = Fit(body, MaxChars);
            return new LinkedInPost(alone, Measure(alone));
        }

        body = Fit(body, MaxChars - Measure(blogUrl) - Separator.Length);
        var text = body.Length == 0 ? blogUrl : body + Separator + blogUrl;
        return new LinkedInPost(text, Measure(text));
    }

    /// <summary>The limit is on the escaped commentary, so that is what a length has to count.</summary>
    public static int Measure(string text) => LinkedInLittleText.Escape(text).Length;

    /// <summary>
    /// Whole blocks until the budget, then an ellipsis line — a post cut mid-sentence reads as a
    /// mistake, a post that stops at a paragraph and says "…" reads as a preview of the link below.
    /// </summary>
    public static string Fit(string text, int budget)
    {
        if (budget <= 0) return "";
        if (Measure(text) <= budget) return text;

        var tail = Separator + Ellipsis;
        var blocks = text.Split(Separator);
        var kept = new StringBuilder();
        foreach (var block in blocks)
        {
            var candidate = kept.Length == 0 ? block : kept + Separator + block;
            if (Measure(candidate) + tail.Length > budget) break;
            kept.Clear();
            kept.Append(candidate);
        }
        if (kept.Length > 0) return kept + tail;

        // Not even the first block fits: cut it on a grapheme, the way every short-post builder does.
        var cut = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(blocks[0]);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            if (Measure(cut + element) + 1 > budget) break;
            cut.Append(element);
        }
        return cut.ToString().TrimEnd() + Ellipsis;
    }

    /// <summary>The document as LinkedIn text, before any override or limit is applied.</summary>
    public static string Render(string cedarJson, bool styled = true)
    {
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(cedarJson);
        }
        catch (JsonException)
        {
            return "";
        }

        var ctx = new Context(styled);
        var blocks = new List<string>();
        RenderBlocks(doc is JsonObject root ? root["content"] as JsonArray : doc as JsonArray, blocks, ctx);
        if (ctx.Footnotes.Count > 0)
            blocks.Add(string.Join("\n", ctx.Footnotes.Select((note, i) => $"{i + 1}. {note}")));
        return string.Join(Separator, blocks.Where(b => b.Trim().Length > 0));
    }

    private sealed class Context(bool styled)
    {
        public bool Styled { get; } = styled;
        public List<string> Footnotes { get; } = [];
    }

    private static void RenderBlocks(JsonArray? nodes, List<string> blocks, Context ctx)
    {
        if (nodes is null) return;
        foreach (var child in nodes)
        {
            if (child is not JsonObject node) continue;
            switch ((string?)node["type"])
            {
                case "paragraph":
                    blocks.Add(Inline(node["content"], ctx));
                    break;

                case "heading":
                    blocks.Add(Inline(node["content"], ctx, forceBold: true));
                    break;

                case "bulletList":
                case "orderedList":
                case "taskList":
                    blocks.Add(RenderList(node, ctx, depth: 0));
                    break;

                case "blockquote":
                    var quoted = new List<string>();
                    RenderBlocks(node["content"] as JsonArray, quoted, ctx);
                    if (quoted.Count > 0) blocks.Add("“" + string.Join(Separator, quoted) + "”");
                    break;

                // No monospace on LinkedIn; the lines and their indentation are what survives.
                case "codeBlock":
                    blocks.Add(Inline(node["content"], new Context(styled: false)));
                    break;

                case "horizontalRule":
                    blocks.Add("———");
                    break;

                case "table":
                    blocks.Add(RenderTable(node, ctx));
                    break;

                // The same line CedarPlainText gives the other short-post networks: caption first,
                // then the watch link — LinkedIn cannot embed it, but it unfurls the link.
                case "youtube":
                    if ((string?)node["attrs"]?["videoId"] is { Length: > 0 } videoId)
                    {
                        var caption = (string?)node["attrs"]?["caption"];
                        var url = $"https://www.youtube.com/watch?v={videoId}";
                        blocks.Add(string.IsNullOrWhiteSpace(caption) ? url : $"{caption.Trim()} {url}");
                    }
                    break;

                case "toggle":
                    blocks.Add(Style((string?)node["attrs"]?["summary"] ?? "Details", ctx, bold: true, italic: false));
                    RenderBlocks(node["content"] as JsonArray, blocks, ctx);
                    break;

                case "poll":
                    var options = (node["attrs"]?["options"] as JsonArray)?
                        .Select(o => (string?)o).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o!).ToList() ?? [];
                    if (options.Count == 0) break;
                    var question = (string?)node["attrs"]?["question"] ?? "";
                    blocks.Add(string.Join("\n", options.Select(o => $"○ {o}").Prepend(Style(question, ctx, bold: true, italic: false))));
                    break;

                case "blockMath":
                    blocks.Add((string?)node["attrs"]?["latex"] ?? "");
                    break;

                // Pictures ride as media, not as text; the rest has no LinkedIn shape at all.
                case "image":
                case "carousel":
                case "collage":
                case "video":
                case "audio":
                case "tableOfContents":
                    break;

                default:
                    RenderBlocks(node["content"] as JsonArray, blocks, ctx);
                    break;
            }
        }
    }

    private static string RenderList(JsonObject list, Context ctx, int depth)
    {
        var type = (string?)list["type"];
        var start = (int?)list["attrs"]?["start"] ?? 1;
        var indent = string.Concat(Enumerable.Repeat(ListIndent, depth));
        var lines = new List<string>();
        var index = 0;
        foreach (var child in list["content"] as JsonArray ?? [])
        {
            if (child is not JsonObject item) continue;
            var marker = type switch
            {
                "orderedList" => $"{start + index}.",
                "taskList" => (bool?)item["attrs"]?["checked"] == true ? "☑" : "☐",
                _ => "•",
            };
            index++;

            var first = true;
            foreach (var part in item["content"] as JsonArray ?? [])
            {
                if (part is not JsonObject block) continue;
                switch ((string?)block["type"])
                {
                    case "bulletList":
                    case "orderedList":
                    case "taskList":
                        lines.Add(RenderList(block, ctx, depth + 1));
                        break;
                    default:
                        var text = (string?)block["type"] == "heading"
                            ? Inline(block["content"], ctx, forceBold: true)
                            : Inline(block["content"], ctx);
                        if (text.Length == 0) continue;
                        // Continuation paragraphs hang under the item's text, not under its marker.
                        lines.Add(first ? $"{indent}{marker} {text}" : $"{indent}{new string(' ', marker.Length + 1)}{text}");
                        first = false;
                        break;
                }
            }
            if (first) lines.Add($"{indent}{marker}");
        }
        return string.Join("\n", lines);
    }

    private static string RenderTable(JsonObject table, Context ctx)
    {
        var rows = new List<string>();
        foreach (var row in table["content"] as JsonArray ?? [])
        {
            if (row is not JsonObject r) continue;
            var cells = new List<string>();
            foreach (var cell in r["content"] as JsonArray ?? [])
            {
                if (cell is not JsonObject c) continue;
                var inner = new List<string>();
                RenderBlocks(c["content"] as JsonArray, inner, ctx);
                cells.Add(string.Join(" ", inner));
            }
            rows.Add(string.Join(" | ", cells));
        }
        return string.Join("\n", rows);
    }

    private static string Inline(JsonNode? content, Context ctx, bool forceBold = false)
    {
        if (content is not JsonArray array) return "";
        var builder = new StringBuilder();
        foreach (var child in array)
        {
            if (child is not JsonObject node) continue;
            switch ((string?)node["type"])
            {
                case "text":
                    var text = (string?)node["text"] ?? "";
                    var bold = forceBold;
                    var italic = false;
                    string? href = null;
                    foreach (var mark in node["marks"] as JsonArray ?? [])
                    {
                        switch ((string?)mark?["type"])
                        {
                            case "bold": bold = true; break;
                            case "italic": italic = true; break;
                            case "link": href = (string?)mark?["attrs"]?["href"]; break;
                        }
                    }
                    builder.Append(Style(text, ctx, bold, italic));
                    // A link's address is invisible in plain text; the label alone would leave the
                    // reader with nothing to open. Skipped when the label already is the address.
                    if (!string.IsNullOrWhiteSpace(href) && !SameAddress(text, href))
                        builder.Append(" (").Append(href).Append(')');
                    break;

                case "hardBreak":
                    builder.Append('\n');
                    break;

                case "wikilink":
                    builder.Append(Style((string?)node["attrs"]?["label"] ?? "", ctx, forceBold, false));
                    break;

                case "inlineMath":
                    builder.Append((string?)node["attrs"]?["latex"]);
                    break;

                case "footnote":
                    ctx.Footnotes.Add((string?)node["attrs"]?["text"] ?? "");
                    builder.Append('[').Append(ctx.Footnotes.Count).Append(']');
                    break;

                case "datetime":
                    var unix = (long?)node["attrs"]?["unix"] ?? 0;
                    builder.Append(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
                    break;

                default:
                    builder.Append(Inline(node["content"], ctx, forceBold));
                    break;
            }
        }
        return builder.ToString().Trim();
    }

    private static bool SameAddress(string text, string href)
    {
        static string Bare(string s) => s.Trim().TrimEnd('/')
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
        return string.Equals(Bare(text), Bare(href), StringComparison.OrdinalIgnoreCase);
    }

    private static string Style(string text, Context ctx, bool bold, bool italic) =>
        ctx.Styled && (bold || italic) ? UnicodeStyle.Apply(text, bold, italic) : text;
}

// The Mathematical Alphanumeric Symbols block as a typeface: LinkedIn (and every other plain-text
// feed) renders no markup, and these codepoints are how a post carries a bold heading there.
// Sans-serif faces, because the feed's own type is sans-serif and a serif bold reads as a font
// change rather than as emphasis. Only ASCII letters and digits have counterparts.
public static class UnicodeStyle
{
    private const int BoldUpper = 0x1D5D4, BoldLower = 0x1D5EE, BoldDigit = 0x1D7EC;
    private const int ItalicUpper = 0x1D608, ItalicLower = 0x1D622;
    private const int BoldItalicUpper = 0x1D63C, BoldItalicLower = 0x1D656;

    public static string Apply(string text, bool bold, bool italic)
    {
        if (!bold && !italic) return text;
        var (upper, lower) = (bold, italic) switch
        {
            (true, true) => (BoldItalicUpper, BoldItalicLower),
            (true, false) => (BoldUpper, BoldLower),
            _ => (ItalicUpper, ItalicLower),
        };

        var builder = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (c is >= 'A' and <= 'Z') builder.Append(char.ConvertFromUtf32(upper + (c - 'A')));
            else if (c is >= 'a' and <= 'z') builder.Append(char.ConvertFromUtf32(lower + (c - 'a')));
            // Italic digits do not exist in the block; bold ones do.
            else if (bold && c is >= '0' and <= '9') builder.Append(char.ConvertFromUtf32(BoldDigit + (c - '0')));
            else builder.Append(c);
        }
        return builder.ToString();
    }
}

// LinkedIn's "little" text format: the commentary field is a tiny grammar in which @[…](urn) is a
// mention, #word a hashtag and {…} a template, so every reserved character that is meant as text
// has to be escaped or the post is refused (or worse, parsed). Hashtags are the one thing left
// alone — a '#' starting a word is what the author meant, and escaping it would kill the tag.
public static class LinkedInLittleText
{
    private const string Reserved = "\\|{}@[]()<>#*_~";

    public static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (Reserved.Contains(c) && !(c == '#' && StartsHashtag(text, i))) builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool StartsHashtag(string text, int i) =>
        (i == 0 || char.IsWhiteSpace(text[i - 1]))
        && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]);
}
