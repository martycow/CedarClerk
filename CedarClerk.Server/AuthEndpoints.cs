using System.Security.Claims;
using System.Text.Json.Serialization;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class AuthEndpoints
{
    public record RegisterRequest(string Email, string Password, string InviteCode);
    public record LoginRequest(string Email, string Password);
    public record SignatureRequest(string? Signature, string? SignatureUrl = null);
    public record ProfileRequest(
        string? AuthorDisplayName, string? ProfileUrl, string? ProfileLocation,
        string? HeaderSlot1Type, string? HeaderSlot2Type, string? HeaderSlot3Type,
        string? SocialTwitterUrl = null, string? SocialInstagramUrl = null, string? SocialFacebookUrl = null,
        string? SocialYoutubeUrl = null, string? SocialGithubUrl = null,
        string? BlogLinkText = null, string? TelegramLinkText = null,
        // Which language the two texts above are for. Absent means the primary one, so an older
        // client keeps writing exactly what it used to.
        string? LinkTextLanguage = null);
    public record NotificationPrefsRequest(bool NotifyOnEngagement);
    public record ToolbarLayoutRequest(string? LayoutJson);
    public record AppearanceRequest(string? PrefsJson);
    public record NewDraftDefaultsRequest(string? DefaultsJson);
    public record UiLanguageRequest(string? UiLanguage);
    public record AvatarRequest(string? AvatarUrl);

    // Arbitrary client-authored JSON blobs (ADR-035) — generous but bounded so a misbehaving
    // client can't grow AspNetUsers rows unbounded.
    private const int PreferenceJsonMaxChars = 16_000;
    
    public record TelegramLinkRequest(
        long Id,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        string? Username,
        [property: JsonPropertyName("photo_url")] string? PhotoUrl,
        [property: JsonPropertyName("auth_date")] long AuthDate,
        string Hash);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var groupBuilder = app.MapGroup("/api/auth");

        #region Register
        groupBuilder.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, IConfiguration cfg, CedarDbContext db) =>
        {
            var submitted = req.InviteCode?.Trim() ?? "";

            // Real invite codes first (IF2 step 3), config code as the fallback — deliberately
            // kept, so a database problem can't lock registration out entirely.
            var now = DateTime.UtcNow;
            var code = await db.InviteCodes.FirstOrDefaultAsync(c => c.Code.ToLower() == submitted.ToLower());
            var codeUsable = code is not null
                && InviteCodeRules.IsUsable(code.IsActive, code.ExpiresAt, code.MaxUses, code.Uses, now);

            var configInvite = cfg[Consts.General.InviteCodeCfg];
            var configMatches = !string.IsNullOrEmpty(configInvite) && submitted == configInvite;

            if (!codeUsable && !configMatches)
                return Results.BadRequest(new { error = "Invalid invite code" });

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                // Null when the config fallback was used: there is no code row to point at, and
                // inventing one would make the attribution list lie.
                InviteCodeId = codeUsable ? code!.Id : null,
            };

            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            // Counted only after the account actually exists — a failed registration shouldn't
            // burn a use off a limited code.
            if (codeUsable)
            {
                code!.Uses++;
                await db.SaveChangesAsync();
            }

            // Sign the new account in. Without this, registration succeeded on the server while
            // the client's follow-up /me returned 401 and it reported "Registration failed" — so
            // every signup looked broken while having actually worked, and on a single-use invite
            // code the retry then genuinely failed. isPersistent matches the login endpoint.
            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(new { message = "Registered" });
        });
        #endregion

        groupBuilder.MapPost("/login", async (LoginRequest req, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) 
                return Results.Unauthorized();
            
            var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);
            return result.Succeeded 
                ? Results.Ok(new { message = "Logged in" }) 
                : Results.Unauthorized();
        });

        groupBuilder.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();

        groupBuilder.MapGet("/me", async (ClaimsPrincipal user, UserManager<ApplicationUser> users) =>
        {
            var appUser = await users.GetUserAsync(user);
            return Results.Ok(new
            {
                email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity!.Name,
                createdAt = appUser?.CreatedAt,
                // Hides the /admin entry point for everyone else. Not a security boundary —
                // that lives on the server, on the /api/admin group (IF2).
                isAdmin = appUser?.IsAdmin ?? false,
                planTier = appUser is null ? null : SubscriptionPlanHelper.CheckPlanExpiration(appUser.PlanTier, appUser.PlanExpiresAt, DateTime.UtcNow).ToString(),
                planExpiresAt = appUser?.PlanExpiresAt,
                trialUsed = appUser?.TrialUsedAt is not null,
                telegramLinked = appUser?.TelegramUserId is not null,
                telegramUsername = appUser?.TelegramUsername,
                telegramLinkedAt = appUser?.TelegramLinkedAt,
                notifyOnEngagement = appUser?.NotifyOnEngagement ?? false,
                postSignature = appUser?.PostSignature,
                postSignatureUrl = appUser?.PostSignatureUrl,
                authorDisplayName = appUser?.AuthorDisplayName,
                profileUrl = appUser?.ProfileUrl,
                profileLocation = appUser?.ProfileLocation,
                headerSlot1Type = appUser?.HeaderSlot1Type?.ToString(),
                headerSlot2Type = appUser?.HeaderSlot2Type?.ToString(),
                headerSlot3Type = appUser?.HeaderSlot3Type?.ToString(),
                socialTwitterUrl = appUser?.SocialTwitterUrl,
                socialInstagramUrl = appUser?.SocialInstagramUrl,
                socialFacebookUrl = appUser?.SocialFacebookUrl,
                socialYoutubeUrl = appUser?.SocialYoutubeUrl,
                socialGithubUrl = appUser?.SocialGithubUrl,
                avatarUrl = appUser?.AvatarUrl,
                blogLinkText = appUser?.BlogLinkText,
                blogLinkTexts = LocalizedTextMap.All(appUser?.BlogLinkTextTranslationsJson),
                telegramLinkTexts = LocalizedTextMap.All(appUser?.TelegramLinkTextTranslationsJson),
                telegramLinkText = appUser?.TelegramLinkText,
                toolbarLayoutJson = appUser?.ToolbarLayoutJson,
                appearancePrefsJson = appUser?.AppearancePrefsJson,
                newDraftDefaultsJson = appUser?.NewDraftDefaultsJson,
                uiLanguage = appUser?.UiLanguage,
            });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/notifications", async (NotificationPrefsRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            user.NotifyOnEngagement = req.NotifyOnEngagement;
            await users.UpdateAsync(user);
            return Results.Ok(new { notifyOnEngagement = user.NotifyOnEngagement });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/toolbar-layout", async (ToolbarLayoutRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (req.LayoutJson is { Length: > PreferenceJsonMaxChars })
                return Results.BadRequest(new { error = "Toolbar layout is too large" });

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            user.ToolbarLayoutJson = req.LayoutJson;
            await users.UpdateAsync(user);
            return Results.Ok(new { toolbarLayoutJson = user.ToolbarLayoutJson });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/appearance", async (AppearanceRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (req.PrefsJson is { Length: > PreferenceJsonMaxChars })
                return Results.BadRequest(new { error = "Appearance preferences are too large" });

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            user.AppearancePrefsJson = req.PrefsJson;
            await users.UpdateAsync(user);
            return Results.Ok(new { appearancePrefsJson = user.AppearancePrefsJson });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/new-draft-defaults", async (NewDraftDefaultsRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (req.DefaultsJson is { Length: > PreferenceJsonMaxChars })
                return Results.BadRequest(new { error = "New-draft defaults are too large" });

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            user.NewDraftDefaultsJson = req.DefaultsJson;
            await users.UpdateAsync(user);
            return Results.Ok(new { newDraftDefaultsJson = user.NewDraftDefaultsJson });
        })
        .RequireAuthorization();

        // Interface language (B26, ADR-044). Its own endpoint rather than a field on /profile,
        // same reasoning as /appearance above. Null resets to "follow the browser".
        groupBuilder.MapPost("/ui-language", async (UiLanguageRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (req.UiLanguage is not null && !Languages.IsUiLanguage(req.UiLanguage))
                return Results.BadRequest(new { error = "Unsupported interface language" });

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            user.UiLanguage = req.UiLanguage;
            await users.UpdateAsync(user);
            return Results.Ok(new { uiLanguage = user.UiLanguage });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/signature", async (SignatureRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) 
                return Results.Unauthorized();

            var currentPlan = SubscriptionPlanHelper.CheckPlanExpiration(user.PlanTier, user.PlanExpiresAt, DateTime.UtcNow);

            if ((!string.IsNullOrWhiteSpace(req.Signature) || !string.IsNullOrWhiteSpace(req.SignatureUrl))
                && !PlanLimitations.HasCustomSignature(currentPlan))
            {
                return Results.Json(new { error = "Post signature is a Pro feature. Upgrade to use it." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            user.PostSignature = string.IsNullOrWhiteSpace(req.Signature) ? null : req.Signature.Trim();
            user.PostSignatureUrl = string.IsNullOrWhiteSpace(req.SignatureUrl) ? null : req.SignatureUrl.Trim();
            await users.UpdateAsync(user);

            return Results.Ok(new { postSignature = user.PostSignature, postSignatureUrl = user.PostSignatureUrl });
        })
        .RequireAuthorization();

        // IF1 — the file itself goes through POST /api/assets like any other image (same type
        // whitelist, same storage quota, same public /media serving); this only records which one
        // is the avatar. Null clears it back to the initial-letter placeholder.
        groupBuilder.MapPost("/avatar", async (AvatarRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var url = req.AvatarUrl?.Trim();
            // Only ever a path we serve ourselves: accepting an arbitrary URL here would let a
            // profile point the app's own chrome at someone else's server.
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("/media/", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Avatar must be an uploaded image" });

            user.AvatarUrl = string.IsNullOrEmpty(url) ? null : url;
            await users.UpdateAsync(user);
            return Results.Ok(new { avatarUrl = user.AvatarUrl });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/profile", async (ProfileRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
                return Results.Unauthorized();

            var currentPlan = SubscriptionPlanHelper.CheckPlanExpiration(user.PlanTier, user.PlanExpiresAt, DateTime.UtcNow);
            var slot3 = ParseSlotType(req.HeaderSlot3Type);

            if (slot3 is not null && PlanLimitations.MaxHeaderSlots(currentPlan) < 3)
            {
                return Results.Json(new { error = "The third header slot is a Pro feature. Upgrade to use it." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            user.AuthorDisplayName = string.IsNullOrWhiteSpace(req.AuthorDisplayName) ? null : req.AuthorDisplayName.Trim();
            user.ProfileUrl = string.IsNullOrWhiteSpace(req.ProfileUrl) ? null : req.ProfileUrl.Trim();
            user.ProfileLocation = string.IsNullOrWhiteSpace(req.ProfileLocation) ? null : req.ProfileLocation.Trim();
            user.HeaderSlot1Type = ParseSlotType(req.HeaderSlot1Type);
            user.HeaderSlot2Type = ParseSlotType(req.HeaderSlot2Type);
            user.HeaderSlot3Type = slot3;
            user.SocialTwitterUrl = string.IsNullOrWhiteSpace(req.SocialTwitterUrl) ? null : req.SocialTwitterUrl.Trim();
            user.SocialInstagramUrl = string.IsNullOrWhiteSpace(req.SocialInstagramUrl) ? null : req.SocialInstagramUrl.Trim();
            user.SocialFacebookUrl = string.IsNullOrWhiteSpace(req.SocialFacebookUrl) ? null : req.SocialFacebookUrl.Trim();
            user.SocialYoutubeUrl = string.IsNullOrWhiteSpace(req.SocialYoutubeUrl) ? null : req.SocialYoutubeUrl.Trim();
            user.SocialGithubUrl = string.IsNullOrWhiteSpace(req.SocialGithubUrl) ? null : req.SocialGithubUrl.Trim();
            // I15 — cross-link wording. Not Pro-gated: it replaces one of our strings with the
            // author's, it doesn't remove attribution the way the signature does.
            //
            // Per language: the cross-link is read by whoever is reading that language's version
            // of the post. The primary-language wording stays in its own column; the rest go into
            // a JSON map beside it (LocalizedTextMap), so no existing row had to be migrated.
            var linkLang = req.LinkTextLanguage is not null && Languages.IsTranslationLanguage(req.LinkTextLanguage)
                ? req.LinkTextLanguage
                : Languages.Primary;
            if (linkLang == Languages.Primary)
            {
                user.BlogLinkText = string.IsNullOrWhiteSpace(req.BlogLinkText) ? null : req.BlogLinkText.Trim();
                user.TelegramLinkText = string.IsNullOrWhiteSpace(req.TelegramLinkText) ? null : req.TelegramLinkText.Trim();
            }
            else
            {
                user.BlogLinkTextTranslationsJson = LocalizedTextMap.Set(user.BlogLinkTextTranslationsJson, linkLang, req.BlogLinkText);
                user.TelegramLinkTextTranslationsJson = LocalizedTextMap.Set(user.TelegramLinkTextTranslationsJson, linkLang, req.TelegramLinkText);
            }
            await users.UpdateAsync(user);

            return Results.Ok(new
            {
                authorDisplayName = user.AuthorDisplayName,
                profileUrl = user.ProfileUrl,
                profileLocation = user.ProfileLocation,
                headerSlot1Type = user.HeaderSlot1Type?.ToString(),
                headerSlot2Type = user.HeaderSlot2Type?.ToString(),
                headerSlot3Type = user.HeaderSlot3Type?.ToString(),
                socialTwitterUrl = user.SocialTwitterUrl,
                socialInstagramUrl = user.SocialInstagramUrl,
                socialFacebookUrl = user.SocialFacebookUrl,
                socialYoutubeUrl = user.SocialYoutubeUrl,
                socialGithubUrl = user.SocialGithubUrl,
                blogLinkText = user.BlogLinkText,
                telegramLinkText = user.TelegramLinkText,
                blogLinkTexts = LocalizedTextMap.All(user.BlogLinkTextTranslationsJson),
                telegramLinkTexts = LocalizedTextMap.All(user.TelegramLinkTextTranslationsJson),
            });
        })
        .RequireAuthorization();

        groupBuilder.MapGet("/telegram/config", (TelegramBotService bot) =>
        {
                return bot.IsRunning
                    ? Results.Ok(new
                    {
                        botUsername = bot.Me.Username, 
                        botId = bot.Me.Id
                    })
                    : Results.Json(new { error = "Telegram bot is not running (no token configured)" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/telegram/link", async (TelegramLinkRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db, IConfiguration cfg) =>
        {
            var botToken = cfg[Consts.Telegram.BotTokenCfg];
            if (string.IsNullOrEmpty(botToken))
                return Results.Json(new { error = "Telegram bot is not running (no token configured)" }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var data = new TelegramLoginData(req.Id, req.FirstName, req.LastName, req.Username, req.PhotoUrl, req.AuthDate, req.Hash);
            if (!TelegramLoginVerifier.Verify(data, botToken, DateTimeOffset.UtcNow))
                return Results.BadRequest(new { error = "Invalid or expired Telegram login signature" });

            var user = await users.GetUserAsync(principal);
            if (user is null) 
                return Results.Unauthorized();

            var alreadyLinkedToOther = await db.Users.AnyAsync(u => u.TelegramUserId == req.Id && u.Id != user.Id);
            if (alreadyLinkedToOther)
                return Results.Conflict(new { error = "This Telegram account is already linked to another Cedar Clerk account" });

            user.TelegramUserId = req.Id;
            user.TelegramUsername = req.Username;
            user.TelegramFirstName = req.FirstName;
            user.TelegramLinkedAt = DateTime.UtcNow;
            await users.UpdateAsync(user);

            return Results.Ok(new { telegramUsername = user.TelegramUsername });
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/telegram/unlink", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
                return Results.Unauthorized();

            user.TelegramUserId = null;
            user.TelegramUsername = null;
            user.TelegramFirstName = null;
            user.TelegramLinkedAt = null;
            await users.UpdateAsync(user);

            return Results.NoContent();
        })
        .RequireAuthorization();

        groupBuilder.MapGet("/telegram/status", (TelegramBotService bot) =>
            Results.Ok(new { reachable = bot.IsRunning, botUsername = bot.IsRunning ? bot.Me.Username : null }))
        .RequireAuthorization();
    }

    private static HeaderSlotType? ParseSlotType(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Enum.TryParse<HeaderSlotType>(value, out var t) ? t : null;
}