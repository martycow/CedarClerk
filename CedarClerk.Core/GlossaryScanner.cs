using System.Text;

namespace CedarClerk.Core;

// Aliases exist because Russian inflects — "рендерер" appears as "рендерера", "рендереру" — and
// listing forms beats guessing at per-language stemming. IsCaseSensitive is for terms where the
// casing IS the meaning ("IT" the industry against "it"), and covers the aliases too.
public sealed record GlossaryMatchTerm(string Term, string Description, string? ImageUrl, IReadOnlyList<string> Aliases, bool IsCaseSensitive = false);

// Idea #11 — wraps glossary terms in rendered text so the blog page can show a description on hover.
// Two rules are the whole reason this is a separate, tested unit:
//
// 1. It runs on ALREADY-ESCAPED text and never introduces unescaped content. Escaping afterwards
//    would escape the markup this adds; the cost is that the matcher must not read "&amp;" as five
//    letters, which is why entities are skipped below.
// 2. Every eligible occurrence is marked so readers can open the definition wherever they meet it.
public static class GlossaryScanner
{
    /// <summary>
    /// Wraps every eligible occurrence of a glossary term in <paramref name="escapedText"/>.
    /// </summary>
    public static string Mark(string escapedText, IReadOnlyList<GlossaryMatchTerm> glossary)
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
                    if (!MatchesAt(escapedText, i, alias, entry.IsCaseSensitive)) continue;

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

    /// <summary>
    /// How many times each entry occurs in <paramref name="plainText"/>, parallel to the input list.
    /// </summary>
    /// <remarks>
    /// Parallel array rather than a dictionary because <see cref="GlossaryMatchTerm"/> carries no id and
    /// the same word is a separate term in each language and each project. The input is plain text,
    /// so — unlike <see cref="Mark"/> — "&amp;amp;" is five letters here and is matched as such.
    /// </remarks>
    public static int[] CountHits(string plainText, IReadOnlyList<GlossaryMatchTerm> glossary)
    {
        var counts = new int[glossary.Count];
        if (glossary.Count == 0 || plainText.Length == 0) return counts;

        var candidates = glossary
            .SelectMany((e, index) => e.Aliases.Prepend(e.Term)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => (Alias: a.Trim(), Index: index, e.IsCaseSensitive)))
            .OrderByDescending(c => c.Alias.Length)
            .ToList();

        var i = 0;
        while (i < plainText.Length)
        {
            var matched = false;
            if (IsWordStart(plainText, i))
            {
                foreach (var (alias, index, caseSensitive) in candidates)
                {
                    if (!MatchesAt(plainText, i, alias, caseSensitive)) continue;

                    counts[index]++;
                    i += alias.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched) i++;
        }

        return counts;
    }

    private static void AppendMarked(StringBuilder sb, string matchedText, GlossaryMatchTerm entry)
    {
        sb.Append("<span class=\"glossary-term\" tabindex=\"0\" data-term=\"")
          .Append(EscapeAttr(entry.Term))
          .Append("\" data-desc=\"")
          .Append(EscapeAttr(entry.Description));
        if (!string.IsNullOrWhiteSpace(entry.ImageUrl))
            sb.Append("\" data-img=\"").Append(EscapeAttr(entry.ImageUrl!));
        sb.Append("\">").Append(matchedText).Append("</span>");
    }

    private static bool MatchesAt(string text, int index, string alias, bool caseSensitive)
    {
        if (index + alias.Length > text.Length) return false;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Compare(text, index, alias, 0, alias.Length, comparison) != 0) return false;
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
