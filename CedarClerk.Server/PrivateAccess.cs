using CedarClerk.Core;
using Microsoft.AspNetCore.DataProtection;

namespace CedarClerk.Server;

/// <summary>
/// Who may read a private post (T-023/T-064).
///
/// Two things were wrong with the cookie this replaces. It said <c>"1"</c> — presence alone, so it
/// carried no proof and could be forged by anyone who knew a draft's id. And it lived only in the
/// browser that filled in the form, so a reader who opened the post in Telegram's in-app browser
/// and then in Chrome was asked to register twice. That was the reported incident, and the two
/// facts have one fix: the grant becomes a signed value, and the thing that grants it travels in
/// the link.
///
/// Signed with DataProtection, like the publish credentials — the key ring already exists, already
/// lives under CEDAR_DATA_DIR and is already backed up (T-074).
/// </summary>
public sealed class PrivateAccess(IDataProtectionProvider provider)
{
    public const string Purpose = "CedarClerk.PrivateAccess.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    /// <summary>What goes in the cookie: proof that this browser was let into this post.</summary>
    public string Grant(Guid draftId, string token) => _protector.Protect($"{draftId:N}:{token}");

    /// <summary>
    /// True when the cookie really was issued by this server for this post. A cookie from another
    /// post, a hand-written one, or one from a database restored beside a different key ring all
    /// read as "no access" — which shows the gate again rather than letting anyone through.
    /// </summary>
    public bool IsValid(string? cookieValue, Guid draftId, out string token)
    {
        token = "";
        if (string.IsNullOrEmpty(cookieValue)) return false;

        string plaintext;
        try
        {
            plaintext = _protector.Unprotect(cookieValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }

        var separator = plaintext.IndexOf(':');
        if (separator <= 0) return false;
        if (plaintext[..separator] != draftId.ToString("N")) return false;

        token = plaintext[(separator + 1)..];
        return true;
    }

    /// <summary>
    /// A reader's own key, handed back in the redirect after the form is submitted. Long enough
    /// that guessing is not a strategy, and URL-safe because it lives in a link people paste.
    /// </summary>
    public static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string CookieName(Guid draftId) => Consts.General.PrivateAccessCookiePrefix + draftId;
}
