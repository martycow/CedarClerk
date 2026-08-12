namespace CedarClerk.Core;

// The two headers a blog reader arrives with, turned into BlogViewGeoDaily's bucket keys. Here and
// unit-tested rather than buried in BlogEndpoints because the headers are attacker-controlled: every
// value that is not a well-formed code has to land in one honest "unknown" bucket, not mint a row.
public static class ReaderGeo
{
    // Cloudflare's CF-IPCountry: alpha-2, "XX" when it cannot tell, "T1" for a Tor exit. A missing
    // header — a local run with nothing in front of Kestrel — collapses to unknown too.
    public static string NormalizeCountry(string? header)
    {
        var value = header?.Trim().ToUpperInvariant();
        if (value is null || value.Length != 2 || !value.All(char.IsAsciiLetterUpper)) return Consts.General.UnknownGeo;
        return value == "XX" ? Consts.General.UnknownGeo : value;
    }

    // Only the primary subtag: the question is which language the audience reads in, not which
    // regional dialect they run their browser in.
    public static string NormalizeLanguage(string? header)
    {
        var first = header?.Split(',')[0].Split(';')[0].Split('-')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(first) || first.Length > 8 || !first.All(char.IsAsciiLetterLower)) return Consts.General.UnknownGeo;
        return first;
    }
}
