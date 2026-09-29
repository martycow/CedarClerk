using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Signing in with somebody else's account (T-003, ADR-237). Two providers, and they are not the
/// same shape: Google is an OAuth round trip that ends in Identity's external cookie, Telegram is a
/// signed widget payload posted straight here.
///
/// Three rules hold for both:
///
/// **The invite gate is not a side door.** A provider button that created accounts freely would
/// open registration to anyone holding a Google account while <c>/register</c> still asked for a
/// code. Signing IN is free; signing UP goes through <see cref="AuthEndpoints.ResolveInviteAsync"/>,
/// exactly as the password form does.
///
/// **An email that already belongs to somebody is never merged on the provider's word.** The
/// callback sends that person to the password form instead, and the link is added after they prove
/// the account is theirs. A provider's `email_verified` is a claim about their world, not ours, and
/// treating it as proof is how accounts get taken over.
///
/// **The server renders nothing here.** Every branch ends in a redirect to an SPA route carrying an
/// outcome, because the screens that ask for an invite code or a password already exist.
/// </summary>
public static class ExternalAuthEndpoints
{
    /// <summary>What the completion screen posts back once it has collected what was missing.</summary>
    public record CompleteRequest(string InviteCode, string Username, string? Email);

    /// <summary>The Telegram widget's payload, verified the same way <c>/telegram/link</c> verifies it.</summary>
    public record TelegramLoginRequest(
        long Id, string? FirstName, string? LastName, string? Username,
        string? PhotoUrl, long AuthDate, string Hash);

    public static void MapExternalAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/external");

        #region Google — the OAuth round trip

