using System.Security.Claims;
using System.Text.Json.Serialization;
using CedarClerk.Core;
using CedarClerk.Localization;
using System.Text;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Email;
using Microsoft.AspNetCore.WebUtilities;
using CedarClerk.Server.Translation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class AuthEndpoints
{
    public record RegisterRequest(string Email, string Password, string InviteCode);
    public record LoginRequest(string Email, string Password);
    public record SignatureRequest(string? Signature, string? SignatureUrl = null,
        // FI5 — the same signature in the other content languages, keyed by language code. Same
        // whole-map-per-request shape as ProfileRequest's link-text dictionaries below.
        Dictionary<string, string>? SignatureTexts = null);
    public record ProfileRequest(
        string? AuthorDisplayName, string? ProfileUrl, string? ProfileLocation,
        string? HeaderSlot1Type, string? HeaderSlot2Type, string? HeaderSlot3Type,
        string? SocialTwitterUrl = null, string? SocialInstagramUrl = null, string? SocialFacebookUrl = null,
        string? SocialYoutubeUrl = null, string? SocialGithubUrl = null,
        string? BlogLinkText = null, string? TelegramLinkText = null,
        // The same two labels in the other content languages, keyed by language code. Sent whole
        // rather than one language per request: the page has a single Save, and a request per
        // language would mean a partial save the moment one of them failed.
        Dictionary<string, string>? BlogLinkTexts = null,
        Dictionary<string, string>? TelegramLinkTexts = null);
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
            SignInManager<ApplicationUser> signIn, IConfiguration cfg, CedarDbContext db,
            ResendEmailProvider email, ILogger<Program> logger) =>
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
                return Results.BadRequest(new { error = ErrorMessages.InvalidInviteCode });

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

            // T-002 — the confirmation mail is sent, and the account works either way. Blocking an
            // unconfirmed account would lock out every account that predates this feature, and the
            // gate that actually matters (public registration, T-052) is not open yet. What this
            // buys today is a real address on file and a visible reminder until it is confirmed.
            await SendConfirmationEmailAsync(user, users, email, cfg, logger);

            return Results.Ok(new { message = "Registered" });
        });

        // Opened from the mail. Redirects rather than answering JSON: this URL is clicked in a mail
        // client, so what has to come back is a page, not a payload.
        groupBuilder.MapGet("/confirm-email", async (string userId, string token,
            UserManager<ApplicationUser> users, IConfiguration cfg) =>
        {
            var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
            var user = await users.FindByIdAsync(userId);
            if (user is null) return Results.Redirect($"{mainHost}/settings?confirmed=unknown");

            // The token arrives through a URL, so it was Base64Url-encoded on the way out.
            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch (FormatException)
            {
                return Results.Redirect($"{mainHost}/settings?confirmed=invalid");
            }

            var result = await users.ConfirmEmailAsync(user, decoded);
            return Results.Redirect($"{mainHost}/settings?confirmed={(result.Succeeded ? "yes" : "invalid")}");
        }).AllowAnonymous();

        // Re-sends it. Deliberately answers the same way whether or not the address needed one:
        // this endpoint is reachable by anyone signed in, and "that address is already confirmed"
        // is not information worth handing out per request.
        groupBuilder.MapPost("/resend-confirmation", async (ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, IConfiguration cfg, ResendEmailProvider email, ILogger<Program> logger) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (!user.EmailConfirmed && user.Email is not null)
                await SendConfirmationEmailAsync(user, users, email, cfg, logger);
            return Results.Ok(new { sent = true });
        }).RequireAuthorization();
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

        groupBuilder.MapGet("/me", async (ClaimsPrincipal user, UserManager<ApplicationUser> users, IConfiguration config) =>
        {
            var appUser = await users.GetUserAsync(user);
            return Results.Ok(new
            {
                // Which optional modules this installation runs (ADR-101). Not a security boundary —
                // the endpoints themselves are simply not mapped when the flag is off; this is what
                // lets the client hide the menu entries instead of linking to a 404.
                modules = new { indieDev = Modules.IndieDev.ProjectEndpoints.IsEnabled(config) },
                email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity!.Name,
                createdAt = appUser?.CreatedAt,
                // T-002 — drives the reminder in Settings. Not a gate: an unconfirmed account
                // works exactly like a confirmed one today.
                emailConfirmed = appUser?.EmailConfirmed ?? false,
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
                postSignatureTexts = LocalizedTextMap.All(appUser?.PostSignatureTranslationsJson),
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
                return Results.BadRequest(new { error = ErrorMessages.ToolbarLayoutTooLarge });

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
                return Results.BadRequest(new { error = ErrorMessages.AppearancePrefsTooLarge });

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
                return Results.BadRequest(new { error = ErrorMessages.NewDraftDefaultsTooLarge });

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
                return Results.BadRequest(new { error = ErrorMessages.UnsupportedUiLanguage });

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

            var hasAnyTranslatedText = req.SignatureTexts?.Values.Any(v => !string.IsNullOrWhiteSpace(v)) ?? false;
            if ((!string.IsNullOrWhiteSpace(req.Signature) || !string.IsNullOrWhiteSpace(req.SignatureUrl) || hasAnyTranslatedText)
                && !PlanLimitations.HasCustomSignature(currentPlan))
            {
                return Results.Json(new { error = ErrorMessages.SignatureIsPro },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            user.PostSignature = string.IsNullOrWhiteSpace(req.Signature) ? null : req.Signature.Trim();
            user.PostSignatureUrl = string.IsNullOrWhiteSpace(req.SignatureUrl) ? null : req.SignatureUrl.Trim();
            if (req.SignatureTexts is not null)
                user.PostSignatureTranslationsJson = BuildLinkTextMap(req.SignatureTexts);
            await users.UpdateAsync(user);

            return Results.Ok(new {
                postSignature = user.PostSignature, postSignatureUrl = user.PostSignatureUrl,
                postSignatureTexts = LocalizedTextMap.All(user.PostSignatureTranslationsJson),
            });
        })
        .RequireAuthorization();

        // Auto-translate the profile's own texts (Marty, 01.08.2026). The signature and the two
        // cross-link lines are per-language (FI5/I15) and were the only per-language texts in the
        // product an author still had to write by hand in every language — the post body, its
        // title and the glossary all had auto-translate already.
        //
        // Deliberately the same shape as the glossary's translate-all (ADR-061): the narrow
        // ITextsTranslationProvider capability, the plan gate before the provider call, one AI
        // call charged for the whole batch, and whatever the provider returns empty is left alone
        // rather than overwritten with a blank.
        groupBuilder.MapPost("/profile/translate-texts", async (
            TranslateProfileTextsRequest req,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            CedarDbContext db,
            IConfiguration cfg,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var source = Languages.IsContentLanguage(req.SourceLanguage) ? req.SourceLanguage : Languages.Russian;
            var targets = (req.TargetLanguages ?? [])
                .Where(l => Languages.IsContentLanguage(l) && l != source)
                .Distinct()
                .ToList();
            if (targets.Count == 0)
                return Results.BadRequest(new { error = ErrorMessages.PickALanguage });

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, user.Id);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = ErrorMessages.AutoTranslateProPlus }, statusCode: StatusCodes.Status403Forbidden);

            // The three texts in a fixed order, so the provider's flat result maps back by index.
            // A blank source stays blank in every language: there is nothing to translate, and
            // inventing a signature for someone who has none would be worse than leaving it empty.
            var slots = new (string Key, string? Text)[]
            {
                ("signature", LocalizedTextMap.Pick(user.PostSignature, user.PostSignatureTranslationsJson, source)),
                ("blogLink", LocalizedTextMap.Pick(user.BlogLinkText, user.BlogLinkTextTranslationsJson, source)),
                ("telegramLink", LocalizedTextMap.Pick(user.TelegramLinkText, user.TelegramLinkTextTranslationsJson, source)),
            };
            var filled = slots.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();
            if (filled.Count == 0)
                return Results.BadRequest(new { error = ErrorMessages.NothingToTranslate });

            ITranslationProvider? provider;
            try
            {
                provider = TranslationProviderFactory.Create(cfg, httpFactory);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is not ITextsTranslationProvider textsProvider)
                return Results.Json(new { error = ErrorMessages.AutoTranslateNoProvider }, statusCode: StatusCodes.Status501NotImplemented);

            var unsupported = targets.Where(l => !provider.SupportsTargetLanguage(l)).ToList();
            if (unsupported.Count > 0)
                return Results.Json(new { error = ErrorMessages.LanguageNotSupportedByProvider(unsupported[0], provider.Name) },
                    statusCode: StatusCodes.Status501NotImplemented);

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, user.Id))
                return Results.Json(new { error = ErrorMessages.AiDailyLimitReached(PlanLimitations.AiDailyLimit) }, statusCode: StatusCodes.Status429TooManyRequests);

            foreach (var target in targets)
            {
                IReadOnlyList<string> translated;
                try
                {
                    translated = await textsProvider.TranslateTextsAsync(filled.Select(f => f.Text!).ToList(), target, ct);
                }
                catch (TranslationException ex)
                {
                    return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
                }

                for (var i = 0; i < filled.Count && i < translated.Count; i++)
                {
                    var value = translated[i].Trim();
                    if (value.Length == 0) continue;
                    switch (filled[i].Key)
                    {
                        case "signature":
                            user.PostSignatureTranslationsJson = LocalizedTextMap.Set(user.PostSignatureTranslationsJson, target, value);
                            break;
                        case "blogLink":
                            user.BlogLinkTextTranslationsJson = LocalizedTextMap.Set(user.BlogLinkTextTranslationsJson, target, value);
                            break;
                        case "telegramLink":
                            user.TelegramLinkTextTranslationsJson = LocalizedTextMap.Set(user.TelegramLinkTextTranslationsJson, target, value);
                            break;
                    }
                }
            }

            await users.UpdateAsync(user);

            return Results.Ok(new
            {
                postSignatureTexts = LocalizedTextMap.All(user.PostSignatureTranslationsJson),
                blogLinkTexts = LocalizedTextMap.All(user.BlogLinkTextTranslationsJson),
                telegramLinkTexts = LocalizedTextMap.All(user.TelegramLinkTextTranslationsJson),
            });
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
                return Results.BadRequest(new { error = ErrorMessages.AvatarMustBeUploaded });

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

            // The third slot is a Pro feature, but the gate only applies to *setting* it. It used
            // to reject any request that carried a third slot at all, which meant an account whose
            // Pro period had lapsed with a third slot already stored could never save its profile
            // again — every save failed on a field the user was not even editing, and the error
            // said "header slots" no matter what they had actually changed.
            //
            // Keeping the stored value is not a loophole: PlanLimitations decides what actually
            // renders, so a lapsed account still doesn't get three slots on its blog.
            var slot3Unchanged = slot3 == user.HeaderSlot3Type;
            if (slot3 is not null && !slot3Unchanged && PlanLimitations.MaxHeaderSlots(currentPlan) < 3)
            {
                return Results.Json(new { error = ErrorMessages.ThirdSlotIsPro },
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
            user.BlogLinkText = string.IsNullOrWhiteSpace(req.BlogLinkText) ? null : req.BlogLinkText.Trim();
            user.TelegramLinkText = string.IsNullOrWhiteSpace(req.TelegramLinkText) ? null : req.TelegramLinkText.Trim();
            if (req.BlogLinkTexts is not null)
                user.BlogLinkTextTranslationsJson = BuildLinkTextMap(req.BlogLinkTexts);
            if (req.TelegramLinkTexts is not null)
                user.TelegramLinkTextTranslationsJson = BuildLinkTextMap(req.TelegramLinkTexts);
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
                    : Results.Json(new { error = ErrorMessages.BotNotRunningNoToken },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .RequireAuthorization();

        groupBuilder.MapPost("/telegram/link", async (TelegramLinkRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db, IConfiguration cfg) =>
        {
            var botToken = cfg[Consts.Telegram.BotTokenCfg];
            if (string.IsNullOrEmpty(botToken))
                return Results.Json(new { error = ErrorMessages.BotNotRunningNoToken }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var data = new TelegramLoginData(req.Id, req.FirstName, req.LastName, req.Username, req.PhotoUrl, req.AuthDate, req.Hash);
            if (!TelegramLoginVerifier.Verify(data, botToken, DateTimeOffset.UtcNow))
                return Results.BadRequest(new { error = ErrorMessages.InvalidTelegramSignature });

            var user = await users.GetUserAsync(principal);
            if (user is null) 
                return Results.Unauthorized();

            var alreadyLinkedToOther = await db.Users.AnyAsync(u => u.TelegramUserId == req.Id && u.Id != user.Id);
            if (alreadyLinkedToOther)
                return Results.Conflict(new { error = ErrorMessages.TelegramAlreadyLinked });

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

    // Unknown codes and blanks are dropped rather than rejected: the map is a set of optional
    // labels, and refusing the whole profile save over one stray key would be out of proportion.
    public record TranslateProfileTextsRequest(string SourceLanguage, List<string>? TargetLanguages);

    /// <summary>
    /// Sends the confirmation mail, and never lets a mail failure break the flow it is part of:
    /// registration has already succeeded by this point, and a Resend outage must not undo it.
    /// The link carries a Base64Url-encoded token because Identity's own tokens contain characters
    /// a query string mangles.
    /// </summary>
    private static async Task SendConfirmationEmailAsync(
        ApplicationUser user, UserManager<ApplicationUser> users, ResendEmailProvider email,
        IConfiguration cfg, ILogger logger)
    {
        if (!email.IsConfigured || string.IsNullOrWhiteSpace(user.Email)) return;

        try
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
            var link = $"{mainHost}/api/auth/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={encoded}";

            await email.SendAsync(user.Email, EmailTexts.ConfirmSubject, EmailTexts.ConfirmBody(link));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send the confirmation email to {Email}", user.Email);
        }
    }

    private static string? BuildLinkTextMap(Dictionary<string, string> texts)
    {
        string? json = null;
        foreach (var (lang, text) in texts)
        {
            if (!Languages.IsContentLanguage(lang)) continue;
            json = LocalizedTextMap.Set(json, lang, text);
        }
        return json;
    }

    private static HeaderSlotType? ParseSlotType(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Enum.TryParse<HeaderSlotType>(value, out var t) ? t : null;
}