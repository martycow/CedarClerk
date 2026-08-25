using CedarClerk.Core;

namespace CedarClerk.Server.Tenancy;

public enum TenantHostKind
{
    /// <summary>Some other host entirely — the app host, the blog host, localhost.</summary>
    NotTenantDomain,
    /// <summary>The tenant domain itself, with no subdomain.</summary>
    Apex,
    /// <summary>A subdomain the platform keeps for itself.</summary>
    Reserved,
    /// <summary>Under the tenant domain, but no name that could ever be assigned.</summary>
    Invalid,
    Tenant,
}

public readonly record struct TenantHostResult(TenantHostKind Kind, string? Username);

/// <summary>
/// Which tenant a Host header names, decided without touching the database.
/// </summary>
public static class TenantHost
{
    public static TenantHostResult Resolve(string? host, string tenantDomain)
    {
        var normalized = host?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return new(TenantHostKind.NotTenantDomain, null);

        var domain = tenantDomain.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized == domain) return new(TenantHostKind.Apex, null);

        // The dot is part of the suffix, so "notcedarclerk.app" does not match "cedarclerk.app".
        var suffix = "." + domain;
        if (!normalized.EndsWith(suffix, StringComparison.Ordinal))
            return new(TenantHostKind.NotTenantDomain, null);

        var label = normalized[..^suffix.Length];
        if (label.Contains('.')) return new(TenantHostKind.Invalid, null);
        if (Usernames.IsReserved(label)) return new(TenantHostKind.Reserved, null);
        if (!Usernames.IsValidFormat(label)) return new(TenantHostKind.Invalid, null);

        return new(TenantHostKind.Tenant, label);
    }
}