        group.MapGet("/{provider}", (string provider, string? returnUrl, HttpContext ctx, IConfiguration cfg) =>
        {
            var scheme = string.Equals(provider, DiscordAuthentication.Scheme, StringComparison.OrdinalIgnoreCase)
                && DiscordAuthentication.IsConfigured(cfg) ? DiscordAuthentication.Scheme : GoogleScheme(provider, cfg);
            if (scheme is null)
                return Results.Json(new { error = ErrorMessages.ExternalProviderNotConfigured },
                    statusCode: StatusCodes.Status501NotImplemented);

            // Same-origin paths only, exactly as the login screen reads it — an open redirect here
            // would be a phishing hop wearing our domain.
            var safeReturn = SafeReturnUrl(returnUrl);
            var callback = $"/api/auth/external/callback?returnUrl={Uri.EscapeDataString(safeReturn)}";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = callback }, [scheme]);
        });

        group.MapGet("/callback", async (string? returnUrl,
            SignInManager<ApplicationUser> signIn, CedarDbContext db) =>
        {
            var safeReturn = SafeReturnUrl(returnUrl);
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
                return Redirect(Consts.ExternalAuth.LoginRoute, Consts.ExternalAuth.OutcomeFailed, safeReturn);

            // The happy path, and the common one: this provider account is already known.
            var known = await signIn.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: true);
            if (known.Succeeded)
                return Results.LocalRedirect(safeReturn is { Length: > 0 } ? safeReturn : "/");

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Redirect(Consts.ExternalAuth.LoginRoute, Consts.ExternalAuth.OutcomeFailed, safeReturn);

            // An account already holds the address. Not merged here on the provider's word (clause 3):
            // the password is what proves it is the same person, and /login says so.
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant()))
                return Redirect(Consts.ExternalAuth.LoginRoute, Consts.ExternalAuth.OutcomeLink, safeReturn);

            // Nobody yet. The external cookie holds the identity while the completion screen asks
            // for the invite code and the account name — the two things a provider cannot give us.
            return Redirect(Consts.ExternalAuth.CompleteRoute, Consts.ExternalAuth.OutcomeNew, safeReturn);
        });

        #endregion

        #region Finishing a new account, and linking to an existing one

        // The identity is read back out of the external cookie rather than passed through the
        // browser, so nothing the client sends can change whose account this becomes.
        group.MapPost("/complete", async (CompleteRequest req, HttpContext ctx,
            SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
            CedarDbContext db, IServiceScopeFactory scopes, IConfiguration cfg,
            ProductAnalytics analytics) =>
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
                return Results.BadRequest(new { error = ErrorMessages.ExternalLoginExpired });

            var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? req.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest(new { error = ErrorMessages.ExternalNoEmail });

            var invite = await AuthEndpoints.ResolveInviteAsync(req.InviteCode, db, scopes, cfg);
            if (!invite.Admits)
                return Results.BadRequest(new { error = ErrorMessages.InvalidInviteCode });

            if (await AuthEndpoints.VerdictAsync(db, req.Username) is var verdict && verdict != UsernameVerdict.Free)
                return Results.BadRequest(new { error = AuthEndpoints.Refusal(verdict, Usernames.Normalize(req.Username)) });

            // Re-checked here and not only in the callback: the two are separate requests, and an
            // account can be made on that address in between.
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant()))
                return Results.Conflict(new { error = ErrorMessages.ExternalEmailTaken });

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                TenantUsername = Usernames.Normalize(req.Username),
                InviteCodeId = invite.Code?.Id,
                // The provider vouched for the address; asking them to confirm what Google just
                // confirmed is a mail nobody needs.
                EmailConfirmed = true,
            };

            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                return Results.BadRequest(new { errors = created.Errors.Select(e => e.Description) });

            var linked = await users.AddLoginAsync(user, info);
            if (!linked.Succeeded)
            {
                // Without the login row the account exists and cannot be reached by the button that
                // made it, which is worse than not having made it.
                await users.DeleteAsync(user);
                return Results.BadRequest(new { errors = linked.Errors.Select(e => e.Description) });
            }

            if (invite.Code is { } spent)
            {
                spent.Uses++;
                await db.SaveChangesAsync();
            }

            await signIn.SignInAsync(user, isPersistent: true);
            await ForgetExternalCookieAsync(ctx);

            analytics.Track(user.Id, Consts.Analytics.Events.SignupCompleted, new()
            {
                ["entry"] = invite.Kind switch
                {
                    AuthEndpoints.InviteKind.Invitation => "invitation",
                    AuthEndpoints.InviteKind.Code => "invite_code",
                    AuthEndpoints.InviteKind.ConfigCode => "config_code",
                    _ => "open",
                },
                ["provider"] = info.LoginProvider,
            });

            return Results.Ok(new { id = user.Id });
        });

        // The other half of clause 3: the password proved the account, so the pending provider
        // identity can now be attached to it.
        group.MapPost("/link", async (HttpContext ctx, SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users, ClaimsPrincipal principal, CedarDbContext db) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
                return Results.BadRequest(new { error = ErrorMessages.ExternalLoginExpired });

            var takenByOther = await db.UserLogins.AnyAsync(l =>
                l.LoginProvider == info.LoginProvider && l.ProviderKey == info.ProviderKey && l.UserId != user.Id);
            if (takenByOther)
                return Results.Conflict(new { error = ErrorMessages.ExternalAlreadyLinkedToOther });

            var result = await users.AddLoginAsync(user, info);
            await ForgetExternalCookieAsync(ctx);

            return result.Succeeded
                ? Results.Ok(new { provider = info.LoginProvider })
                : Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }).RequireAuthorization();

        // What the account screen lists, so somebody can see which buttons will let them back in.
        group.MapGet("/logins", async (UserManager<ApplicationUser> users, ClaimsPrincipal principal) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var logins = await users.GetLoginsAsync(user);
            var hasPassword = await users.HasPasswordAsync(user);
            return Results.Ok(new
            {
                hasPassword,
                logins = logins.Select(l => new { provider = l.LoginProvider, name = l.ProviderDisplayName }),
            });
        }).RequireAuthorization();

        group.MapDelete("/logins/{provider}", async (string provider, UserManager<ApplicationUser> users,
            ClaimsPrincipal principal) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            // Removing the last way in is the one thing this must refuse: an account with no password
            // and no provider is unreachable by its owner and recoverable only by hand.
            var logins = await users.GetLoginsAsync(user);
            var target = logins.FirstOrDefault(l => l.LoginProvider == provider);
            if (target is null) return Results.NotFound();
            if (!await users.HasPasswordAsync(user) && logins.Count == 1)
                return Results.Conflict(new { error = ErrorMessages.ExternalLastWayIn });

            var result = await users.RemoveLoginAsync(user, target.LoginProvider, target.ProviderKey);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest();
        }).RequireAuthorization();

        #endregion

        #region Telegram — a signed payload, not a redirect

        group.MapPost("/telegram", async (TelegramLoginRequest req, IConfiguration cfg,
            SignInManager<ApplicationUser> signIn, CedarDbContext db) =>
        {
            var botToken = cfg[Consts.Telegram.BotTokenCfg];
            if (string.IsNullOrEmpty(botToken))
                return Results.Json(new { error = ErrorMessages.BotNotRunningNoToken },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var data = new TelegramLoginData(req.Id, req.FirstName, req.LastName, req.Username, req.PhotoUrl, req.AuthDate, req.Hash);
            if (!TelegramLoginVerifier.Verify(data, botToken, DateTimeOffset.UtcNow))
                return Results.BadRequest(new { error = ErrorMessages.InvalidTelegramSignature });

            // One Telegram identity belongs to one account — the same unique index /telegram/link
            // relies on, read here as the whole of the sign-in.
            var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramUserId == req.Id);
            if (user is null)
            {
                // Telegram carries no email, and the account model needs one. Rather than invent an
                // address, the answer is honest: this button signs in, it does not sign up.
                return Results.Json(new { error = ErrorMessages.TelegramNoAccount },
                    statusCode: StatusCodes.Status404NotFound);
            }

            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(new { id = user.Id });
        });

        #endregion
    }

    public static string? GoogleScheme(string provider, IConfiguration cfg) =>
        string.Equals(provider, Consts.ExternalAuth.Google, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(cfg[Consts.ExternalAuth.GoogleClientIdCfg])
        && !string.IsNullOrWhiteSpace(cfg[Consts.ExternalAuth.GoogleClientSecretCfg])
            ? Consts.ExternalAuth.Google : null;

    /// <summary>Same-origin paths only — anything else is dropped rather than followed.</summary>
    private static string SafeReturnUrl(string? url) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//") ? url : "";

    private static IResult Redirect(string route, string outcome, string returnUrl)
    {
        var query = $"?external={outcome}";
        if (returnUrl.Length > 0) query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.LocalRedirect(route + query);
    }

    /// <summary>
    /// The external cookie has done its job once the identity is attached to an account; leaving it
    /// set would let a later request finish a sign-in that was already finished.
    ///
    /// Named scheme, deliberately: <c>SignOutAsync()</c> with no argument clears every scheme, the
    /// application cookie included — it would drop the session that was just created.
    /// </summary>
    private static Task ForgetExternalCookieAsync(HttpContext ctx) =>
        ctx.SignOutAsync(IdentityConstants.ExternalScheme);
}
