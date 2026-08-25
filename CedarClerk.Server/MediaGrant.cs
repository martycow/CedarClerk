using Microsoft.AspNetCore.DataProtection;

namespace CedarClerk.Server;

/// <summary>
/// A short-lived key to one file, for a reader that has no session and no invite: Telegram's
/// fetcher, which is anonymous and pulls media by URL while a post is being sent.
///
/// The alternative was to keep every unclaimed file readable so that the fetcher could reach the
/// one it needs, which is what T-285 was. A grant is narrower in both directions — it names the
/// file, and it stops mattering minutes later.
/// </summary>
public sealed class MediaGrant(IDataProtectionProvider provider)
{
    public const string QueryKey = "k";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector protector =
        provider.CreateProtector("CedarClerk.MediaGrant.v1").ToTimeLimitedDataProtector();

    public string Issue(string fileName) => protector.Protect(fileName, Lifetime);

    public bool Allows(string? token, string fileName)
    {
        if (string.IsNullOrEmpty(token)) return false;

        try
        {
            return protector.Unprotect(token) == fileName;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}
