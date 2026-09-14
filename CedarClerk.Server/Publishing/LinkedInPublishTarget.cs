using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Publishing;

/// <summary>Credentials for one LinkedIn member, as stored (encrypted) on the PublishTarget row.</summary>
public sealed record LinkedInCredentials(string PersonId, string Name, string AccessToken, DateTime AccessExpiresAt);

/// <summary>
/// LinkedIn over the versioned Posts API (T-381, ADR-299), on the member's own profile through the
/// self-serve "Share on LinkedIn" product. One post per publish: the document with its layout
/// carried into plain text (<see cref="LinkedInPostBuilder"/>), up to twenty of its pictures
/// through the Images API, and — when there are no pictures — the blog page as an article card
/// with a rendered thumbnail, since LinkedIn scrapes nothing from a URL.
///
/// Two things distinguish this target. **No refresh**: a self-serve app gets a 60-day token and
/// no refresh token, so an expired connection is a reconnect, never a silent renewal, and the
/// expiry is shown in Settings so the author is asked before a publish fails. **No scheduling**:
/// LinkedIn's API terms forbid automated posting (§3.1 item 26), so this network takes the Publish
/// button and refuses the scheduler and the evergreen slots — the queue still carries it, because
/// a queued send that runs at once is the publish button, not a schedule.
/// </summary>
public class LinkedInPublishTarget(
    CedarDbContext db,
    PublishTargetSecrets secrets,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    MediaPaths media,
    ILogger<LinkedInPublishTarget> logger) : IPublishTarget
{
    public const string ApiBase = "https://api.linkedin.com";

    /// <summary>openid+profile give the member id and name; w_member_social is the post itself.</summary>
    public const string Scopes = "openid profile w_member_social";

    /// <summary>Marketing-version header LinkedIn requires; each YYYYMM is sunset a year on.</summary>
    public const string DefaultApiVersion = "202608";

    /// <summary>LinkedIn's multi-image post takes twenty at most; a single image is its own shape.</summary>
    public const int MaxImages = 20;

    /// <summary>Nothing published for the Images API; 8 MB is LinkedIn's own composer cap.</summary>
    private const long MaxImageBytesValue = 8L * 1024 * 1024;

    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    public string Network => PublishNetworks.LinkedIn;

    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.LinkedIn,
        MaxCharacters = LinkedInPostBuilder.MaxChars,
        MaxMediaItems = MaxImages,
        MaxImageBytes = MaxImageBytesValue,
        SupportsVideo = false,
        SupportsAudio = false,
        // Bold and italic reach the feed through the Unicode block, not as marks; the matrix says "as text".
        SupportsRichText = false,
        SupportsHeadings = false,
        SupportsLists = true,
        SupportsTables = false,
        SupportsCodeBlocks = false,
        SupportsMath = false,
        SupportsLinkPreview = true,
        SupportsAltText = true,
        SupportsThreads = false,
        PostsHavePublicUrls = true,
        DerivesShortPost = true,
    };

    public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var credentials = ReadCredentials(request.Target);
        if (credentials is null || credentials.AccessExpiresAt - ExpirySkew < DateTime.UtcNow)
            return PublishOutcome.Fail(ErrorMessages.LinkedInReconnect, StatusCodes.Status401Unauthorized);

        if (request.Part is { Count: > 1 })
            return PublishOutcome.Fail("LinkedIn does not support threads — publish as one post", StatusCodes.Status422UnprocessableEntity);

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var blogUrl = await MicroThreadPlan.BlogUrlAsync(draft, request.Language, db, cfg, ct);
        var post = LinkedInPostBuilder.Build(request.AuthorText, request.CedarJson, blogUrl);
        if (post.Text.Trim().Length == 0)
            return PublishOutcome.Fail("Nothing to post — write a LinkedIn version or some body text");

        try
        {
            var http = Client(credentials.AccessToken);
            var author = $"urn:li:person:{credentials.PersonId}";

            var images = await UploadImagesAsync(http, author, request, ct);
            JsonObject? content = null;
            if (images.Count > 0)
                content = ImageContent(images);
            else if (blogUrl is not null)
                content = ArticleContent(blogUrl, request.Title, ArticleDescription(request.CedarJson),
                    await UploadThumbnailAsync(http, author, request, ct));

            var payload = BuildPayload(author, LinkedInLittleText.Escape(post.Text), content);
            var (response, body) = await CreatePostAsync(http, payload, ct);

            // The card was refused, the text was not — a 4xx means nothing was posted, so a second
            // attempt without the card is the post the author wrote, not a duplicate.
            if (!response.IsSuccessStatusCode && content?["article"] is not null
                && (int)response.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity)
            {
                logger.LogWarning("LinkedIn refused the article card for draft {DraftId} ({Status} {Body}); sending text only",
                    request.DraftId, (int)response.StatusCode, body);
                payload.Remove("content");
                (response, body) = await CreatePostAsync(http, payload, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("LinkedIn rejected a post for draft {DraftId}: {Status} {Body}", request.DraftId, (int)response.StatusCode, body);
                return (int)response.StatusCode switch
                {
                    401 or 403 => PublishOutcome.Fail(ErrorMessages.LinkedInReconnect, StatusCodes.Status401Unauthorized),
                    429 => PublishOutcome.Fail($"LinkedIn rate limit: {Describe(body)}", StatusCodes.Status429TooManyRequests),
                    >= 400 and < 500 => PublishOutcome.Fail($"LinkedIn rejected the post: {Describe(body)}"),
                    _ => PublishOutcome.Fail($"LinkedIn rejected the post: {Describe(body)}", StatusCodes.Status502BadGateway),
                };
            }

            var urn = response.Headers.TryGetValues("x-restli-id", out var ids) ? ids.FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(urn))
                return PublishOutcome.Fail("LinkedIn accepted the post but returned no id", StatusCodes.Status502BadGateway);

            await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title,
                request.CedarJson, PublishNetworks.LinkedIn, request.Target.Id.ToString(), ct);
            await db.SaveChangesAsync(ct);

            return PublishOutcome.Ok(new PublishReceipt(urn, PublicUrl(urn)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure publishing draft {DraftId} to LinkedIn", request.DraftId);
            return PublishOutcome.Fail($"Publish failed: {ex.GetType().Name}: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
    }

    public static string PublicUrl(string urn) => $"https://www.linkedin.com/feed/update/{urn}/";

    /// <summary>When the stored token stops working, for the reconnect notice in Settings.</summary>
    public DateTime? ExpiresAt(PublishTarget target) => ReadCredentials(target)?.AccessExpiresAt;

    public HttpClient Client(string accessToken)
    {
        var http = httpFactory.CreateClient();
        http.BaseAddress = new Uri(ApiBase);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.DefaultRequestHeaders.Add("Linkedin-Version", cfg[Consts.LinkedIn.ApiVersionCfg] ?? DefaultApiVersion);
        http.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
        return http;
    }

    // ── The wire shapes, pure so a test can pin them ─────────────────────────────────────────

    public static JsonObject BuildPayload(string author, string commentary, JsonObject? content)
    {
        var payload = new JsonObject
        {
            ["author"] = author,
            ["commentary"] = commentary,
            ["visibility"] = "PUBLIC",
            ["distribution"] = new JsonObject
            {
                ["feedDistribution"] = "MAIN_FEED",
                ["targetEntities"] = new JsonArray(),
                ["thirdPartyDistributionChannels"] = new JsonArray(),
            },
            ["lifecycleState"] = "PUBLISHED",
            ["isReshareDisabledByAuthor"] = false,
        };
        if (content is not null) payload["content"] = content;
        return payload;
    }

    /// <summary>One picture is `media`, two to twenty are `multiImage` — different shapes, same alt text.</summary>
    public static JsonObject ImageContent(IReadOnlyList<(string Urn, string? Alt)> images)
    {
        static JsonObject Item((string Urn, string? Alt) image)
        {
            var item = new JsonObject { ["id"] = image.Urn };
            if (image.Alt is not null) item["altText"] = image.Alt;
            return item;
        }

        return images.Count == 1
            ? new JsonObject { ["media"] = Item(images[0]) }
            : new JsonObject
            {
                ["multiImage"] = new JsonObject
                {
                    ["images"] = new JsonArray(images.Select(i => (JsonNode?)Item(i)).ToArray()),
                },
            };
    }

    public static JsonObject ArticleContent(string source, string title, string? description, string? thumbnailUrn)
    {
        var article = new JsonObject { ["source"] = source, ["title"] = Clip(title, 400) };
        if (!string.IsNullOrWhiteSpace(description)) article["description"] = Clip(description, 4086);
        if (thumbnailUrn is not null) article["thumbnail"] = thumbnailUrn;
        return new JsonObject { ["article"] = article };
    }

    /// <summary>The opening paragraphs as the card's description — a summary, not the post again.</summary>
    public static string? ArticleDescription(string cedarJson)
    {
        var paragraphs = CedarPlainText.Paragraphs(cedarJson).Where(p => p.Length > 0).ToList();
        if (paragraphs.Count == 0) return null;
        var text = paragraphs[0];
        if (paragraphs.Count > 1 && text.Length + paragraphs[1].Length < 300) text += " " + paragraphs[1];
        return Clip(text, 300);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    // ── Media ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The document's pictures as image URNs, in reading order, at most <see cref="MaxImages"/>.
    /// Never fails the publish: a post without one of its pictures is worth more than no post.
    /// </summary>
    private async Task<List<(string Urn, string? Alt)>> UploadImagesAsync(HttpClient http, string author, PublishRequest request, CancellationToken ct)
    {
        var urns = new List<(string, string?)>();
        foreach (var image in CedarImageRefs.Collect(request.CedarJson))
        {
            if (urns.Count >= MaxImages) break;
            var fileName = CedarImageRefs.LocalFileName(image.Src);
            if (fileName is null) continue;
            var path = Path.Combine(media.Dir, fileName);
            if (!File.Exists(path)) continue;

            var bytes = await File.ReadAllBytesAsync(path, ct);
            var contentType = ContentTypeOf(fileName);
            // JPG, PNG and GIF only, and under the cap: anything else is re-encoded as a JPEG.
            if (bytes.LongLength > MaxImageBytesValue || contentType == "image/webp")
            {
                var compressed = ImageCompressor.TryCompressJpeg(bytes, MaxImageBytesValue, logger);
                if (compressed is null)
                {
                    logger.LogWarning("Skipping {File} for LinkedIn: {Bytes} bytes and it would not re-encode under the cap", fileName, bytes.LongLength);
                    continue;
                }
                bytes = compressed;
                contentType = "image/jpeg";
            }

            var urn = await UploadImageAsync(http, author, bytes, contentType, ct);
            if (urn is not null) urns.Add((urn, image.Alt));
        }
        return urns;
    }

    /// <summary>The blog's own OG card, so the article preview is not a blank tile.</summary>
    private async Task<string?> UploadThumbnailAsync(HttpClient http, string author, PublishRequest request, CancellationToken ct)
    {
        try
        {
            var site = await BlogTenant.SiteForOwnerAsync(db, cfg, request.OwnerId, ct);
            var siteName = site is null ? "" : await BlogSubscriberNotifier.SiteNameAsync(db, site.Value, ct);
            var card = OgImageEndpoint.RenderCard(request.Title, siteName);
            return await UploadImageAsync(http, author, card, "image/png", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No thumbnail for the LinkedIn article card of draft {DraftId}", request.DraftId);
            return null;
        }
    }

    /// <summary>
    /// Images API: register the upload, then PUT the bytes to the URL it answers with. The post
    /// may reference the URN at once — a w_member_social token cannot even GET the image back to
    /// poll its status, and LinkedIn publishes the post when processing finishes.
    /// </summary>
    private async Task<string?> UploadImageAsync(HttpClient http, string author, byte[] bytes, string contentType, CancellationToken ct)
    {
        var init = await http.PostAsJsonAsync("/rest/images?action=initializeUpload",
            new { initializeUploadRequest = new { owner = author } }, ct);
        if (!init.IsSuccessStatusCode)
        {
            logger.LogWarning("LinkedIn refused an image upload registration: {Status} {Body}", (int)init.StatusCode, await init.Content.ReadAsStringAsync(ct));
            return null;
        }

        var value = JsonNode.Parse(await init.Content.ReadAsStringAsync(ct))?["value"];
        var uploadUrl = (string?)value?["uploadUrl"];
        var urn = (string?)value?["image"];
        if (uploadUrl is null || urn is null) return null;

        using var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(bytes) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var uploaded = await http.SendAsync(put, ct);
        if (!uploaded.IsSuccessStatusCode)
        {
            logger.LogWarning("LinkedIn refused image bytes for {Urn}: {Status}", urn, (int)uploaded.StatusCode);
            return null;
        }
        return urn;
    }

    private static async Task<(HttpResponseMessage Response, string Body)> CreatePostAsync(HttpClient http, JsonObject payload, CancellationToken ct)
    {
        var response = await http.PostAsync("/rest/posts",
            new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), ct);
        return (response, await response.Content.ReadAsStringAsync(ct));
    }

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    public LinkedInCredentials? ReadCredentials(PublishTarget target)
    {
        var plaintext = secrets.TryUnprotect(target.CredentialsProtected);
        if (plaintext is null) return null;
        try
        {
            return JsonSerializer.Deserialize<LinkedInCredentials>(plaintext);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>LinkedIn's errors are JSON; `message` (with its `code` when named) is the readable part.</summary>
    private static string Describe(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var message = (string?)node?["message"];
            var code = (string?)node?["code"];
            return message is null ? body : code is null ? message : $"{message} ({code})";
        }
        catch (JsonException)
        {
            return body;
        }
    }

    public sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);

    public sealed record UserInfo(
        [property: System.Text.Json.Serialization.JsonPropertyName("sub")] string? Sub,
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string? Name);
}
