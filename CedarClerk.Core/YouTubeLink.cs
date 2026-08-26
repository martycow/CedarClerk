namespace CedarClerk.Core;

/// <summary>
/// The video id inside a YouTube link, in the four shapes people actually paste. The editor's own
/// youtube node stores an id because the front end parsed it; a trailer URL is typed into a field
/// on the server side of the wire, so the same parsing has to exist here (ADR-216).
/// </summary>
public static class YouTubeLink
{
    /// <summary>The id, or null when the link is not a YouTube video.</summary>
    public static string? VideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        var id = host.ToLowerInvariant() switch
        {
            "youtu.be" => segments.FirstOrDefault(),
            "youtube.com" or "m.youtube.com" or "youtube-nocookie.com" => segments switch
            {
                ["watch"] => QueryValue(uri.Query, "v"),
                ["embed", var embedded, ..] => embedded,
                ["shorts", var short_, ..] => short_,
                ["live", var live, ..] => live,
                _ => null,
            },
            _ => null,
        };

        return IsId(id) ? id : null;
    }

    /// <summary>The nocookie embed the blog renderer already uses, or null for a link that is not one.</summary>
    public static string? EmbedUrl(string? url) =>
        VideoId(url) is { } id ? $"https://www.youtube-nocookie.com/embed/{id}" : null;

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == key) return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }

    // An id is what YouTube hands out and nothing else: it goes straight into an iframe src, so a
    // string with a slash or a quote in it must never reach that far.
    private static bool IsId(string? candidate) =>
        candidate is { Length: > 0 and <= 20 }
        && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
