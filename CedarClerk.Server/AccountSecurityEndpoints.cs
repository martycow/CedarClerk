using System.Net.Http.Headers;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public static class AccountSecurityEndpoints
{
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record DeleteAccountRequest(string Email, string CurrentPassword);

    public static void MapAccountSecurityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").RequireAuthorization().RequireRateLimiting("password-reset");
        group.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext context,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            var result = await users.ChangePasswordAsync(user, request.CurrentPassword ?? "", request.NewPassword ?? "");
            if (!result.Succeeded) return Results.BadRequest(new { error = ErrorMessages.PasswordChangeFailed });
            await signIn.RefreshSignInAsync(user);
            return Results.NoContent();
        });
        group.MapPost("/delete-account", async (DeleteAccountRequest request, HttpContext context,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            IServiceScopeFactory scopes, MediaPaths media, TenantOwnerCache.ForHosts hosts,
            IConfiguration configuration, IHttpClientFactory clients) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(request.Email?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase) ||
                !await users.CheckPasswordAsync(user, request.CurrentPassword ?? ""))
                return Results.BadRequest(new { error = ErrorMessages.AccountVerificationFailed });
            if (!await BillingClosedAsync(user.StripeCustomerId, configuration[Consts.Stripe.SecretKeyCfg], clients.CreateClient("billing"), context.RequestAborted))
                return Results.Conflict(new { error = ErrorMessages.AccountBillingMustClose });
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
            await AccountDeletion.DeleteAsync(db, user.Id, media.Dir, hosts);
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
    }

    public static async Task<bool> BillingClosedAsync(string? customerId, string? secretKey,
        HttpClient client, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return true;
        if (string.IsNullOrWhiteSpace(secretKey)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.stripe.com/v1/subscriptions?limit=1&customer=" + Uri.EscapeDataString(customerId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.TryGetProperty("data", out var subscriptions) &&
                subscriptions.ValueKind == JsonValueKind.Array && subscriptions.GetArrayLength() == 0 &&
                json.RootElement.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.False;
        }
        catch (HttpRequestException) { return false; }
        catch (JsonException) { return false; }
    }
}
