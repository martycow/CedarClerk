namespace CedarClerk.Core;

/// <summary>
/// The rules a tenant name obeys. Used by the Host-header resolver and by registration, so that a
/// name which can be typed as a subdomain is exactly a name which can be registered.
/// </summary>
public static class Usernames
{
    public const int MaxLength = 63;

    /// <summary>Canonical spelling. A hostname is case-insensitive, so case is not an error.</summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>A DNS label: a-z, 0-9 and inner hyphens, at most 63 characters.</summary>
    public static bool IsValidFormat(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
        if (name[0] == '-' || name[^1] == '-') return false;

        foreach (var c in name)
            if (c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
                return false;

        return true;
    }

    public static bool IsReserved(string? name) =>
        name is not null && Consts.ReservedSubdomains.Contains(name.Trim().ToLowerInvariant());

    /// <summary>What registration asks before writing the name down.</summary>
    public static bool IsAssignable(string? name)
    {
        var normalized = Normalize(name);
        return IsValidFormat(normalized) && !IsReserved(normalized);
    }
}
