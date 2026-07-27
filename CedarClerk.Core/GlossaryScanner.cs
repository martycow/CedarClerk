using System.Text;

namespace CedarClerk.Core;

/// <summary>One glossary entry as the renderer needs it: what to match, and what to show.</summary>
/// <param name="Term">The canonical spelling, shown as the tooltip's heading.</param>
/// <param name="Aliases">
/// Other spellings that mean the same entry. Russian inflects — "рендерер" appears as
/// "рендерера", "рендереру" — so matching only the canonical form would miss most occurrences of
/// a term in a Russian post. Listing them beats guessing at stemming rules per language.
/// </param>
public sealed record GlossaryEntry(string Term, string Description, string? ImageUrl, IReadOnlyList<string> Aliases);

// Idea #11 — finds glossary terms in already-escaped rendered text and wraps them so the blog page
// can show a description on hover or tap.
//
// Two rules that are not obvious and are the whole reason this is a separate, tested unit:
//
// 1. It runs on text that is ALREADY HTML-escaped, and it never introduces unescaped content —
//    the description and image go into attributes through EscapeAttr. Scanning raw text and
//    escaping afterwards would escape the markup this adds; scanning escaped text means the
//    matcher must not treat "&amp;" as five letters, which is why entities are skipped below.
//
// 2. Only the FIRST occurrence of each term on a page is marked. Marking every occurrence turns
//    an article that uses a word twenty times into a page of underlines, which is noise, not help
//    — the same call every encyclopaedia makes.
public static class GlossaryScanner
{
    /// <summary>
    /// Wraps the first occurrence of each not-yet-seen term in <paramref name="escapedText"/>.
    /// <paramref name="alreadyMarked"/> carries the terms marked earlier on the same page and is
    /// updated in place, so the "first occurrence only" rule holds across the whole document
    /// rather than per text node.
    /// </summary>
    public static string Mark(string escapedText, IReadOnlyList<GlossaryEntry> glossary, HashSet<string> alreadyMarked)
    {
        if (glossary.Count == 0 || escapedText.Length == 0) return escapedText;

        // Longest first: with both "unity" and "unity engine" defined, the longer entry is the
        // one a reader means, and marking "unity" first would leave " engine" dangling outside.
        var candidates = glossary
            .SelectMany(e => e.Aliases.Prepend(e.Term).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => (Alias: a.Trim(), Entry: e)))
            .OrderByDescending(c => c.Alias.Length)
            .ToList();

        var sb = new StringBuilder(escapedText.Length);
        var i = 0;
        while (i < escapedText.Length)
        {
            // Skip HTML entities whole: "&amp;" is one character to a reader, and letting the
            // matcher walk into it could mark "amp" as a term and split the entity in half.
            if (escapedText[i] == '&')
            {
                var end = escapedText.IndexOf(';', i);
                if (end > i && end - i <= 10)
                {
                    sb.Append(escapedText, i, end - i + 1);
                    i = end + 1;
                    continue;
                }
            }

            var matched = false;
            if (IsWordStart(escapedText, i))
            {
                foreach (var (alias, entry) in candidates)
                {
                    if (alreadyMarked.Contains(entry.Term)) continue;
                    if (!MatchesAt(escapedText, i, alias)) continue;

                    alreadyMarked.Add(entry.Term);
                    AppendMarked(sb, escapedText.Substring(i, alias.Length), entry);
                    i += alias.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                sb.Append(escapedText[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static void AppendMarked(StringBuilder sb, string matchedText, GlossaryEntry entry)
    {
        sb.Append("<span class=\"glossary-term\" tabindex=\"0\" data-term=\"")
          .Append(EscapeAttr(entry.Term))
          .Append("\" data-desc=\"")
          .Append(EscapeAttr(entry.Description));
        if (!string.IsNullOrWhiteSpace(entry.ImageUrl))
            sb.Append("\" data-img=\"").Append(EscapeAttr(entry.ImageUrl!));
        sb.Append("\">").Append(matchedText).Append("</span>");
    }

    private static bool MatchesAt(string text, int index, string alias)
    {
        if (index + alias.Length > text.Length) return false;
        if (string.Compare(text, index, alias, 0, alias.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
        // A term must not match inside a longer word: "art" in "articles" is not the term.
        var after = index + alias.Length;
        return after >= text.Length || !IsWordChar(text[after]);
    }

    private static bool IsWordStart(string text, int index) =>
        IsWordChar(text[index]) && (index == 0 || !IsWordChar(text[index - 1]));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Matches CedarToBlogHtmlRenderer's own attribute escaping — the description is owner-authored
    // text landing inside a double-quoted attribute.
    private static string EscapeAttr(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
