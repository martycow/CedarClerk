using CedarClerk.Core;
using CedarClerk.Server.Publishing;
using Microsoft.AspNetCore.DataProtection;

namespace CedarClerk.Tests;

// T-084 / ADR-078. These credentials are the first data in the product that belongs to a third
// party — a tenant's own social account — sitting in a database that is copied to a microSD card
// every night. The tests below are about the two things that can go wrong with that: it comes back
// unchanged, and an unreadable blob fails as null rather than as an exception in a Quartz job.
public class PublishTargetSecretsTests
{
    private static PublishTargetSecrets Make(string appName = "cedar-test") =>
        new(DataProtectionProvider.Create(appName));

    [Fact]
    public void Protected_value_round_trips()
    {
        var secrets = Make();
        const string credentials = """{"handle":"marty.bsky.social","appPassword":"abcd-efgh-ijkl-mnop"}""";

        var stored = secrets.Protect(credentials);

        Assert.NotEqual(credentials, stored);
        Assert.DoesNotContain("abcd-efgh", stored);
        Assert.Equal(credentials, secrets.TryUnprotect(stored));
    }

    [Fact]
    public void Unicode_and_long_payloads_survive()
    {
        var secrets = Make();
        var credentials = "{\"note\":\"пароль от аккаунта — не потерять\",\"token\":\"" + new string('x', 4000) + "\"}";

        Assert.Equal(credentials, secrets.TryUnprotect(secrets.Protect(credentials)));
    }

    [Fact]
    public void Tampered_payload_reads_as_null_rather_than_throwing()
    {
        var secrets = Make();
        var stored = secrets.Protect("secret");

        // Flip one character in the middle — what a corrupted row or a partial restore looks like.
        var chars = stored.ToCharArray();
        chars[stored.Length / 2] = chars[stored.Length / 2] == 'A' ? 'B' : 'A';

        Assert.Null(secrets.TryUnprotect(new string(chars)));
    }

    [Fact]
    public void Garbage_and_empty_values_read_as_null()
    {
        var secrets = Make();

        Assert.Null(secrets.TryUnprotect(null));
        Assert.Null(secrets.TryUnprotect(""));
        Assert.Null(secrets.TryUnprotect("not-protected-at-all"));
    }

    // The scenario this stands in for: `cedar.db` restored beside a key ring that is not the one it
    // was written with. The credentials must be unreadable — and must say so by returning null, so
    // the owner is told to reconnect instead of a publish job dying.
    [Fact]
    public void A_different_key_ring_cannot_read_the_payload()
    {
        var stored = Make("cedar-machine-a").Protect("secret");

        Assert.Null(Make("cedar-machine-b").TryUnprotect(stored));
    }

    [Fact]
    public void Purpose_is_versioned_so_a_future_credential_shape_cannot_be_read_as_the_old_one()
    {
        Assert.EndsWith(".v1", PublishTargetSecrets.Purpose);
    }
}

public class PublishNetworkTests
{
    [Theory]
    [InlineData(PublishNetworks.Telegram)]
    [InlineData(PublishNetworks.Bluesky)]
    public void Known_networks_are_recognised(string network) => Assert.True(PublishNetworks.IsKnown(network));

    [Theory]
    [InlineData("Telegram")]
    [InlineData("mastodon")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not(string? network) => Assert.False(PublishNetworks.IsKnown(network));
}
