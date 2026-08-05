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
        ThreadPartCharacters = BlueskyPostBuilder.MaxGraphemes,
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
        var blogUrl = MicroThreadPlan.BlogUrl(draft, request.Language, cfg);

        // ADR-094 — a thread carries the document itself (recomputed, T-106's principle); a
        // single post carries the author's override or the teaser (ADR-077).
        BlueskyPost post;
        if (request.Part is { Count: > 1 } threadPart)
        {
            var parts = MicroThreadPlan.Parts(request.CedarJson, PublishNetworks.Bluesky, blogUrl);
            if (threadPart.Index >= parts.Count)
                return PublishOutcome.Fail(ErrorMessages.ThreadPartGone, StatusCodes.Status409Conflict);
            var text = parts[threadPart.Index];
            if (threadPart.Index == threadPart.Count - 1 && blogUrl is not null)
            {
                text = $"{text}\n\n{blogUrl}";
                var byteLength = System.Text.Encoding.UTF8.GetByteCount(text);
                post = new BlueskyPost(text,
                    [new BlueskyFacet(byteLength - System.Text.Encoding.UTF8.GetByteCount(blogUrl), byteLength, blogUrl)]);
            }
            else
            {
                post = new BlueskyPost(text, []);
            }
        }
        else
        {
            post = BlueskyPostBuilder.Build(request.AuthorText, request.CedarJson, blogUrl);
        }

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

            // ADR-094 — the reply record wants both the thread root and the parent, each as
            // uri+cid; that pair is exactly what this target's RemoteId carries since threads.
            if (request.Part is { ReplyToRemoteId: not null } reply
                && ParseRef(reply.ReplyToRemoteId) is { } parent
                && ParseRef(reply.RootRemoteId ?? reply.ReplyToRemoteId) is { } root)
            {
                record["reply"] = new JsonObject
                {
                    ["root"] = new JsonObject { ["uri"] = root.Uri, ["cid"] = root.Cid },
                    ["parent"] = new JsonObject { ["uri"] = parent.Uri, ["cid"] = parent.Cid },
                };
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

            // "uri|cid", because a reply reference needs both halves and RemoteId is the one
            // field a later part gets to see (ADR-094). The public URL above still comes from
            // the uri alone, so nothing user-facing changes shape.
            var remoteId = created.Cid is null ? created.Uri : $"{created.Uri}|{created.Cid}";

            // The same baseline Telegram writes, so ADR-065's "this would overwrite a live post"
            // guard has something to compare against here too. Keyed by target id rather than by
            // handle: the handle is renameable, the row is not. Once per publication — on the
            // last part for a thread.
            if (request.Part is not { Count: > 1 } || request.Part.Index == request.Part.Count - 1)
            {
                await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title,
                    request.CedarJson, PublishNetworks.Bluesky, request.Target.Id.ToString(), ct);
            }
            await db.SaveChangesAsync(ct);

            return PublishOutcome.Ok(new PublishReceipt(remoteId, publicUrl));
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

    /// <summary>The two halves of a stored "uri|cid" RemoteId; null when the cid half is missing.</summary>
    private static (string Uri, string Cid)? ParseRef(string remoteId)
    {
        var split = remoteId.IndexOf('|');
        return split > 0 && split < remoteId.Length - 1
            ? (remoteId[..split], remoteId[(split + 1)..])
            : null;
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
