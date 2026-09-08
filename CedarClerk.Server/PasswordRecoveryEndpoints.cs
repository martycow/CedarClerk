using System.Text;
using CedarClerk.Core;
using CedarClerk.Server.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace CedarClerk.Server;

public static class PasswordRecoveryEndpoints
{
    public record Request(string? Email);
    public record ResetRequest(string? UserId, string? Token, string? Password);

    public static void MapPasswordRecoveryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/forgot-password", RequestAsync)
            .AllowAnonymous().RequireRateLimiting("password-request");
        app.MapPost("/api/auth/reset-password", ResetAsync)
            .AllowAnonymous().RequireRateLimiting("password-reset");
    }

    public static async Task<IResult> RequestAsync(Request request, UserManager<ApplicationUser> users,
        ResendEmailProvider email, IConfiguration configuration)
    {
        if (!email.IsConfigured) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254)
            return Results.Accepted();

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed || !await users.HasPasswordAsync(user))
            return Results.Accepted();

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var mainHost = configuration[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
        var link = $"{mainHost.TrimEnd('/')}/reset-password#userId={Uri.EscapeDataString(user.Id)}&token={encoded}";
        // Always return the same response for an eligible address even if its transport fails.
        await email.SendAsync(user.Email!, CedarClerk.Localization.EmailTexts.ResetPasswordSubject,
            CedarClerk.Localization.EmailTexts.ResetPasswordBody(System.Net.WebUtility.HtmlEncode(link)));
        return Results.Accepted();
    }

    public static async Task<IResult> ResetAsync(ResetRequest request, UserManager<ApplicationUser> users)
    {
        if (string.IsNullOrEmpty(request.UserId) || request.UserId.Length > 128
            || string.IsNullOrEmpty(request.Token) || request.Token.Length > 4096
            || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
            return Results.BadRequest();

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
        catch (FormatException) { return Results.BadRequest(); }

        var user = await users.FindByIdAsync(request.UserId);
        if (user is null || !user.EmailConfirmed || !await users.HasPasswordAsync(user))
            return Results.BadRequest();

        var result = await users.ResetPasswordAsync(user, token, request.Password);
        return result.Succeeded ? Results.Ok() : Results.BadRequest();
    }
}
