using System.Text;
using System.Threading.RateLimiting;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace CedarClerk.Server;

public static class PasswordRecoveryEndpoints
{
    public record ForgotRequest(string? Email);
    public record ResetRequest(string? UserId, string? Token, string? Password);

    public static void AddPasswordRecovery(this IServiceCollection services)
    {
        services.AddTransient<PasswordRecoveryTokenProvider>();
        services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.PasswordResetTokenProvider = PasswordRecoveryTokenProvider.ProviderName;
            options.Tokens.ProviderMap[PasswordRecoveryTokenProvider.ProviderName] = new(typeof(PasswordRecoveryTokenProvider));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("password-request", limiter =>
            {
                limiter.PermitLimit = 20;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("password-reset", limiter =>
            {
                limiter.PermitLimit = 60;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
        services.AddSingleton(new RecoveryMailLimit());
    }

    public static void MapPasswordRecoveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/forgot-password", ForgotAsync).RequireRateLimiting("password-request").AllowAnonymous();

        group.MapPost("/reset-password", async (ResetRequest request, UserManager<ApplicationUser> users) =>
        {
            var result = await ResetAsync(request, users);
            return result.Succeeded ? Results.Ok() : Results.BadRequest(new { error = ErrorMessages.PasswordResetInvalid });
        }).RequireRateLimiting("password-reset").AllowAnonymous();
    }

    public static async Task<IResult> ForgotAsync(ForgotRequest request,
        UserManager<ApplicationUser> users, ResendEmailProvider email, IConfiguration config,
        RecoveryMailLimit limit)
    {
        if (!email.IsConfigured)
            return Results.Json(new { error = ErrorMessages.PasswordRecoveryUnavailable }, statusCode: 503);
        using var lease = limit.Limiter.AttemptAcquire();
        if (!lease.IsAcquired) return Results.StatusCode(429);

        if (request.Email is { Length: > 0 and <= 254 })
        {
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is { EmailConfirmed: true } && await users.HasPasswordAsync(user))
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var host = (config[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost).TrimEnd('/');
                var link = $"{host}/reset-password#userId={Uri.EscapeDataString(user.Id)}&token={encoded}";
                await email.SendAsync(user.Email!, EmailTexts.PasswordResetSubject, EmailTexts.PasswordResetBody(link));
            }
        }
        // Neither account eligibility nor the mail provider's result may disclose an address.
        return Results.Ok();
    }

    public static async Task<IdentityResult> ResetAsync(ResetRequest request, UserManager<ApplicationUser> users)
    {
        var invalid = IdentityResult.Failed(new IdentityError { Code = "InvalidToken" });
        if (request.UserId is not { Length: > 0 and <= 450 }
            || request.Token is not { Length: > 0 and <= 4096 }
            || request.Password is not { Length: >= 8 and <= 128 }) return invalid;
        var user = await users.FindByIdAsync(request.UserId);
        if (user is not { EmailConfirmed: true } || !await users.HasPasswordAsync(user)) return invalid;
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
        catch (FormatException) { return invalid; }
        return await users.ResetPasswordAsync(user, token, request.Password);
    }
}

public sealed class RecoveryMailLimit : IDisposable
{
    public FixedWindowRateLimiter Limiter { get; } = new(new()
    {
        PermitLimit = 50, Window = TimeSpan.FromHours(1), QueueLimit = 0,
    });
    public void Dispose() => Limiter.Dispose();
}
