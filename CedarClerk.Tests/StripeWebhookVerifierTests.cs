using System.Security.Cryptography;
using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

public class StripeWebhookVerifierTests
{
    private const string Secret = "whsec_test_secret_123";
    private const string Payload = """{"type":"checkout.session.completed","data":{"object":{}}}""";

    private static string Sign(string payload, long timestamp, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

    [Fact]
    public void Valid_signature_passes()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var header = $"t={ts},v1={Sign(Payload, ts, Secret)}";
        Assert.True(StripeWebhookVerifier.Verify(Payload, header, Secret, now));
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var header = $"t={ts},v1={Sign(Payload, ts, Secret)}";
        Assert.False(StripeWebhookVerifier.Verify(Payload + " ", header, Secret, now));
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var header = $"t={ts},v1={Sign(Payload, ts, "whsec_other")}";
        Assert.False(StripeWebhookVerifier.Verify(Payload, header, Secret, now));
    }

    [Fact]
    public void Stale_timestamp_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.AddMinutes(-10).ToUnixTimeSeconds();
        var header = $"t={ts},v1={Sign(Payload, ts, Secret)}";
        Assert.False(StripeWebhookVerifier.Verify(Payload, header, Secret, now));
    }

    [Fact]
    public void Multiple_v1_signatures_any_match_passes()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var header = $"t={ts},v1=deadbeef,v1={Sign(Payload, ts, Secret)}";
        Assert.True(StripeWebhookVerifier.Verify(Payload, header, Secret, now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=notanumber,v1=abc")]
    public void Malformed_header_fails(string? header)
    {
        Assert.False(StripeWebhookVerifier.Verify(Payload, header, Secret, DateTimeOffset.UtcNow));
    }
}
