using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Publishing;

/// <summary>Credentials for one Bluesky account, as stored (encrypted) on the PublishTarget row.</summary>
public sealed record BlueskyCredentials(string Handle, string AppPassword, string Service);

/// <summary>
/// Bluesky over the AT Protocol (T-089, ADR-077/078). The first network with real per-tenant
/// credentials, and the first one whose post is a short standalone text rather than the document.
///
/// **A session per publish, from the app password**, rather than storing and refreshing a JWT:
/// access tokens live minutes, refresh tokens rotate on every use, and a rotated token that fails
/// to save leaves the account locked out. An app password is revocable from Bluesky's own settings,
/// which is the property that makes this safe to hold; a stale refresh token is not.
/// </summary>
public class BlueskyPublishTarget(
    CedarDbContext db,
    PublishTargetSecrets secrets,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    ILogger<BlueskyPublishTarget> logger) : IPublishTarget
{
    public const string DefaultService = "https://bsky.social";

    public string Network => PublishNetworks.Bluesky;

    // Published limits, not guesses: 300 graphemes of text, four images per post, and a 1MB cap on
    // an uploaded blob. No video, no audio, no formatting — a post is plain text plus facets.
    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.Bluesky,
        MaxCharacters = BlueskyPostBuilder.MaxGraphemes,
        MaxMediaItems = 4,
        MaxImageBytes = 1_000_000,
        SupportsVideo = false,
        SupportsAudio = false,
        SupportsRichText = false,
        SupportsHeadings = false,
        SupportsLists = false,
        SupportsTables = false,
        SupportsCodeBlocks = false,
        SupportsMath = false,
        SupportsLinkPreview = true,
        SupportsAltText = true,
        SupportsThreads = true,
        PostsHavePublicUrls = true,
        // ADR-093 — the post is BlueskyPostBuilder's teaser/override, never the document, so
        // document overflow informs rather than blocks. Without this every real document 422'd.
        DerivesShortPost = true,
    };

    public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var credentials = ReadCredentials(request.Target);
        if (credentials is null)
            return PublishOutcome.Fail(ErrorMessages.BlueskyReconnect, StatusCodes.Status401Unauthorized);

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var blogUrl = draft.IsBlogPublished && draft.BlogSlug is not null
            ? $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}/{draft.BlogSlug}"
              + (request.Language == draft.PrimaryLanguage ? "" : $"?lang={request.Language}")
            : null;

        var post = BlueskyPostBuilder.Build(request.AuthorText, request.CedarJson, blogUrl);
        if (post.Text.Trim().Length == 0)
            return PublishOutcome.Fail("Nothing to post — write a Bluesky version or some body text");

        var http = httpFactory.CreateClient();
        http.BaseAddress = new Uri(credentials.Service);

        try
        {
            var session = await CreateSessionAsync(http, credentials, ct);
            if (session is null)
                return PublishOutcome.Fail(ErrorMessages.BlueskyReconnect, StatusCodes.Status401Unauthorized);

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessJwt);

            var record = new JsonObject
            {
                ["$type"] = "app.bsky.feed.post",
                ["text"] = post.Text,
                // The protocol wants an ISO-8601 instant; the server's own clock is the only one
                // available and Bluesky does not require it to agree with theirs.
                ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            };

            if (post.Facets.Count > 0)
            {
                var facets = new JsonArray();
                foreach (var facet in post.Facets)
                {
                    facets.Add(new JsonObject
                    {
                        ["index"] = new JsonObject { ["byteStart"] = facet.ByteStart, ["byteEnd"] = facet.ByteEnd },
                        ["features"] = new JsonArray(new JsonObject
                        {
                            ["$type"] = "app.bsky.richtext.facet#link",
                            ["uri"] = facet.Uri,
                        }),
                    });
                }
                record["facets"] = facets;
            }

            var response = await http.PostAsJsonAsync("/xrpc/com.atproto.repo.createRecord", new
            {
                repo = session.Did,
                collection = "app.bsky.feed.post",
                record,
            }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Bluesky rejected a post for draft {DraftId}: {Status} {Body}", request.DraftId, (int)response.StatusCode, body);
                return PublishOutcome.Fail($"Bluesky rejected the post: {Describe(body)}", StatusCodes.Status502BadGateway);
            }

            var created = await response.Content.ReadFromJsonAsync<CreateRecordResponse>(cancellationToken: ct);
            if (created?.Uri is null)
                return PublishOutcome.Fail("Bluesky accepted the post but returned no reference", StatusCodes.Status502BadGateway);

            // at://did:plc:xxx/app.bsky.feed.post/RKEY — the last segment is what a public URL needs.
            var rkey = created.Uri.Split('/').LastOrDefault();
            var publicUrl = rkey is null ? null : $"https://bsky.app/profile/{credentials.Handle}/post/{rkey}";

            // The same baseline Telegram writes, so ADR-065's "this would overwrite a live post"
            // guard has something to compare against here too. Keyed by target id rather than by
            // handle: the handle is renameable, the row is not.
            await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title,
                request.CedarJson, PublishNetworks.Bluesky, request.Target.Id.ToString(), ct);
            await db.SaveChangesAsync(ct);

            return PublishOutcome.Ok(new PublishReceipt(created.Uri, publicUrl));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure publishing draft {DraftId} to Bluesky", request.DraftId);
            return PublishOutcome.Fail($"Publish failed: {ex.GetType().Name}: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Verifies a handle and app password by opening a session — used by the connect flow.</summary>
    public static async Task<SessionResponse?> CreateSessionAsync(HttpClient http, BlueskyCredentials credentials, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/xrpc/com.atproto.server.createSession", new
        {
            identifier = credentials.Handle,
            password = credentials.AppPassword,
        }, ct);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SessionResponse>(cancellationToken: ct)
            : null;
    }

    public BlueskyCredentials? ReadCredentials(PublishTarget target)
    {
        var plaintext = secrets.TryUnprotect(target.CredentialsProtected);
        if (plaintext is null) return null;
        try
        {
            return JsonSerializer.Deserialize<BlueskyCredentials>(plaintext);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Bluesky's errors are JSON; its `message` is the part worth showing an author.</summary>
    private static string Describe(string body)
    {
        try
        {
            return (string?)JsonNode.Parse(body)?["message"] ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    public sealed record SessionResponse(string Did, string Handle, string AccessJwt, string RefreshJwt);
    private sealed record CreateRecordResponse(string? Uri, string? Cid);
}
