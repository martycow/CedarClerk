using System.Net.Http.Json;
using System.Text.Json;
using CedarClerk.Core;

namespace CedarClerk.Server;

/// <summary>
/// Verifies a sign-in against another Cedar Clerk installation (ADR-108).
///
/// Configured only on the desktop shell, which points at the Pi. On the Pi itself the setting is
/// absent — an installation delegating identity to itself would be a loop.
///
/// It answers one question — "are these credentials real, and whose are they" — and nothing else.
/// The session that follows is local: this is not a proxy, and no upstream cookie is kept.
/// </summary>
public class UpstreamAuth(IHttpClientFactory http, IConfiguration config, ILogger<UpstreamAuth> logger)
{
    public enum Outcome
    {
        Ok,
        /// <summary>The server answered, and said no.</summary>
        BadCredentials,
        /// <summary>The server did not answer. **Not** the same thing, and never shown as if it were.</summary>
        Unreachable,
    }

    /// <summary>Whoever the upstream says this is. <see cref="RemoteId"/> is null on an older upstream.</summary>
    public record Identity(string? RemoteId, string Email);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public string? BaseUrl => config[Consts.General.UpstreamAuthCfg]?.TrimEnd('/');

    /// <summary>
    /// Whitespace and empty both read as "not configured" — a cleared environment variable is an
    /// ordinary thing, and it must not turn into a base URL of "".
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>The bare host, for telling the author where they are signing in.</summary>
    public string? DisplayHost => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : BaseUrl;

    public async Task<(Outcome Result, Identity? Identity)> VerifyAsync(string email, string password, CancellationToken ct = default)
    {
        var baseUrl = BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) return (Outcome.Unreachable, null);

        var client = http.CreateClient();
        client.Timeout = Timeout;

        HttpResponseMessage login;
        try
        {
            login = await client.PostAsJsonAsync($"{baseUrl}/api/auth/login", new { email, password }, ct);
        }
        catch (Exception e)
        {
            // No network, DNS gone, TLS refused, the Pi is off. All of it is "cannot ask right now",
            // which is a different answer to the author than "wrong password".
            logger.LogWarning(e, "Upstream auth at {BaseUrl} is unreachable", baseUrl);
            return (Outcome.Unreachable, null);
        }

        if (!login.IsSuccessStatusCode)
            return (login.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? Outcome.BadCredentials
                // A 500 upstream is not the author's password being wrong.
                : Outcome.Unreachable, null);

        // The auth cookie the upstream just issued, replayed once to ask who this is. It is used
        // for this one call and never stored: the local session is what the author ends up with.
        var cookies = login.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(v => v.Split(';')[0]))
            : null;
        if (cookies is null)
        {
            logger.LogWarning("Upstream accepted the login but issued no cookie");
            return (Outcome.Unreachable, null);
        }

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/auth/me");
        meRequest.Headers.Add("Cookie", cookies);

        try
        {
            using var me = await client.SendAsync(meRequest, ct);
            if (!me.IsSuccessStatusCode) return (Outcome.Unreachable, null);

            using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            // `id` arrived with ADR-108; an upstream built before it simply has no such property,
            // and the email carries the identity until that installation is updated.
            var remoteId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
            var remoteEmail = root.TryGetProperty("email", out var mail) ? mail.GetString() : null;

            return string.IsNullOrWhiteSpace(remoteEmail)
                ? (Outcome.Unreachable, null)
                : (Outcome.Ok, new Identity(remoteId, remoteEmail));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Upstream accepted the login but /me failed");
            return (Outcome.Unreachable, null);
        }
    }
}
