using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CedarClerk.Core;

// Hand-rolled, deliberately scoped Markdown -> TipTap JSON converter — not a full CommonMark
// implementation and not a new dependency (CedarClerk.Core stays zero-external-dependency).
// Built for Notion's Markdown export shape: headings, paragraphs, bullet/ordered/checkbox lists
// (with indent-based nesting), block-level images, blockquotes, fenced code blocks, horizontal
// rules, and the common inline marks (bold/italic/strikethrough/code/links). Anything else
// degrades to a plain paragraph containing the raw line — never thrown away, never a crash.
// See ADR-026, docs/DECISIONS.md.
public static class MarkdownToCedarConverter
{
    private enum ListKind { Bullet, Ordered, Task }

    private readonly record struct InlineMark(string Type, string? Href);
    private readonly record struct ListLine(int Indent, ListKind Kind, string Text, bool Checked);

    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$");
    private static readonly Regex TaskRegex = new(@"^(\s*)[-*+]\s+\[([ xX])\]\s+(.*)$");
    private static readonly Regex OrderedRegex = new(@"^(\s*)\d+[.)]\s+(.*)$");
    private static readonly Regex BulletRegex = new(@"^(\s*)[-*+]\s+(.*)$");
    private static readonly Regex ImageRegex = new(@"^!\[([^\]]*)\]\(([^)]+)\)\s*$");
    private static readonly Regex HrRegex = new(@"^(-{3,}|\*{3,}|_{3,})$");
    private static readonly Regex BlockquoteRegex = new(@"^>\s?(.*)$");
    private static readonly Regex FenceRegex = new(@"^```");
    private static readonly Regex InlineLinkRegex = new(@"\G\[([^\]]*)\]\(([^)]+)\)");

    public static string Convert(string markdown, out string? titleFromHeading)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = ParseBlocks(lines);
        titleFromHeading = FindFirstHeadingText(blocks);

