namespace CedarClerk.Core;

/// <summary>
/// A project's own domain, reduced to the bare host the Host header will carry (T-300, ADR-216).
/// People paste what their browser shows them — a scheme, a trailing slash, sometimes a whole URL —
/// and the middleware compares against <c>Request.Host.Host</c>, which has none of that.
/// </summary>
public static class ShowcaseDomain
{
    /// <summary>Null host with <c>Rejected: false</c> means "no domain", which is the normal case.</summary>
    public readonly record struct Result(string? Host, bool Rejected);

    public static Result Normalize(string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return new Result(null, false);

        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return new Result(null, true);
            text = uri.Host;
        }

        text = text.Split('/')[0].Trim().TrimEnd('.').ToLowerInvariant();
        if (text.StartsWith("www.", StringComparison.Ordinal)) text = text[4..];
        if (text.Length == 0 || text.Length > Consts.Showcase.CustomDomainMaxLength) return new Result(null, true);

        var labels = text.Split('.');
        if (labels.Length < 2) return new Result(null, true);
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63) return new Result(null, true);
            if (label.StartsWith('-') || label.EndsWith('-')) return new Result(null, true);
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return new Result(null, true);
        }

        return new Result(text, false);
    }
}
