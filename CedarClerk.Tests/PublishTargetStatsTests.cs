using System.Net;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Publishing;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// T-241. What the readers must get right: the JSON of each network lands in the right columns,
// X leaves engagement null rather than zero, only OUR posts count on Bluesky, and a dead X token
// marks the target for reconnect instead of throwing out of the job.
public class PublishTargetStatsTests : IDisposable
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly PublishTargetSecrets _secrets = new(DataProtectionProvider.Create("cedar-test"));

    public PublishTargetStatsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(_connection));
        _services = services.BuildServiceProvider();

        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", Email = "o1@test.local" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private XPublishTarget X(CedarDbContext db, IHttpClientFactory factory, IConfiguration? cfg = null) =>
        new(db, _secrets, factory, cfg ?? new ConfigurationBuilder().Build(), new MediaPaths(Path.GetTempPath()), NullLogger<XPublishTarget>.Instance);

    private BlueskyPublishTarget Bluesky(CedarDbContext db, IHttpClientFactory factory) =>
        new(db, _secrets, factory, new ConfigurationBuilder().Build(), new MediaPaths(Path.GetTempPath()), NullLogger<BlueskyPublishTarget>.Instance);

    private PublishTarget AddXTarget(CedarDbContext db, DateTime expiresAt)
    {
        var target = new PublishTarget
        {
            OwnerId = "o1", Network = PublishNetworks.X, DisplayName = "@marty", RemoteId = "42",
            CredentialsProtected = _secrets.Protect(JsonSerializer.Serialize(
                new XCredentials("42", "marty", "access-token", "refresh-token", expiresAt))),
        };
        db.PublishTargets.Add(target);
        db.SaveChanges();
        return target;
    }

    private PublishTarget AddBlueskyTarget(CedarDbContext db)
    {
        var target = new PublishTarget
        {
            OwnerId = "o1", Network = PublishNetworks.Bluesky, DisplayName = "marty.bsky.social", RemoteId = "did:plc:abc",
            CredentialsProtected = _secrets.Protect(JsonSerializer.Serialize(
                new BlueskyCredentials("marty.bsky.social", "app-pass", BlueskyPublishTarget.DefaultService))),
        };
        db.PublishTargets.Add(target);
        db.SaveChanges();
        return target;
    }

    [Fact]
    public async Task X_reads_public_metrics_with_the_bearer_and_leaves_engagement_null()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var target = AddXTarget(db, DateTime.UtcNow.AddHours(1));
        var handler = new StubHandler(_ => Json("""{"data":{"id":"42","username":"marty","public_metrics":{"followers_count":1234,"following_count":7,"tweet_count":89,"listed_count":1}}}"""));
        var factory = new StubFactory(handler);

        var snapshot = await PublishTargetStats.TakeAsync(db, target, factory, X(db, factory), Bluesky(db, factory), NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(PublishNetworks.X, snapshot.Network);
        Assert.Equal(target.Id, snapshot.TargetId);
        Assert.Equal("o1", snapshot.OwnerId);
        Assert.Equal(1234, snapshot.FollowerCount);
        Assert.Equal(89, snapshot.PostCount);
        Assert.Null(snapshot.LikeCount);
        Assert.Null(snapshot.CommentCount);
        Assert.Null(snapshot.RepostCount);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{XPublishTarget.ApiBase}/2/users/me?user.fields=public_metrics", request.RequestUri!.ToString());
        Assert.Equal("access-token", request.Headers.Authorization!.Parameter);
        Assert.Null(target.LastError);
    }

    [Fact]
    public async Task X_401_marks_the_target_for_reconnect_and_yields_no_row()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var target = AddXTarget(db, DateTime.UtcNow.AddHours(1));
        var factory = new StubFactory(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var snapshot = await PublishTargetStats.TakeAsync(db, target, factory, X(db, factory), Bluesky(db, factory), NullLogger.Instance, CancellationToken.None);

        Assert.Null(snapshot);
        Assert.Equal(ErrorMessages.XReconnect, target.LastError);
    }

    [Fact]
    public async Task X_expired_token_that_cannot_be_refreshed_is_a_reconnect_not_a_read()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var target = AddXTarget(db, DateTime.UtcNow.AddMinutes(-5));
        var handler = new StubHandler(_ => Json("{}"));
        var factory = new StubFactory(handler);

        // No X client id/secret configured, so the refresh cannot even be attempted.
        var snapshot = await PublishTargetStats.TakeAsync(db, target, factory, X(db, factory), Bluesky(db, factory), NullLogger.Instance, CancellationToken.None);

        Assert.Null(snapshot);
        Assert.Equal(ErrorMessages.XReconnect, target.LastError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task X_expired_token_is_refreshed_and_saved_before_the_read()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var target = AddXTarget(db, DateTime.UtcNow.AddMinutes(-5));
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/2/oauth2/token"
            ? Json("""{"access_token":"new-access","refresh_token":"new-refresh","expires_in":7200}""")
            : Json("""{"data":{"public_metrics":{"followers_count":5,"tweet_count":2}}}"""));
        var factory = new StubFactory(handler);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Consts.X.ClientIdCfg] = "client", [Consts.X.ClientSecretCfg] = "secret",
        }).Build();
        var x = X(db, factory, cfg);

        var snapshot = await PublishTargetStats.TakeAsync(db, target, factory, x, Bluesky(db, factory), NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(5, snapshot.FollowerCount);
        Assert.Equal("new-access", handler.Requests[1].Headers.Authorization!.Parameter);
        Assert.Equal("new-refresh", x.ReadCredentials(target)!.RefreshToken);
    }

    [Fact]
    public async Task Bluesky_reads_the_public_profile_and_sums_engagement_over_our_posts_only()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var target = AddBlueskyTarget(db);
        var draft = new Draft { OwnerId = "o1", Title = "T", CedarJson = "{}" };
        db.Drafts.Add(draft);
        db.PublishJobs.Add(new PublishJob
        {
            OwnerId = "o1", DraftId = draft.Id, TargetId = target.Id, Network = PublishNetworks.Bluesky,
            Status = PublishJobStatus.Succeeded, RemoteId = "at://did:plc:abc/app.bsky.feed.post/ours|bafycid",
        });
        db.PublishJobs.Add(new PublishJob
        {
            OwnerId = "o1", DraftId = draft.Id, TargetId = target.Id, Network = PublishNetworks.Bluesky,
            Status = PublishJobStatus.Failed, RemoteId = "at://did:plc:abc/app.bsky.feed.post/failed|bafycid",
        });
        db.SaveChanges();

        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("getProfile")
            ? Json("""{"did":"did:plc:abc","handle":"marty.bsky.social","followersCount":321,"followsCount":10,"postsCount":45}""")
            : Json("""
                {"feed":[
                  {"post":{"uri":"at://did:plc:abc/app.bsky.feed.post/ours","likeCount":7,"replyCount":2,"repostCount":3}},
                  {"post":{"uri":"at://did:plc:abc/app.bsky.feed.post/failed","likeCount":100,"replyCount":100,"repostCount":100}},
                  {"post":{"uri":"at://did:plc:abc/app.bsky.feed.post/handwritten","likeCount":50,"replyCount":5,"repostCount":9}}
                ]}
                """));
        var factory = new StubFactory(handler);

        var snapshot = await PublishTargetStats.TakeAsync(db, target, factory, X(db, factory), Bluesky(db, factory), NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(PublishNetworks.Bluesky, snapshot.Network);
        Assert.Equal(321, snapshot.FollowerCount);
        Assert.Equal(45, snapshot.PostCount);
        Assert.Equal(7, snapshot.LikeCount);
        Assert.Equal(2, snapshot.CommentCount);
        Assert.Equal(3, snapshot.RepostCount);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("public.api.bsky.app", r.RequestUri!.Host));
        Assert.Contains("actor=did%3Aplc%3Aabc", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("filter=posts_no_replies", handler.Requests[1].RequestUri!.Query);
        Assert.All(handler.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task Bluesky_without_our_posts_skips_the_feed_and_reports_zero_engagement()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var target = AddBlueskyTarget(db);
        var handler = new StubHandler(_ => Json("""{"followersCount":3,"postsCount":1}"""));
        var factory = new StubFactory(handler);

        var snapshot = await PublishTargetStats.TakeAsync(db, target, factory, X(db, factory), Bluesky(db, factory), NullLogger.Instance, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot.LikeCount);
        Assert.Equal(0, snapshot.CommentCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_job_isolates_one_failing_account_from_the_others()
    {
        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var broken = AddBlueskyTarget(db);
        var healthy = new PublishTarget { OwnerId = "o1", Network = PublishNetworks.Bluesky, DisplayName = "second", RemoteId = "did:plc:second" };
        var inactive = new PublishTarget { OwnerId = "o1", Network = PublishNetworks.Bluesky, DisplayName = "gone", RemoteId = "did:plc:gone", IsActive = false };
        var telegram = new PublishTarget { OwnerId = "o1", Network = PublishNetworks.Telegram, DisplayName = "tg", RemoteId = "-100" };
        db.PublishTargets.AddRange(healthy, inactive, telegram);
        db.SaveChanges();

        var handler = new StubHandler(request => request.RequestUri!.Query.Contains("did%3Aplc%3Aabc")
            ? throw new HttpRequestException("boom")
            : Json("""{"followersCount":9,"postsCount":4}"""));
        var factory = new StubFactory(handler);
        // A fresh strict scope, as Quartz hands the job: the job's own UsePlatform is what opens it.
        using var jobScope = _services.CreateScope();
        var jobDb = jobScope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var job = new SnapshotPublishTargetStatsJob(jobDb, jobScope.ServiceProvider.GetRequiredService<TenantProvider>(), factory,
            X(jobDb, factory), Bluesky(jobDb, factory), NullLogger<SnapshotPublishTargetStatsJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var rows = await db.PublishTargetStatSnapshots.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(healthy.Id, row.TargetId);
        Assert.Equal(9, row.FollowerCount);
        Assert.DoesNotContain(rows, r => r.TargetId == broken.Id || r.TargetId == inactive.Id || r.TargetId == telegram.Id);
    }
}
