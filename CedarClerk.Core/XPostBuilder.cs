using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CedarClerk.Core;

public sealed record XPost(string Text, int WeightedLength);

/// <summary>
/// Turns a Cedar document into the one short post X takes (T-110). Same contract as
/// <see cref="BlueskyPostBuilder"/> (ADR-077): the author's override wins, the teaser is the
/// fallback, and the blog link survives truncation of the body.
///
/// The trap in this format is the counting. X does not count characters: it counts *weighted*
/// units against 280 (twitter-text config v3) — Latin/Cyrillic/general punctuation weigh 1,
/// everything else (CJK, emoji) weighs 2, and **every URL counts as exactly 23** regardless of
/// its length, because X rewrites it through t.co. Counting `string.Length` would refuse valid
/// posts with links and happily build invalid CJK ones.
/// </summary>
public static partial class XPostBuilder
{
    public const int MaxWeightedChars = 280;

    /// <summary>What any URL costs after the t.co rewrite, long or short.</summary>
    public const int UrlWeight = 23;

    private const string Ellipsis = "…";
    private const int EllipsisWeight = 2; // U+2026 is outside every weight-1 range

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();

    /// <param name="authorText">The author's own text for this network (ADR-077), or null.</param>
    /// <param name="blogUrl">Appended when present; the body is trimmed around it, never the reverse.</param>
    public static XPost Build(string? authorText, string cedarJson, string? blogUrl)
    {
        var body = string.IsNullOrWhiteSpace(authorText)
            ? Teaser(cedarJson)
            : authorText.Trim();

        if (!string.IsNullOrWhiteSpace(blogUrl))
        {
            body = TruncateToWeight(body, MaxWeightedChars - UrlWeight - 1); // -1: the newline before the link
            var text = body.Length == 0 ? blogUrl! : body + "\n" + blogUrl;
            return new XPost(text, WeightedLength(text));
        }

        var alone = TruncateToWeight(body, MaxWeightedChars);
        return new XPost(alone, WeightedLength(alone));
    }

    /// <summary>Opening paragraphs, whole, until the weighted limit — same shape as Bluesky's teaser.</summary>
    public static string Teaser(string cedarJson)
    {
        var paragraphs = CedarPlainText.Paragraphs(cedarJson);

        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length == 0) continue;
            var candidate = builder.Length == 0 ? paragraph : builder + "\n\n" + paragraph;
            if (WeightedLength(candidate) > MaxWeightedChars) break;
            builder.Clear();
            builder.Append(candidate);
        }

        return builder.Length > 0 ? builder.ToString() : TruncateToWeight(paragraphs.FirstOrDefault() ?? "", MaxWeightedChars);
    }

    /// <summary>
    /// The weighted length X will measure this text at: URLs 23 apiece, then per grapheme —
    /// an emoji sequence is one element of 2 however many codepoints compose it, anything else
    /// is the sum of its runes' range weights. Counted on the NFC form, which is what X counts.
    /// </summary>
    public static int WeightedLength(string text)
    {
        text = text.Normalize(NormalizationForm.FormC);
        var total = 0;
        var consumed = 0;
        foreach (Match url in UrlPattern().Matches(text))
        {
            total += SegmentWeight(text[consumed..url.Index]) + UrlWeight;
            consumed = url.Index + url.Length;
        }
        return total + SegmentWeight(text[consumed..]);
    }

    private static int SegmentWeight(string segment)
    {
        var total = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(segment);
        while (enumerator.MoveNext())
            total += GraphemeWeight((string)enumerator.Current);
        return total;
    }

    private static int GraphemeWeight(string grapheme)
    {
        var weight = 0;
        foreach (var rune in grapheme.EnumerateRunes())
        {
            // Any emoji-ish rune makes the whole grapheme one weight-2 element: a ZWJ family
            // sequence is 7 codepoints but X charges it as a single emoji.
            if (rune.Value is >= 0x1F000 or (>= 0x2600 and <= 0x27BF) or 0xFE0F or 0x200D)
                return 2;
            weight += IsLightRange(rune.Value) ? 1 : 2;
        }
        return weight;
    }

    // twitter-text config v3: the ranges that weigh 100 (=1); the default for everything else is
    // 200 (=2). 0–4351 covers Latin, Cyrillic, Greek, Hebrew, Arabic; the small ranges are general
    // punctuation. Notably NOT here: CJK, emoji.
    private static bool IsLightRange(int codepoint) => codepoint
        is (>= 0 and <= 4351)
        or (>= 8192 and <= 8205)
        or (>= 8208 and <= 8223)
        or (>= 8242 and <= 8247);

    /// <summary>
    /// Truncates to the weighted budget on a grapheme boundary, marking the cut. No URL awareness
    /// on purpose: this only ever cuts the body (teaser or override), and a body long enough to be
    /// cut mid-URL is over budget with or without the rewrite discount.
    /// </summary>
    public static string TruncateToWeight(string text, int maxWeight)
    {
        if (maxWeight <= 0) return "";
        if (WeightedLength(text) <= maxWeight) return text;
        if (maxWeight <= EllipsisWeight) return Ellipsis;

        text = text.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder();
        var spent = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var weight = GraphemeWeight(element);
            if (spent + weight > maxWeight - EllipsisWeight) break;
            builder.Append(element);
            spent += weight;
        }
        return builder.ToString().TrimEnd() + Ellipsis;
    }
}
