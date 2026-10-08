namespace CedarClerk.Core;

/// <summary>Why a name cannot be taken, or <see cref="Free"/> when it can.</summary>
public enum UsernameVerdict
{
    Free,
    Missing,
    Invalid,
    Reserved,
    /// <summary>Well-formed and allowed, but another account already holds it.</summary>
    Taken,
}

/// <summary>
/// The rules a tenant name obeys. Used by the Host-header resolver and by registration, so that a
/// name which can be typed as a subdomain is exactly a name which can be registered.
/// </summary>
public static class Usernames
{
    public const int MaxLength = 63;

    /// <summary>ADR-325: a name taken from now on is at most 16; older, longer names still resolve as hosts.</summary>
    public const int MaxNewLength = 16;

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

    /// <summary>
    /// Everything that can be decided without asking the database. <see cref="UsernameVerdict.Taken"/>
    /// is the caller's to add — it is the only verdict a rule cannot reach on its own.
    /// </summary>
    public static UsernameVerdict Check(string? name)
    {
        var normalized = Normalize(name);
        if (normalized is null) return UsernameVerdict.Missing;
        if (!IsValidFormat(normalized) || normalized.Length > MaxNewLength) return UsernameVerdict.Invalid;
        return IsReserved(normalized) ? UsernameVerdict.Reserved : UsernameVerdict.Free;
    }

    /// <summary>What registration asks before writing the name down.</summary>
    public static bool IsAssignable(string? name) => Check(name) == UsernameVerdict.Free;
}
