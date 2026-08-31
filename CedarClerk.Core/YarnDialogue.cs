using System.Text;
using System.Text.RegularExpressions;

namespace CedarClerk.Core;

/// <summary>One node of a dialogue graph — a Yarn node body plus the editor's canvas position.</summary>
public record YarnDialogueNode(string Id, string Title, double X, double Y, string Body);

/// <summary>One localizable line, addressed by its <c>#line:</c> tag — the unit the xlsx sheet trades in.</summary>
public record YarnLine(string LineId, string NodeTitle, string Character, string Text);

/// <summary>
/// Yarn (Yarn Spinner) text mechanics for the dialogue tool: which body lines are localizable,
/// stamping stable <c>#line:</c> ids onto them, extracting them for the translation sheet, and
/// assembling the <c>.yarn</c> file. Pure string work on purpose — the graph JSON, the database
/// and the xlsx stay in the server.
///
/// Localizable means: a plain dialogue line (with or without a <c>Speaker:</c> prefix) or a
/// shortcut option (<c>-&gt; text</c>). Commands (<c>&lt;&lt;…&gt;&gt;</c>), comments (<c>//</c>) and blank
/// lines carry no player-visible text and are skipped.
/// </summary>
public static class YarnDialogue
{
    private static readonly Regex LineTag = new(@"#line:(?<id>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex Speaker = new(@"^(?<name>[^:\[\]{}<>#/]{1,60}?):\s", RegexOptions.Compiled);
    private static readonly Regex InlineCommand = new(@"<<.*?>>", RegexOptions.Compiled);
    private static readonly Regex TitleUnsafe = new(@"[^A-Za-z0-9_]", RegexOptions.Compiled);

    public static bool IsLocalizable(string rawLine)
    {
        var t = rawLine.TrimStart();
        if (t.Length == 0 || t.StartsWith("//") || t.StartsWith("<<")) return false;
        // An option marker alone, or one followed only by a condition, has no text to translate.
        if (t.StartsWith("->")) t = t[2..].Trim();
        return StripNonText(t).Length > 0;
    }

    /// <summary>
    /// Returns the body with a fresh <c>#line:</c> tag appended to every localizable line that
    /// lacks one. Existing tags are kept — ids must survive edits, or every save would orphan the
    /// sheet's translations. <paramref name="usedIds"/> spans the whole script and collects every
    /// id seen or created.
    /// </summary>
    public static string EnsureLineIds(string body, HashSet<string> usedIds)
    {
        var lines = SplitLines(body);
        for (var i = 0; i < lines.Length; i++)
        {
            var existing = LineTag.Match(lines[i]);
            if (existing.Success) { usedIds.Add(existing.Groups["id"].Value); continue; }
            if (!IsLocalizable(lines[i])) continue;

            string id;
            do { id = NewLineId(); } while (!usedIds.Add(id));
            lines[i] = lines[i].TrimEnd() + " #line:" + id;
        }
        return string.Join("\n", lines);
    }

    /// <summary>Every tagged localizable line of the script, in node-then-body order.</summary>
    public static List<YarnLine> ExtractLines(IEnumerable<YarnDialogueNode> nodes)
    {
        var result = new List<YarnLine>();
        foreach (var node in nodes)
        {
            foreach (var raw in SplitLines(node.Body))
            {
                var tag = LineTag.Match(raw);
                if (!tag.Success || !IsLocalizable(raw)) continue;

                var text = raw.TrimStart();
                var character = "";
                if (text.StartsWith("->")) text = text[2..].Trim();
                else
                {
                    var speaker = Speaker.Match(text);
                    if (speaker.Success)
                    {
                        character = speaker.Groups["name"].Value.Trim();
                        text = text[speaker.Length..];
                    }
                }
                result.Add(new YarnLine(tag.Groups["id"].Value, node.Title, character, StripNonText(text)));
            }
        }
        return result;
    }

    /// <summary>
    /// The <c>.yarn</c> file: <c>title</c>/<c>position</c> headers, <c>---</c> body <c>===</c> per node.
    /// The position header is what Yarn Spinner's own VS Code graph view reads, so the canvas
    /// layout survives the round trip. Titles are sanitized to Yarn's identifier rules; the
    /// editor's stored title is not rewritten.
    /// </summary>
    public static string BuildYarnFile(IEnumerable<YarnDialogueNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
        {
            sb.Append("title: ").Append(SafeTitle(node.Title)).Append('\n');
            sb.Append("position: ").Append((int)node.X).Append(',').Append((int)node.Y).Append('\n');
            sb.Append("---\n");
            sb.Append(node.Body.Replace("\r\n", "\n").TrimEnd('\n')).Append('\n');
            sb.Append("===\n");
        }
        return sb.ToString();
    }

    /// <summary>Yarn node titles are identifiers: anything else becomes '_', a leading digit gets a prefix.</summary>
    public static string SafeTitle(string title)
    {
        var safe = TitleUnsafe.Replace(title.Trim(), "_");
        if (safe.Length == 0) safe = "Node";
        if (char.IsDigit(safe[0])) safe = "_" + safe;
        return safe;
    }

    private static string StripNonText(string line)
    {
        var noCommands = InlineCommand.Replace(line, "");
        var hash = FindTagStart(noCommands);
        return (hash >= 0 ? noCommands[..hash] : noCommands).Trim();
    }

    // A '#' opens the tag section only as its own token — '#3' inside a sentence is text.
    private static int FindTagStart(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '#') continue;
            if ((i == 0 || char.IsWhiteSpace(line[i - 1])) && i + 1 < line.Length && char.IsLetter(line[i + 1]))
                return i;
        }
        return -1;
    }

    private static string NewLineId() => Guid.NewGuid().ToString("N")[..8];

    private static string[] SplitLines(string body) => body.Replace("\r\n", "\n").Split('\n');
}
