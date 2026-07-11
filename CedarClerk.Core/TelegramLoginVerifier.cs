using System.Security.Cryptography;
using System.Text;

namespace CedarClerk.Core;

public record TelegramLoginData(
    long Id,
    string? FirstName,
    string? LastName, 
    string? Username,
    string? PhotoUrl,
    long AuthDate,
    string Hash);

/// <summary>
/// For Telegram Login Widget 
/// </summary>
public static class TelegramLoginVerifier
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static bool Verify(TelegramLoginData data, string botToken, DateTimeOffset now)
    {
        var authDate = DateTimeOffset.FromUnixTimeSeconds(data.AuthDate);
        if (authDate > now || now - authDate > MaxAge) 
            return false;

        if (string.IsNullOrEmpty(data.Hash)) 
            return false;

        var fields = new (string Key, string? Value)[]
        {
            ("auth_date", data.AuthDate.ToString()),
            ("first_name", data.FirstName),
            ("id", data.Id.ToString()),
            ("last_name", data.LastName),
            ("photo_url", data.PhotoUrl),
            ("username", data.Username),
        };

        var dataCheckString = string.Join('\n', fields
            .Where(f => !string.IsNullOrEmpty(f.Value))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={f.Value}"));

        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var computedHash = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));

        byte[] providedHash;
        try
        {
            providedHash = Convert.FromHexString(data.Hash);
        }
        catch (FormatException)
        {
            return false;
        }

        return computedHash.Length == providedHash.Length && CryptographicOperations.FixedTimeEquals(computedHash, providedHash);
    }
}
