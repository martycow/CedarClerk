using System.Security.Cryptography;
using System.Text;

namespace CedarClerk.Core;

/// <summary>
/// For Stripe verifications
/// </summary>
public static class StripeWebhookVerifier
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static bool Verify(string payload, string? signatureHeader, string secret, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(secret))
            return false;

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) 
                continue;
            
            var key = kv[0].Trim();
            var value = kv[1].Trim();
            
            switch (key)
            {
                case "t" when long.TryParse(value, out var ts):
                    timestamp = ts;
                    break;
                case "v1":
                    signatures.Add(value);
                    break;
            }
        }

        if (timestamp is null || signatures.Count == 0)
            return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        var maxAge = tolerance ?? DefaultTolerance;
        if (age > maxAge || age < -maxAge)
            return false;

        var signedPayload = $"{timestamp.Value}.{payload}";
        var expected = Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret), 
                Encoding.UTF8.GetBytes(signedPayload)))
            .ToLowerInvariant();

        foreach (var candidate in signatures)
        {
            var candidateBytes = Encoding.UTF8.GetBytes(candidate.ToLowerInvariant());
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            
            if (candidateBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes))
                return true;
        }
        return false;
    }
}
