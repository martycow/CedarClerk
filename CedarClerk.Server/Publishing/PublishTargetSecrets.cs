using Microsoft.AspNetCore.DataProtection;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// Encrypts the credentials a tenant hands over for their own social accounts (T-084).
///
/// These are somebody else's credentials sitting in a shared database — the first data in this
/// product that is neither the tenant's content nor Cedar Clerk's own secret. Storing them in
/// plaintext would mean a stolen `cedar.db` (or one of the fourteen nightly backup copies on a
/// microSD card) hands over every tenant's Bluesky session.
///
/// ASP.NET DataProtection rather than hand-rolled AES: the key ring already exists, already lives
/// under CEDAR_DATA_DIR and is therefore already backed up (T-074/ADR-066), and it already rotates
/// and reads older keys on its own. **The consequence to know**: the key ring and the database are
/// now a pair. Restoring `cedar.db` next to a lost or regenerated key ring leaves every stored
/// credential unreadable — recoverable only by the tenant reconnecting the account, never silently
/// wrong, which is why <see cref="TryUnprotect"/> returns null instead of throwing.
/// </summary>
public sealed class PublishTargetSecrets(IDataProtectionProvider provider)
{
    // Versioned on purpose: a future change of what is stored (say, from an app password to an
    // OAuth refresh token) gets a new purpose rather than reinterpreting old bytes as the new shape.
    public const string Purpose = "CedarClerk.PublishTarget.Credentials.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    /// <summary>
    /// Returns null when the payload cannot be read — a rotated-away key ring, a restored database
    /// from a different machine, or a tampered row. Callers surface "reconnect this account", which
    /// is the only honest recovery, rather than crashing a publish job.
    /// </summary>
    public string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            return null;

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
