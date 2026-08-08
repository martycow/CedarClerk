namespace CedarClerk.Core;

/// <summary>
/// Turns the two request headers a blog reader arrives with into the bucket keys of
/// BlogViewGeoDaily. Pure string work, so it lives here and is unit-tested rather than being
/// buried in BlogEndpoints — the headers are attacker-controlled, and every value that isn't a
/// well-formed code has to land in one honest "unknown" bucket instead of minting a row.
/// </summary>
public static class ReaderGeo
{
    /// <summary>
    /// Cloudflare's CF-IPCountry: a real alpha-2 code, "XX" when it can't tell, "T1" for a Tor
    /// exit. Anything that isn't two letters — including a missing header on a local run, where
    /// nothing sits in front of Kestrel — collapses to unknown.
    /// </summary>
    public static string NormalizeCountry(string? header)
    {
        var value = header?.Trim().ToUpperInvariant();
        if (value is null || value.Length != 2 || !value.All(char.IsAsciiLetterUpper)) return Consts.General.UnknownGeo;
        return value == "XX" ? Consts.General.UnknownGeo : value;
    }

    /// <summary>
    /// Accept-Language ("ru-RU,ru;q=0.9,en-US;q=0.8") to "ru" — the reader's top preference, and
    /// only its primary subtag: the question is which language the audience reads in, not which
    /// regional dialect they run their browser in.
    /// </summary>
    public static string NormalizeLanguage(string? header)
    {
        var first = header?.Split(',')[0].Split(';')[0].Split('-')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(first) || first.Length > 8 || !first.All(char.IsAsciiLetterLower)) return Consts.General.UnknownGeo;
        return first;
    }
}
