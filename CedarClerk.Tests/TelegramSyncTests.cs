using System.Net;
using System.Text;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Publishing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot;

namespace CedarClerk.Tests;

// T-180 (ADR-278). Sync edits the message the last send left behind; everything around the edit —
// which message, whether it is one message at all, who owns the channel — is decided here, and the
// edit itself is proved against a stubbed Bot API since there is no bot in a test.
public class TelegramSyncTests : IDisposable
{
    private const string Owner = "o1";
    private const long ChatId = -1001234567890;
    private const int MessageId = 77;
    private static readonly DateTime SentAt = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private const string ParagraphJson =
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Edited body."}]}]}""";

    private readonly string mediaDir = Path.Combine(Path.GetTempPath(), "cedar-sync-" + Guid.NewGuid().ToString("N"));

    public TelegramSyncTests() => Directory.CreateDirectory(mediaDir);
    public void Dispose() { try { Directory.Delete(mediaDir, true); } catch (IOException) { } }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.RequestUri!.AbsolutePath, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(int status, string json) =>
        new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Edited() => Json(200,
        "{\"ok\":true,\"result\":{\"message_id\":" + MessageId + ",\"date\":1725000000,\"chat\":{\"id\":" + ChatId + ",\"type\":\"channel\",\"title\":\"Test\"}}}");

    private static HttpResponseMessage Refused(int code, string description, string extra = "") => Json(code,
        "{\"ok\":false,\"error_code\":" + code + ",\"description\":\"" + description + "\"" + extra + "}");

    private static TelegramBotClient Client(StubHandler handler) =>
        new(new TelegramBotClientOptions("123456:ABCDEF"), new HttpClient(handler));

    private static Draft Seed(CedarDbContext db, bool withChannel = true, bool sent = true)
    {
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = Owner, IsAdmin = true });
        if (withChannel)
        {
            var channel = new Channel { OwnerId = Owner, Title = "Test", TelegramChatId = ChatId, Username = "testchan" };
            db.Channels.Add(channel);
            db.SaveChanges();
            TelegramTargetProjection.EnsureAsync(db, channel).GetAwaiter().GetResult();
        }
        var draft = new Draft
        {
            OwnerId = Owner,
            Title = "Devlog",
            CedarJson = ParagraphJson,
            PrimaryLanguage = "en",
            UpdatedAt = SentAt.AddHours(2),
        };
        if (sent)
        {
            draft.LastTelegramChatId = ChatId.ToString();
            draft.LastTelegramMessageId = MessageId;
            draft.LastTelegramUsername = "testchan";
            draft.LastTelegramSentAt = SentAt;
        }
        db.Drafts.Add(draft);
        db.SaveChanges();
        return draft;
    }

    private TelegramPublishTarget Target(CedarDbContext db)
    {
        var cfg = new ConfigurationBuilder().Build();
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var bot = new TelegramBotService(cfg, NullLogger<TelegramBotService>.Instance, scopes,
            new ProductAnalytics(null, cfg, NullLogger<ProductAnalytics>.Instance));
        return new TelegramPublishTarget(db, bot, cfg, new MediaPaths(mediaDir),
            new MediaGrant(DataProtectionProvider.Create("cedar-sync-test")), NullLogger<TelegramPublishTarget>.Instance);
    }

    private static Task<PostEndpoints.TelegramSyncResult> Sync(CedarDbContext db, Draft draft, TelegramPublishTarget target, StubHandler handler) =>
        PostEndpoints.SyncTelegramAsync(draft.Id, Owner, null, db,
            (request, chatId, messageId, ct) => target.EditAsync(Client(handler), request, chatId, messageId, ct));

    [Fact]
    public async Task Sync_edits_the_message_with_the_blocks_render_and_restamps()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        var handler = new StubHandler(_ => Edited());

        var result = await Sync(db, draft, Target(db), handler);

        Assert.True(result.Success, result.Error);
        Assert.Equal(MessageId, result.MessageId);
        Assert.Equal("https://t.me/testchan/77", result.Url);
        Assert.False(result.Unchanged);

        var call = Assert.Single(handler.Calls);
        Assert.EndsWith("/editMessageText", call.Path);
        Assert.Contains("\"message_id\":77", call.Body);
        Assert.Contains("rich_message", call.Body);
        Assert.Contains("Edited body.", call.Body);

        Assert.True(draft.LastTelegramSentAt > SentAt);
        Assert.Equal(result.SyncedAt, draft.LastTelegramSentAt);
        Assert.Contains(db.DraftRevisions, r => r.DraftId == draft.Id && r.Kind == DraftRevisionService.Kinds.Telegram);
    }

    [Fact]
    public async Task Not_modified_is_a_success_that_says_so()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        var handler = new StubHandler(_ => Refused(400,
            "Bad Request: message is not modified: specified new message content and reply markup are exactly the same"));

        var result = await Sync(db, draft, Target(db), handler);

        Assert.True(result.Success, result.Error);
        Assert.True(result.Unchanged);
        Assert.True(draft.LastTelegramSentAt > SentAt);
    }

    [Theory]
    [InlineData("Bad Request: message can't be edited")]
    [InlineData("Bad Request: message to edit not found")]
    public async Task A_message_that_is_gone_is_409_with_telegrams_words(string description)
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        var handler = new StubHandler(_ => Refused(400, description));

        var result = await Sync(db, draft, Target(db), handler);

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Contains(description, result.Error);
        Assert.Equal(SentAt, draft.LastTelegramSentAt);
    }

    [Fact]
    public async Task Forbidden_flood_and_the_rest_keep_the_publish_mapping()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        var target = Target(db);

        var forbidden = await Sync(db, draft, target, new StubHandler(_ => Refused(403, "Forbidden: bot is not a member of the channel chat")));
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);

        var flood = await Sync(db, draft, target, new StubHandler(_ => Refused(429, "Too Many Requests: retry after 7", ",\"parameters\":{\"retry_after\":7}")));
        Assert.Equal(StatusCodes.Status429TooManyRequests, flood.StatusCode);
        Assert.Contains("retry after 7s", flood.Error);

        var outage = await Sync(db, draft, target, new StubHandler(_ => Refused(502, "Bad Gateway")));
        Assert.Equal(StatusCodes.Status502BadGateway, outage.StatusCode);
    }

    [Fact]
    public async Task Nothing_sent_yet_is_404()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db, sent: false);
        var handler = new StubHandler(_ => Edited());

        var result = await Sync(db, draft, Target(db), handler);

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(ErrorMessages.TelegramNotSentYet, result.Error);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task A_thread_is_409_and_never_reaches_telegram()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        db.PublishJobs.Add(new PublishJob
        {
            OwnerId = Owner, DraftId = draft.Id, Network = "telegram", Status = PublishJobStatus.Succeeded,
            ThreadId = Guid.NewGuid(), PartIndex = 2, PartCount = 3, RemoteId = MessageId.ToString(),
        });
        db.SaveChanges();
        var handler = new StubHandler(_ => Edited());

        var result = await Sync(db, draft, Target(db), handler);

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(ErrorMessages.TelegramThreadNotSyncable, result.Error);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task A_disconnected_channel_is_403()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db, withChannel: false);
        var handler = new StubHandler(_ => Edited());

        var result = await Sync(db, draft, Target(db), handler);

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task A_bot_that_is_not_running_is_503()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        var target = Target(db);

        var result = await PostEndpoints.SyncTelegramAsync(draft.Id, Owner, null, db, target.EditAsync);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(ErrorMessages.BotNotRunning, result.Error);
    }
}
