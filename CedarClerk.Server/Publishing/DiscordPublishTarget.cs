using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// Credentials for one Discord channel webhook, as stored (encrypted) on the PublishTarget row.
/// The URL contains its own token, so it is the whole credential; guild and channel ids are
/// captured at connect time because they are what a public message URL is built from.
/// </summary>
public sealed record DiscordCredentials(string WebhookUrl, string? GuildId, string? ChannelId, string Name);

/// <summary>
/// Discord over a channel webhook (T-161, ADR-131) — no bot, no OAuth, no app review. The message
/// is the ADR-077 short post: the author's override or a derived teaser, plus the blog link, which
/// Discord unfurls into a card from the blog's OG tags (T-174). That card is why this target
/// attaches no media of its own.
/// </summary>
public partial class DiscordPublishTarget(
    CedarDbContext db,
    PublishTargetSecrets secrets,
    IHttpClientFactory httpFactory,
    IConfiguration cfg,
    ILogger<DiscordPublishTarget> logger) : IPublishTarget
{
    [GeneratedRegex(@"^https://(?:discord\.com|discordapp\.com)/api/(?:v\d+/)?webhooks/(\d+)/[\w-]+$")]
    public static partial Regex WebhookUrlShape();

    public string Network => PublishNetworks.Discord;

    public PublishCapabilities Capabilities { get; } = new()
    {
        Network = PublishNetworks.Discord,
        MaxCharacters = DiscordPostBuilder.MaxChars,
        ThreadPartCharacters = DiscordPostBuilder.MaxChars,
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
        // Webhook messages have no reply structure — a "thread" would be N disconnected posts.
        SupportsThreads = false,
        PostsHavePublicUrls = true,
        DerivesShortPost = true,
    };

    public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var credentials = ReadCredentials(request.Target);
        if (credentials is null)
            return PublishOutcome.Fail(ErrorMessages.DiscordReconnect, StatusCodes.Status401Unauthorized);

        if (request.Part is { Count: > 1 })
            return PublishOutcome.Fail("Discord does not support threads — publish as an announcement with a link",
                StatusCodes.Status422UnprocessableEntity);

        var draft = await db.Drafts.FirstAsync(d => d.Id == request.DraftId, ct);
        var blogUrl = MicroThreadPlan.BlogUrl(draft, request.Language, cfg);
        var content = DiscordPostBuilder.Build(request.AuthorText, request.CedarJson, blogUrl);
        if (content.Trim().Length == 0)
            return PublishOutcome.Fail("Nothing to post — write a Discord version or some body text");

        var http = httpFactory.CreateClient();
        try
        {
            // allowed_mentions kills @everyone/@here/@role pings a teaser could otherwise carry
            // into the whole server (ADR-131). wait=true makes Discord return the created message.
            var response = await http.PostAsJsonAsync($"{credentials.WebhookUrl}?wait=true", new
            {
                content,
                allowed_mentions = new { parse = Array.Empty<string>() },
            }, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized)
                return PublishOutcome.Fail(ErrorMessages.DiscordReconnect, StatusCodes.Status401Unauthorized);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Discord rejected a post for draft {DraftId}: {Status} {Body}",
                    request.DraftId, (int)response.StatusCode, body);
                return PublishOutcome.Fail($"Discord rejected the post: {Describe(body)}", StatusCodes.Status502BadGateway);
            }

            var created = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var messageId = created.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (messageId is null)
                return PublishOutcome.Fail("Discord accepted the post but returned no message", StatusCodes.Status502BadGateway);

            var channelId = created.TryGetProperty("channel_id", out var ch) ? ch.GetString() : credentials.ChannelId;
            var publicUrl = credentials.GuildId is not null && channelId is not null
                ? $"https://discord.com/channels/{credentials.GuildId}/{channelId}/{messageId}"
                : null;

            await DraftRevisionService.RecordAsync(db, request.DraftId, request.Language, request.Title,
                request.CedarJson, PublishNetworks.Discord, request.Target.Id.ToString(), ct);
            await db.SaveChangesAsync(ct);

            return PublishOutcome.Ok(new PublishReceipt(messageId, publicUrl));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected failure publishing draft {DraftId} to Discord", request.DraftId);
            return PublishOutcome.Fail($"Publish failed: {ex.GetType().Name}: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
    }

    public DiscordCredentials? ReadCredentials(PublishTarget target)
    {
        var plaintext = secrets.TryUnprotect(target.CredentialsProtected);
        if (plaintext is null) return null;
        try
        {
            return JsonSerializer.Deserialize<DiscordCredentials>(plaintext);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Discord's errors are JSON; `message` is the half worth showing an author.</summary>
    private static string Describe(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.TryGetProperty("message", out var m)
                ? m.GetString() ?? body
                : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
