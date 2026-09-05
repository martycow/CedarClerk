using System.Net;
using System.Text;

namespace CedarClerk.Core;

/// <summary>
/// What a page is allowed to reveal to link-preview crawlers (ADR-124). A function of the post's
/// own privacy flags, never of the reader's authorization — a crawler holding a valid invite
/// cookie is still a crawler.
/// </summary>
public enum OgMetaPolicy
{
    /// <summary>Private, unlisted: the page emits no meta at all.</summary>
    None,

    /// <summary>Semi-public: title and the static fallback image only — no description, no cover,
    /// no times, no language cluster.</summary>
    TitleImageOnly,

    Full,
}

public sealed record OgMetaInput(
    string Title,
    string? Description,
    string CanonicalUrl,
    string? ImageUrl,
    int? ImageWidth,
    int? ImageHeight,
    string SiteName,
    string Locale,
    IReadOnlyList<(string Lang, string Url)> Alternates,
    string? XDefaultUrl,
    DateTime? PublishedUtc,
    DateTime? ModifiedUtc,
    bool IsArticle);

public static class OgMetaBuilder
{
    public const int DescriptionMaxLength = 200;

    // og:locale wants territory-qualified codes; a bare "ru" is tolerated but the canonical forms
    // cost one lookup. Covers exactly the content languages the product ships.
    private static readonly Dictionary<string, string> OgLocales = new()
    {
        ["ru"] = "ru_RU", ["en"] = "en_US", ["de"] = "de_DE", ["fr"] = "fr_FR", ["es"] = "es_ES",
        ["ja"] = "ja_JP", ["uk"] = "uk_UA", ["be"] = "be_BY", ["ka"] = "ka_GE",
    };

    public static string Build(OgMetaInput input, OgMetaPolicy policy)
    {
        if (policy == OgMetaPolicy.None)
            return "";

        var sb = new StringBuilder();
        var title = WebUtility.HtmlEncode(input.Title);
        var url = WebUtility.HtmlEncode(input.CanonicalUrl);

        sb.Append("<link rel=\"canonical\" href=\"").Append(url).Append("\">\n");
        sb.Append("<meta property=\"og:type\" content=\"").Append(input.IsArticle ? "article" : "website").Append("\">\n");
        sb.Append("<meta property=\"og:title\" content=\"").Append(title).Append("\">\n");
        sb.Append("<meta property=\"og:url\" content=\"").Append(url).Append("\">\n");
        sb.Append("<meta property=\"og:site_name\" content=\"").Append(WebUtility.HtmlEncode(input.SiteName)).Append("\">\n");
        sb.Append("<meta property=\"og:locale\" content=\"").Append(OgLocales.GetValueOrDefault(input.Locale, input.Locale)).Append("\">\n");

        if (input.ImageUrl is { Length: > 0 })
        {
            var image = WebUtility.HtmlEncode(input.ImageUrl);
            sb.Append("<meta property=\"og:image\" content=\"").Append(image).Append("\">\n");
            if (input.ImageWidth is { } w)
                sb.Append("<meta property=\"og:image:width\" content=\"").Append(w).Append("\">\n");
            if (input.ImageHeight is { } h)
                sb.Append("<meta property=\"og:image:height\" content=\"").Append(h).Append("\">\n");
        }

        sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">\n");
        sb.Append("<meta name=\"twitter:title\" content=\"").Append(title).Append("\">\n");
        if (input.ImageUrl is { Length: > 0 })
            sb.Append("<meta name=\"twitter:image\" content=\"").Append(WebUtility.HtmlEncode(input.ImageUrl)).Append("\">\n");

        if (policy == OgMetaPolicy.TitleImageOnly)
            return sb.ToString();

        if (TruncateAtWord(input.Description) is { Length: > 0 } description)
        {
            var encoded = WebUtility.HtmlEncode(description);
            sb.Append("<meta property=\"og:description\" content=\"").Append(encoded).Append("\">\n");
            sb.Append("<meta name=\"twitter:description\" content=\"").Append(encoded).Append("\">\n");
        }

        if (input.IsArticle)
        {
            if (input.PublishedUtc is { } published)
                sb.Append("<meta property=\"article:published_time\" content=\"").Append(published.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append("\">\n");
            if (ClampModified(input.PublishedUtc, input.ModifiedUtc) is { } modified)
                sb.Append("<meta property=\"article:modified_time\" content=\"").Append(modified.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append("\">\n");
        }

        foreach (var (altLang, altUrl) in input.Alternates)
        {
            sb.Append("<link rel=\"alternate\" hreflang=\"").Append(WebUtility.HtmlEncode(altLang))
              .Append("\" href=\"").Append(WebUtility.HtmlEncode(altUrl)).Append("\">\n");
            if (altLang != input.Locale && OgLocales.TryGetValue(altLang, out var ogAlt))
                sb.Append("<meta property=\"og:locale:alternate\" content=\"").Append(ogAlt).Append("\">\n");
        }

        if (input.XDefaultUrl is { Length: > 0 })
            sb.Append("<link rel=\"alternate\" hreflang=\"x-default\" href=\"").Append(WebUtility.HtmlEncode(input.XDefaultUrl)).Append("\">\n");

        return sb.ToString();
    }

    /// <summary>The draft's UpdatedAt predates its publication on first publish (the last save came
    /// before the publish click), so an unclamped value advertises a modification before the
    /// publication — the same clamp the JSON-LD Article applies, so both surfaces of one page agree.</summary>
    public static DateTime? ClampModified(DateTime? publishedUtc, DateTime? modifiedUtc)
    {
        if (modifiedUtc is not { } modified)
            return null;
        return publishedUtc is { } floor && modified < floor ? floor : modified;
    }

    /// <summary>Cuts at the last word boundary inside the cap — a preview that ends mid-word reads
    /// as broken. Runs on plain text before encoding, so no entity can be split.</summary>
    public static string? TruncateAtWord(string? text, int max = DescriptionMaxLength)
    {
        if (text is null)
            return null;
        var trimmed = text.Trim();
        if (trimmed.Length <= max)
            return trimmed;

        var cut = trimmed[..max];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 0)
            cut = cut[..lastSpace];
        return cut.TrimEnd() + "…";
    }
}
