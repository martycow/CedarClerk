using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CedarClerk.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace CedarClerk.Server;

/// <summary>
/// Carries a session from the system browser to the desktop shell (ADR-327). The two share no
/// cookies, and a provider round trip can only finish in the browser.
/// </summary>
public static class DesktopAuthEndpoints
{
    /// <summary>
    /// A page can post a form to the redeem endpoint but cannot put a header on a navigation; the
    /// shell can. Without this, a link could sign a visitor into somebody else's account.
    /// </summary>
    public const string ShellHeader = "X-Cedar-Desktop";

    public record CodeRequest(string? Challenge);

    public static void MapDesktopAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/desktop");

        group.MapPost("/code", (CodeRequest req, HttpContext ctx,
            UserManager<ApplicationUser> users, DesktopHandoffCodes codes) =>
        {
            var userId = users.GetUserId(ctx.User);
            if (userId is null) return Results.Unauthorized();
            return DesktopHandoffCodes.IsChallenge(req.Challenge)
                ? Results.Ok(new { code = codes.Mint(userId, req.Challenge!) })
                : Results.BadRequest();
        }).RequireAuthorization();

        group.MapPost("/redeem", async (HttpContext ctx, DesktopHandoffCodes codes,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            if (!ctx.Request.Headers.ContainsKey(ShellHeader) || !ctx.Request.HasFormContentType)
                return Failed();

            var form = await ctx.Request.ReadFormAsync();
            var user = await ResolveAsync(form["code"], form["verifier"], codes, users);
            if (user is null || !await signIn.CanSignInAsync(user))
                return Failed();

            await signIn.SignInAsync(user, isPersistent: true);
            return Results.LocalRedirect(SafeReturnUrl(form["returnUrl"]));
        }).AllowAnonymous();
    }

    public static async Task<ApplicationUser?> ResolveAsync(string? code, string? verifier,
        DesktopHandoffCodes codes, UserManager<ApplicationUser> users) =>
        codes.Redeem(code, verifier) is { } userId ? await users.FindByIdAsync(userId) : null;

    public static string SafeReturnUrl(string? url) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//") ? url : "/";

    private static IResult Failed() => Results.LocalRedirect(
        $"{Consts.ExternalAuth.LoginRoute}?external={Consts.ExternalAuth.OutcomeFailed}");
}

/// <summary>
/// In memory on purpose (ADR-327): one process serves production, and a restart inside a code's
/// two minutes costs one retry.
/// </summary>
public sealed class DesktopHandoffCodes(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private const int ChallengeLength = 43;
    private const int MaxVerifierLength = 128;

    private sealed record Pending(string UserId, string Challenge, DateTimeOffset Expires);

    private readonly ConcurrentDictionary<string, Pending> live = new();

    /// <summary>An unpadded base64url SHA-256, which is what the shell sends.</summary>
    public static bool IsChallenge(string? value) =>
        value is { Length: ChallengeLength }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public string Mint(string userId, string challenge)
    {
        var now = time.GetUtcNow();
        foreach (var (key, pending) in live)
            if (pending.Expires <= now || pending.UserId == userId) live.TryRemove(key, out _);

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        live[code] = new Pending(userId, challenge, now + Lifetime);
        return code;
    }

    /// <summary>The account the code was minted for, or null. Any attempt spends the code.</summary>
    public string? Redeem(string? code, string? verifier)
    {
        if (string.IsNullOrEmpty(code)
            || verifier is not { Length: >= ChallengeLength and <= MaxVerifierLength }) return null;
        if (!live.TryRemove(code, out var pending) || pending.Expires <= time.GetUtcNow()) return null;

        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(challenge), Encoding.UTF8.GetBytes(pending.Challenge))
            ? pending.UserId
            : null;
    }
}
