using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public static class DiscordAuthentication
{
    public const string Scheme = "Discord";
    public const string ClientIdKey = "Cedar:Auth:Discord:ClientId";
    public const string ClientSecretKey = "Cedar:Auth:Discord:ClientSecret";

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[ClientIdKey]) &&
        !string.IsNullOrWhiteSpace(configuration[ClientSecretKey]);

    public static void AddDiscord(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        if (!IsConfigured(configuration)) return;
        builder.AddOAuth(Scheme, options =>
        {
            options.ClientId = configuration[ClientIdKey]!;
            options.ClientSecret = configuration[ClientSecretKey]!;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.CallbackPath = "/signin-discord";
            options.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
            options.TokenEndpoint = "https://discord.com/api/oauth2/token";
            options.UserInformationEndpoint = "https://discord.com/api/users/@me";
            options.Scope.Add("identify");
            options.Scope.Add("email");
            options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
            options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
            options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.Events.OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();
                using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                if (!HasVerifiedEmail(user.RootElement))
                    throw new AuthenticationFailureException("Discord did not supply a verified email.");
                context.RunClaimActions(user.RootElement);
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/login?external=failed");
                return Task.CompletedTask;
            };
        });
    }

    public static bool HasVerifiedEmail(JsonElement user) =>
        user.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True &&
        user.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(email.GetString());
}
