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
/// X (Twitter) over API v2 (T-110, ADR-093). The post is a short standalone teaser/override
/// (ADR-077) with the blog link, or the document as a thread (ADR-094); the first post carries up
/// to four of the document's pictures, uploaded through the v2 media endpoint (ADR-241).
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
    MediaPaths media,
    ILogger<XPublishTarget> logger) : IPublishTarget
{
    public const string ApiBase = "https://api.x.com";

    /// <summary>What the connect flow asks for; media.write is what lets a picture ride along.</summary>
    public const string Scopes = "tweet.read tweet.write users.read offline.access media.write";

    /// <summary>X caps a still image at 5 MB; anything bigger is recompressed the way Bluesky's are.</summary>
    private const long MaxImageBytesValue = 5L * 1024 * 1024;

    /// <summary>Refresh this long before the recorded expiry — covers clock skew and transit time.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    public string Network => PublishNetworks.X;

    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.X,
        MaxCharacters = XPostBuilder.MaxWeightedChars,
        MaxMediaItems = 4,
        MaxImageBytes = MaxImageBytesValue,
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
        SupportsThreads = true,
        ThreadPartCharacters = XPostBuilder.MaxWeightedChars,
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
        // A thread checks for every part still ahead (ADR-094) — a thread that stops halfway for
        // money is worse than one that never starts.
        var partsAhead = request.Part is { Count: > 1 } p ? p.Count - p.Index : 1;
        if (await CreditWallet.BalanceAsync(db, request.OwnerId, ct) < partsAhead * CreditPacks.XPostCost)
            return PublishOutcome.Fail(ErrorMessages.NotEnoughCredits, StatusCodes.Status402PaymentRequired);

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var blogUrl = await MicroThreadPlan.BlogUrlAsync(draft, request.Language, db, cfg, ct);

        // ADR-094 — a thread carries the document itself, recomputed from it (T-106's principle);
        // a single post carries the author's override or the teaser (ADR-077).
        string text;
        if (request.Part is { Count: > 1 } part)
        {
            var parts = MicroThreadPlan.Parts(request.CedarJson, PublishNetworks.X, blogUrl);
            if (part.Index >= parts.Count)
                return PublishOutcome.Fail(ErrorMessages.ThreadPartGone, StatusCodes.Status409Conflict);
            text = parts[part.Index];
            if (part.Index == part.Count - 1 && blogUrl is not null)
                text = $"{text}\n\n{blogUrl}";
        }
        else
        {
            text = XPostBuilder.Build(request.AuthorText, request.CedarJson, blogUrl).Text;
        }

        if (text.Trim().Length == 0)
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

            // The pictures ride on the first post only, like Bluesky's — a thread that repeats
            // its four pictures on every part is not what a thread looks like anywhere. The
            // scope hint is decided with them and carried by the later parts, so the last part
            // of a thread does not clear what the first one found out.
            List<string> mediaIds = [];
            string? warning = null;
            if (request.Part is null or { Index: 0 })
                (mediaIds, warning) = await UploadImagesAsync(http, request, ct);
            else if (request.Target.LastError == ErrorMessages.XMediaScopeMissing)
                warning = ErrorMessages.XMediaScopeMissing;

            // ADR-094 — a thread part replies to the one before it, which is what makes X render
            // a thread rather than a scatter of posts.
            var payload = new JsonObject { ["text"] = text };
            if (request.Part?.ReplyToRemoteId is { } replyTo)
                payload["reply"] = new JsonObject { ["in_reply_to_tweet_id"] = replyTo };
            if (mediaIds.Count > 0)
                payload["media"] = new JsonObject { ["media_ids"] = new JsonArray(mediaIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) };
            var response = await http.PostAsJsonAsync("/2/tweets", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("X rejected a post for draft {DraftId}: {Status} {Body}", request.DraftId, (int)response.StatusCode, body);

                // The X app's own pay-per-use balance ran dry (found live 05.08.2026: "credits
                // depleted", which the old catch-all mapped to 502 — so the queue retried a bill
                // three times and the author watched a spinner instead of the reason). Named apart
                // from every other refusal because the author's first read of "credits" is the
                // Cedar wallet, which is a different pocket entirely.
                if (body.Contains("credits depleted", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("UsageCapExceeded", StringComparison.OrdinalIgnoreCase))
                {
                    return PublishOutcome.Fail(ErrorMessages.XApiCreditsDepleted, StatusCodes.Status402PaymentRequired);
                }

                // Every 4xx except a plain rate limit is X's verdict on this request — retrying
                // resends the same refused thing (and each retry is a real, billable API call).
                var status = (int)response.StatusCode switch
                {
                    401 or 403 => StatusCodes.Status401Unauthorized,
                    429 => StatusCodes.Status429TooManyRequests,
                    >= 400 and < 500 => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status502BadGateway,
                };
                return PublishOutcome.Fail($"X rejected the post: {Describe(body)}", status);
            }

            var created = await response.Content.ReadFromJsonAsync<CreateTweetResponse>(cancellationToken: ct);
            var tweetId = created?.Data?.Id;
            if (tweetId is null)
                return PublishOutcome.Fail("X accepted the post but returned no id", StatusCodes.Status502BadGateway);

            await CreditWallet.TryChargeAsync(db, request.OwnerId, CreditPacks.XPostCost, CreditReasons.XPost, tweetId, ct);

            // The revision is the publication's baseline, recorded once — on the last part, when
            // the whole document has actually gone out (same rule as Telegram's threads).
            if (request.Part is not { Count: > 1 } || request.Part.Index == request.Part.Count - 1)
            {
                await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title,
                    request.CedarJson, PublishNetworks.X, request.Target.Id.ToString(), ct);
            }
            await db.SaveChangesAsync(ct);

            return PublishOutcome.Ok(new PublishReceipt(tweetId, $"https://x.com/{credentials.Username}/status/{tweetId}"), warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure publishing draft {DraftId} to X", request.DraftId);
            return PublishOutcome.Fail($"Publish failed: {ex.GetType().Name}: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The stored credentials, refreshed (and saved) first when they are about to expire — the
    /// same save-before-use rule the publish path follows. Null means reconnect.
    /// </summary>
    public async Task<XCredentials?> FreshCredentialsAsync(PublishTarget target, CancellationToken ct)
    {
        var credentials = ReadCredentials(target);
        if (credentials is null) return null;
        return credentials.AccessExpiresAt - ExpirySkew < DateTime.UtcNow
            ? await RefreshAsync(target, credentials, ct)
            : credentials;
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

    /// <summary>
    /// The document's first pictures as X media ids, at most <see cref="PublishCapabilities.MaxMediaItems"/>.
    /// Never fails the publish: a post without its picture is worth more than no post. A 403 means
    /// the connection predates the media scope — the warning stays on the target until a
    /// reconnect, or an upload that succeeds, clears it.
    /// </summary>
    private async Task<(List<string> Ids, string? Warning)> UploadImagesAsync(HttpClient http, PublishRequest request, CancellationToken ct)
    {
        var ids = new List<string>();
        foreach (var image in CedarImageRefs.Collect(request.CedarJson))
        {
            if (ids.Count >= Capabilities.MaxMediaItems) break;

            var fileName = CedarImageRefs.LocalFileName(image.Src);
            if (fileName is null) continue;
            var path = Path.Combine(media.Dir, fileName);
            if (!File.Exists(path)) continue;

            var bytes = await File.ReadAllBytesAsync(path, ct);
            var contentType = ContentTypeOf(fileName);
            if (bytes.LongLength > MaxImageBytesValue)
            {
                var compressed = ImageCompressor.TryCompressJpeg(bytes, MaxImageBytesValue, logger);
                if (compressed is null)
                {
                    logger.LogWarning("Skipping {File} for X: {Bytes} bytes and it would not compress under the cap", fileName, bytes.LongLength);
                    continue;
                }
                bytes = compressed;
                contentType = "image/jpeg";
            }

            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(part, "media", fileName);
            form.Add(new StringContent("tweet_image"), "media_category");
            form.Add(new StringContent(contentType), "media_type");

            var response = await http.PostAsync("/2/media/upload", form, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                logger.LogWarning("X refused a media upload for target {TargetId} (403): the connection lacks media.write", request.Target.Id);
                return (ids, ErrorMessages.XMediaScopeMissing);
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("X refused the upload of {File}: {Status} {Body}", fileName, (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                continue;
            }

            var uploaded = await response.Content.ReadFromJsonAsync<MediaUploadResponse>(cancellationToken: ct);
            if (uploaded?.Data?.Id is { } id) ids.Add(id);
        }

        return (ids, null);
    }

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

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
    private sealed record MediaUploadResponse(MediaData? Data);
    private sealed record MediaData(string? Id);
    private sealed record TweetData(string? Id, string? Text);

    public sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
