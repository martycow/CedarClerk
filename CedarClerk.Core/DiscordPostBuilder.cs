namespace CedarClerk.Core;

// One announcement message for a Discord channel webhook (T-161, ADR-131). Same ADR-077 model as
// Bluesky/X: the author's own text wins, the teaser is the fallback, and the blog link is worth
// more than the teaser's last paragraph. Discord unfurls the link into a card from the blog's OG
// tags, so the message itself stays plain text.
public static class DiscordPostBuilder
{
    // Discord's message limit is 2000 codepoints; UTF-16 length only over-counts astral symbols,
    // so measuring with it can never produce a message Discord refuses.
    public const int MaxChars = 2000;

    private const string Ellipsis = "…";

    public static string Build(string? authorText, string cedarJson, string? blogUrl)
    {
        var body = string.IsNullOrWhiteSpace(authorText) ? Teaser(cedarJson) : authorText.Trim();

        if (string.IsNullOrWhiteSpace(blogUrl))
            return Truncate(body, MaxChars);

        var reserve = blogUrl.Length + 2; // the blank line before the link
        body = Truncate(body, Math.Max(0, MaxChars - reserve));
        return body.Length == 0 ? blogUrl : body + "\n\n" + blogUrl;
    }

    // Opening paragraphs until the limit, so the announcement ends on a thought, not mid-word.
    public static string Teaser(string cedarJson)
    {
        var paragraphs = CedarPlainText.Paragraphs(cedarJson);

        var builder = new System.Text.StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length == 0) continue;
            var candidate = builder.Length == 0 ? paragraph : builder + "\n\n" + paragraph;
            if (candidate.Length > MaxChars) break;
            builder.Clear();
            builder.Append(candidate);
        }

        return builder.Length > 0 ? builder.ToString() : Truncate(paragraphs.FirstOrDefault() ?? "", MaxChars);
    }

    public static string Truncate(string text, int maxChars)
    {
        if (maxChars <= 0) return "";
        if (text.Length <= maxChars) return text;
        if (maxChars == 1) return Ellipsis;
        return text[..(maxChars - 1)].TrimEnd() + Ellipsis;
    }
}
