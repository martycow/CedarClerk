using CedarClerk.Core;

namespace CedarClerk.Tests;

public class TelegramLoginVerifierTests
{
    // Test vector cross-checked independently via `openssl dgst -sha256 -mac HMAC`:
    // secret = SHA256("TESTBOTTOKEN123"); dataCheckString = "auth_date=1700000000\nfirst_name=Marty\nid=12345\nusername=martycow"
    private const string BotToken = "TESTBOTTOKEN123";
    private const string ValidHash = "3a967525aaecbd8014318d93d15a4a4f16671e10bd7cf308d108469044d0ce35";
    private static readonly DateTimeOffset AuthTime = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    private static TelegramLoginData ValidData(string hash = ValidHash) =>
        new(Id: 12345, FirstName: "Marty", LastName: null, Username: "martycow", PhotoUrl: null, AuthDate: 1700000000, Hash: hash);

    [Fact]
    public void Valid_signature_within_window_is_accepted()
    {
        Assert.True(TelegramLoginVerifier.Verify(ValidData(), BotToken, AuthTime.AddHours(1)));
    }

    [Fact]
    public void Tampered_hash_is_rejected()
    {
        var tampered = ValidHash[..^1] + (ValidHash[^1] == '0' ? '1' : '0');
        Assert.False(TelegramLoginVerifier.Verify(ValidData(tampered), BotToken, AuthTime.AddHours(1)));
    }

    [Fact]
    public void Tampered_field_is_rejected()
    {
        var data = ValidData() with { FirstName = "Eve" };
        Assert.False(TelegramLoginVerifier.Verify(data, BotToken, AuthTime.AddHours(1)));
    }

    [Fact]
    public void Wrong_bot_token_is_rejected()
    {
        Assert.False(TelegramLoginVerifier.Verify(ValidData(), "SOME_OTHER_TOKEN", AuthTime.AddHours(1)));
    }

    [Fact]
    public void Auth_date_older_than_24h_is_rejected()
    {
        Assert.False(TelegramLoginVerifier.Verify(ValidData(), BotToken, AuthTime.AddHours(25)));
    }

    [Fact]
    public void Auth_date_in_the_future_is_rejected()
    {
        Assert.False(TelegramLoginVerifier.Verify(ValidData(), BotToken, AuthTime.AddMinutes(-1)));
    }

    [Fact]
    public void Malformed_hash_does_not_throw()
    {
        Assert.False(TelegramLoginVerifier.Verify(ValidData("not-hex!"), BotToken, AuthTime.AddHours(1)));
    }
}