        var doc = new JsonObject { ["type"] = "doc", ["content"] = blocks };
        return doc.ToJsonString();
    }

    private static string? FindFirstHeadingText(JsonArray blocks)
    {
        foreach (var block in blocks)
        {
            if (block is JsonObject obj && obj["type"]?.GetValue<string>() == "heading")
            {
                var text = ExtractPlainText(obj["content"] as JsonArray);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return null;
    }

    private static string ExtractPlainText(JsonArray? content)
    {
        if (content is null) return "";
        var sb = new StringBuilder();
        foreach (var node in content)
        {
            if (node is JsonObject o && o["text"] is JsonValue v && v.TryGetValue<string>(out var s))
                sb.Append(s);
        }
        return sb.ToString();
    }

    private static bool IsBlockStart(string line) =>
        HeadingRegex.IsMatch(line) || HrRegex.IsMatch(line.Trim()) || ImageRegex.IsMatch(line.Trim())
        || BlockquoteRegex.IsMatch(line) || FenceRegex.IsMatch(line)
        || TryMatchListItem(line, out _);

    private static JsonArray ParseBlocks(string[] lines)
    {
        var result = new JsonArray();
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            if (FenceRegex.IsMatch(line))
            {
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !FenceRegex.IsMatch(lines[i]))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++; // skip closing fence
                result.Add(BuildCodeBlock(string.Join("\n", codeLines)));
                continue;
            }

            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                result.Add(BuildHeading(headingMatch.Groups[1].Value.Length, headingMatch.Groups[2].Value));
                i++;
                continue;
            }

            if (HrRegex.IsMatch(line.Trim()))
            {
                result.Add(new JsonObject { ["type"] = "horizontalRule" });
                i++;
                continue;
            }

            var imageMatch = ImageRegex.Match(line.Trim());
            if (imageMatch.Success)
            {
                result.Add(BuildImage(imageMatch.Groups[1].Value, imageMatch.Groups[2].Value));
                i++;
                continue;
            }

            if (BlockquoteRegex.IsMatch(line))
            {
                var bqLines = new List<string>();
                while (i < lines.Length && BlockquoteRegex.IsMatch(lines[i]))
                {
                    bqLines.Add(BlockquoteRegex.Match(lines[i]).Groups[1].Value);
                    i++;
                }
                result.Add(new JsonObject
                {
                    ["type"] = "blockquote",
                    ["content"] = new JsonArray(BuildParagraph(string.Join(" ", bqLines))),
                });
                continue;
            }

            if (TryMatchListItem(line, out _))
            {
                var (node, consumed) = ParseList(lines, i);
                result.Add(node);
                i += consumed;
                continue;
            }

            // Default: paragraph — gather until a blank line or the start of another block.
            var paraLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines[i]))
            {
                paraLines.Add(lines[i]);
                i++;
            }
            result.Add(BuildParagraph(string.Join(" ", paraLines).Trim()));
        }
        return result;
    }

    private static bool TryMatchListItem(string line, out ListLine item)
    {
        var taskMatch = TaskRegex.Match(line);
        if (taskMatch.Success)
        {
            item = new ListLine(NormalizedIndent(taskMatch.Groups[1].Value), ListKind.Task,
                taskMatch.Groups[3].Value, taskMatch.Groups[2].Value is "x" or "X");
            return true;
        }

        var orderedMatch = OrderedRegex.Match(line);
        if (orderedMatch.Success)
        {
            item = new ListLine(NormalizedIndent(orderedMatch.Groups[1].Value), ListKind.Ordered,
                orderedMatch.Groups[2].Value, false);
            return true;
        }

        var bulletMatch = BulletRegex.Match(line);
        if (bulletMatch.Success)
        {
            item = new ListLine(NormalizedIndent(bulletMatch.Groups[1].Value), ListKind.Bullet,
                bulletMatch.Groups[2].Value, false);
            return true;
        }

        item = default;
        return false;
    }

    private static int NormalizedIndent(string whitespace) => whitespace.Replace("\t", "    ").Length;

    // Gathers the contiguous run of list-item lines (of any indent/kind — tolerating single blank
    // lines between items, matching common "loose list" Notion output), then builds a nested
    // bulletList/orderedList/taskList tree from it by indent depth.
    private static (JsonObject node, int consumed) ParseList(string[] lines, int start)
    {
        var items = new List<ListLine>();
        var i = start;
        while (i < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                if (i + 1 < lines.Length && TryMatchListItem(lines[i + 1], out _)) { i++; continue; }
                break;
            }
            if (!TryMatchListItem(lines[i], out var item)) break;
            items.Add(item);
            i++;
        }

        var node = BuildNestedList(items, 0, items.Count);
        return (node, i - start);
    }

    private static JsonObject BuildNestedList(List<ListLine> items, int start, int end)
    {
        var baseIndent = items[start].Indent;
        var kind = items[start].Kind;
        var listType = kind switch { ListKind.Ordered => "orderedList", ListKind.Task => "taskList", _ => "bulletList" };
        var listItems = new JsonArray();

        var i = start;
        while (i < end && items[i].Indent <= baseIndent)
        {
            var item = items[i];
            var itemContent = new JsonArray { BuildParagraph(item.Text) };
            i++;

            if (i < end && items[i].Indent > baseIndent)
            {
                var subStart = i;
                while (i < end && items[i].Indent > baseIndent) i++;
                itemContent.Add(BuildNestedList(items, subStart, i));
            }

            var itemObj = new JsonObject { ["type"] = kind == ListKind.Task ? "taskItem" : "listItem", ["content"] = itemContent };
            if (kind == ListKind.Task)
                itemObj["attrs"] = new JsonObject { ["checked"] = item.Checked };
            listItems.Add(itemObj);
        }

        return new JsonObject { ["type"] = listType, ["content"] = listItems };
    }

    private static JsonObject BuildHeading(int level, string text) =>
        new() { ["type"] = "heading", ["attrs"] = new JsonObject { ["level"] = level }, ["content"] = ParseInline(text) };

    private static JsonObject BuildParagraph(string text)
    {
        var inline = ParseInline(text);
        var node = new JsonObject { ["type"] = "paragraph" };
        if (inline.Count > 0) node["content"] = inline;
        return node;
    }

    private static JsonObject BuildCodeBlock(string code) =>
        new() { ["type"] = "codeBlock", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = code }) };

    // Images are block-level only (matching the editor's Image node, which is not configured
    // inline) — the common case for a Notion export, where each image sits on its own line.
    // `src` is a placeholder the caller rewrites to a real asset path (see CedarPackage.RewriteMediaPaths).
    private static JsonObject BuildImage(string alt, string path)
    {
        var decoded = Uri.UnescapeDataString(path);
        var basename = decoded[(decoded.LastIndexOfAny(['/', '\\']) + 1)..];
        return new JsonObject
        {
            ["type"] = "image",
            ["attrs"] = new JsonObject { ["src"] = $"/media/{basename}", ["alt"] = alt.Length > 0 ? alt : null, ["title"] = null, ["caption"] = alt.Length > 0 ? alt : null },
        };
    }

    private static JsonArray ParseInline(string text) => new(ParseInlineSpan(text, []).Cast<JsonNode>().ToArray());

    // Returns freshly-created, not-yet-parented nodes — callers own attaching them to a JsonArray
    // exactly once (a JsonNode throws if added to a second array).
    private static List<JsonObject> ParseInlineSpan(string text, IReadOnlyList<InlineMark> marks)
    {
        var result = new List<JsonObject>();
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0) return;
            var node = new JsonObject { ["type"] = "text", ["text"] = buffer.ToString() };
            if (marks.Count > 0) node["marks"] = BuildMarksArray(marks);
            result.Add(node);
            buffer.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    var nested = new List<InlineMark>(marks) { new("code", null) };
                    result.Add(new JsonObject { ["type"] = "text", ["text"] = text[(i + 1)..end], ["marks"] = BuildMarksArray(nested) });
                    i = end + 1;
                    continue;
                }
            }

            if (text[i] == '[')
            {
                var linkMatch = InlineLinkRegex.Match(text, i);
                if (linkMatch.Success)
                {
                    Flush();
                    var nested = new List<InlineMark>(marks) { new("link", linkMatch.Groups[2].Value) };
                    result.AddRange(ParseInlineSpan(linkMatch.Groups[1].Value, nested));
                    i += linkMatch.Length;
                    continue;
                }
            }

            if (i + 1 < text.Length && text[i] == '~' && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Flush();
                    var nested = new List<InlineMark>(marks) { new("strike", null) };
                    result.AddRange(ParseInlineSpan(text[(i + 2)..end], nested));
                    i = end + 2;
                    continue;
                }
            }

            if (i + 1 < text.Length && (text[i] == '*' || text[i] == '_') && text[i + 1] == text[i])
            {
                var marker = text[i];
                var end = text.IndexOf(new string(marker, 2), i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Flush();
                    var nested = new List<InlineMark>(marks) { new("bold", null) };
                    result.AddRange(ParseInlineSpan(text[(i + 2)..end], nested));
                    i = end + 2;
                    continue;
                }
            }

            if (text[i] == '*' || text[i] == '_')
            {
                var marker = text[i];
                var end = text.IndexOf(marker, i + 1);
                if (end > i)
                {
                    Flush();
                    var nested = new List<InlineMark>(marks) { new("italic", null) };
                    result.AddRange(ParseInlineSpan(text[(i + 1)..end], nested));
                    i = end + 1;
                    continue;
                }
            }

            buffer.Append(text[i]);
            i++;
        }
        Flush();
        return result;
    }

    private static JsonArray BuildMarksArray(IReadOnlyList<InlineMark> marks) =>
        new(marks.Select(m => (JsonNode)(m.Href is null
            ? new JsonObject { ["type"] = m.Type }
            : new JsonObject { ["type"] = m.Type, ["attrs"] = new JsonObject { ["href"] = m.Href } })).ToArray());
}
