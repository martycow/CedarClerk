using System.Security.Cryptography;
using System.Text;
using CedarClerk.Server;
using Microsoft.AspNetCore.WebUtilities;

namespace CedarClerk.Tests;

// ADR-327 — the code that carries a browser session to the desktop shell.
public class DesktopHandoffTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const string Verifier = "desktop-shell-verifier-of-at-least-forty-three-chars";

    private static string ChallengeOf(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    [Fact]
    public void A_code_returns_its_account_to_the_holder_of_the_verifier_once()
    {
        var codes = new DesktopHandoffCodes(new Clock());
        var code = codes.Mint("user-1", ChallengeOf(Verifier));

        Assert.Equal("user-1", codes.Redeem(code, Verifier));
        Assert.Null(codes.Redeem(code, Verifier));
    }

    [Fact]
    public void A_wrong_verifier_gets_nothing_and_spends_the_code()
    {
        var codes = new DesktopHandoffCodes(new Clock());
        var code = codes.Mint("user-1", ChallengeOf(Verifier));

        Assert.Null(codes.Redeem(code, Verifier + "x"));
        Assert.Null(codes.Redeem(code, Verifier));
    }

    [Fact]
    public void A_code_dies_with_its_lifetime()
    {
        var clock = new Clock();
        var codes = new DesktopHandoffCodes(clock);
        var code = codes.Mint("user-1", ChallengeOf(Verifier));

        clock.Now += DesktopHandoffCodes.Lifetime;
        Assert.Null(codes.Redeem(code, Verifier));
    }

    [Fact]
    public void Asking_again_replaces_the_account_s_previous_code_and_nobody_else_s()
    {
        var codes = new DesktopHandoffCodes(new Clock());
        var challenge = ChallengeOf(Verifier);
        var first = codes.Mint("user-1", challenge);
        var other = codes.Mint("user-2", challenge);
        var second = codes.Mint("user-1", challenge);

        Assert.Null(codes.Redeem(first, Verifier));
        Assert.Equal("user-2", codes.Redeem(other, Verifier));
        Assert.Equal("user-1", codes.Redeem(second, Verifier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void Missing_or_malformed_input_is_refused(string? value)
    {
        var codes = new DesktopHandoffCodes(new Clock());
        var code = codes.Mint("user-1", ChallengeOf(Verifier));

        Assert.Null(codes.Redeem(value, Verifier));
        Assert.Null(codes.Redeem(code, value));
    }

    [Fact]
    public void Only_an_unpadded_base64url_sha256_is_a_challenge()
    {
        Assert.True(DesktopHandoffCodes.IsChallenge(ChallengeOf(Verifier)));
        Assert.False(DesktopHandoffCodes.IsChallenge(null));
        Assert.False(DesktopHandoffCodes.IsChallenge(ChallengeOf(Verifier)[1..]));
        Assert.False(DesktopHandoffCodes.IsChallenge(new string('+', 43)));
    }

    [Theory]
    [InlineData("/projects", "/projects")]
    [InlineData("/editor?id=1", "/editor?id=1")]
    [InlineData(null, "/")]
    [InlineData("https://example.test/", "/")]
    [InlineData("//example.test/", "/")]
    public void The_return_path_stays_on_this_origin(string? asked, string expected) =>
        Assert.Equal(expected, DesktopAuthEndpoints.SafeReturnUrl(asked));
}
