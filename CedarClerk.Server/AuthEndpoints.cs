using System.Security.Claims;
using System.Text.Json.Serialization;
using CedarClerk.Core;
using CedarClerk.Server.Bot;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class AuthEndpoints
{
    public record RegisterRequest(string Email, string Password, string InviteCode);
    public record LoginRequest(string Email, string Password);
    public record SignatureRequest(string? Signature);
    
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
        groupBuilder.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> users, IConfiguration cfg) =>
        {
            var invite = cfg[Consts.General.InviteCodeCfg];
            if (string.IsNullOrEmpty(invite) || req.InviteCode != invite)
                return Results.BadRequest(new { error = "Invalid invite code" });

            var user = new ApplicationUser
            {
                UserName = req.Email, 
                Email = req.Email
            };
            
            var result = await users.CreateAsync(user, req.Password);
            return result.Succeeded
                ? Results.Ok(new { message = "Registered" })
                : Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
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
                planTier = appUser is null ? null : SubscriptionPlanHelper.CheckPlanExpiration(appUser.PlanTier, appUser.PlanExpiresAt, DateTime.UtcNow).ToString(),
                planExpiresAt = appUser?.PlanExpiresAt,
                trialUsed = appUser?.TrialUsedAt is not null,
                telegramLinked = appUser?.TelegramUserId is not null,
                telegramUsername = appUser?.TelegramUsername,
                telegramLinkedAt = appUser?.TelegramLinkedAt,
                postSignature = appUser?.PostSignature,
            });
        })
        .RequireAuthorization();
        
        groupBuilder.MapPost("/signature", async (SignatureRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) 
                return Results.Unauthorized();

            var currentPlan = SubscriptionPlanHelper.CheckPlanExpiration(user.PlanTier, user.PlanExpiresAt, DateTime.UtcNow);
            
            if (!string.IsNullOrWhiteSpace(req.Signature) && !PlanLimitations.HasCustomSignature(currentPlan))
            {
                return Results.Json(new { error = "Post signature is a Pro feature. Upgrade to use it." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            user.PostSignature = string.IsNullOrWhiteSpace(req.Signature) ? null : req.Signature.Trim();
            await users.UpdateAsync(user);
            
            return Results.Ok(new { postSignature = user.PostSignature });
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
}