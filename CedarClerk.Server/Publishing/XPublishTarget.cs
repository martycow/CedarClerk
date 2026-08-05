using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Publishing;

/// <summary>Credentials for one X account, as stored (encrypted) on the PublishTarget row.</summary>
public sealed record XCredentials(string UserId, string Username, string AccessToken, string RefreshToken, DateTime AccessExpiresAt);

/// <summary>
/// X (Twitter) over API v2 (T-110, ADR-093). Text plus the blog link in v1 — no media, no threads;
/// the post is a short standalone teaser/override (ADR-077) and the link carries the reader to the
/// full document.
///
/// Two things distinguish this target from Bluesky's. **Token rotation**: X invalidates the old
/// refresh token the moment a new one is issued, so the new pair is saved to the database BEFORE
/// the access token is first used — a save that fails aborts the publish, because publishing with
/// an unsaved rotated token is how an account gets locked out (ADR-079's lesson, applied to the
/// network that offers no session-per-publish alternative). **The credit charge** (ADR-092): the
/// balance is checked before the send and one credit is taken after success, anchored to the
/// created post's id, so a queue retry that creates a second real post pays for it and one that
/// never posted pays nothing.
/// </summary>
public class XPublishTarget(
    CedarDbContext db,
    PublishTargetSecrets secrets,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    ILogger<XPublishTarget> logger) : IPublishTarget
{
    public const string ApiBase = "https://api.x.com";

    /// <summary>Refresh this long before the recorded expiry — covers clock skew and transit time.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    public string Network => PublishNetworks.X;

    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.X,
        MaxCharacters = XPostBuilder.MaxWeightedChars,
        MaxMediaItems = 0,
        SupportsVideo = false,
        SupportsAudio = false,
        SupportsRichText = false,
        SupportsHeadings = false,
        SupportsLists = false,
        SupportsTables = false,
        SupportsCodeBlocks = false,
        SupportsMath = false,
        SupportsLinkPreview = true,
        SupportsAltText = false,
        SupportsThreads = false,
        PostsHavePublicUrls = true,
        DerivesShortPost = true,
    };

    public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var credentials = ReadCredentials(request.Target);
        if (credentials is null)
            return PublishOutcome.Fail(ErrorMessages.XReconnect, StatusCodes.Status401Unauthorized);

        // The refusal happens before anything reaches the network: a post X accepted is a post
        // Marty already paid for, so "not enough credits" must never be discovered afterwards.
        if (await CreditWallet.BalanceAsync(db, request.OwnerId, ct) < CreditPacks.XPostCost)
            return PublishOutcome.Fail(ErrorMessages.NotEnoughCredits, StatusCodes.Status402PaymentRequired);

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var blogUrl = draft.IsBlogPublished && draft.BlogSlug is not null
            ? $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}"
              + (request.Language == draft.PrimaryLanguage ? "" : $"?lang={request.Language}")
            : null;

        var post = XPostBuilder.Build(request.AuthorText, request.CedarJson, blogUrl);
        if (post.Text.Trim().Length == 0)
            return PublishOutcome.Fail("Nothing to post — write an X version or some body text");

        try
        {
            if (credentials.AccessExpiresAt - ExpirySkew < DateTime.UtcNow)
            {
                credentials = await RefreshAsync(request.Target, credentials, ct);
                if (credentials is null)
                    return PublishOutcome.Fail(ErrorMessages.XReconnect, StatusCodes.Status401Unauthorized);
            }

            var http = httpFactory.CreateClient();
            http.BaseAddress = new Uri(ApiBase);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

            var response = await http.PostAsJsonAsync("/2/tweets", new { text = post.Text }, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("X rejected a post for draft {DraftId}: {Status} {Body}", request.DraftId, (int)response.StatusCode, body);
                var status = (int)response.StatusCode switch
                {
                    401 or 403 => StatusCodes.Status401Unauthorized,
                    429 => StatusCodes.Status429TooManyRequests,
                    _ => StatusCodes.Status502BadGateway,
                };
                return PublishOutcome.Fail($"X rejected the post: {Describe(body)}", status);
            }

            var created = await response.Content.ReadFromJsonAsync<CreateTweetResponse>(cancellationToken: ct);
            var tweetId = created?.Data?.Id;
            if (tweetId is null)
                return PublishOutcome.Fail("X accepted the post but returned no id", StatusCodes.Status502BadGateway);

            await CreditWallet.TryChargeAsync(db, request.OwnerId, CreditPacks.XPostCost, CreditReasons.XPost, tweetId, ct);

            await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title,
                request.CedarJson, PublishNetworks.X, request.Target.Id.ToString(), ct);
            await db.SaveChangesAsync(ct);

            return PublishOutcome.Ok(new PublishReceipt(tweetId, $"https://x.com/{credentials.Username}/status/{tweetId}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure publishing draft {DraftId} to X", request.DraftId);
            return PublishOutcome.Fail($"Publish failed: {ex.GetType().Name}: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Exchanges the refresh token for a new pair and **persists it before returning** — the old
    /// refresh token is dead the moment X answers, so an unsaved new one locks the account out.
    /// Null means the token was refused and the author has to reconnect.
    /// </summary>
    private async Task<XCredentials?> RefreshAsync(PublishTarget target, XCredentials credentials, CancellationToken ct)
    {
        var clientId = cfg[Consts.X.ClientIdCfg];
        var clientSecret = cfg[Consts.X.ClientSecretCfg];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return null;

        var http = httpFactory.CreateClient();
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/2/oauth2/token");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = credentials.RefreshToken,
        });

        var response = await http.SendAsync(tokenRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("X refused a token refresh for target {TargetId}: {Status}", target.Id, (int)response.StatusCode);
            return null;
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        if (tokens?.AccessToken is null || tokens.RefreshToken is null)
            return null;

        var refreshed = credentials with
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            AccessExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn),
        };

        // The save-before-use rule (ADR-093). A failed save throws out of PublishAsync — the
        // publish fails loudly rather than continuing on a token the database does not hold.
        target.CredentialsProtected = secrets.Protect(JsonSerializer.Serialize(refreshed));
        await db.SaveChangesAsync(ct);
        return refreshed;
    }

    public XCredentials? ReadCredentials(PublishTarget target)
    {
        var plaintext = secrets.TryUnprotect(target.CredentialsProtected);
        if (plaintext is null) return null;
        try
        {
            return JsonSerializer.Deserialize<XCredentials>(plaintext);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>X's errors are JSON; `detail` (or the first error's message) is the readable part.</summary>
    private static string Describe(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return (string?)node?["detail"]
                ?? (string?)node?["errors"]?[0]?["message"]
                ?? (string?)node?["title"]
                ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private sealed record CreateTweetResponse(TweetData? Data);
    private sealed record TweetData(string? Id, string? Text);

    public sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
