using CedarClerk.Server;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// Wave 1 item 7 — the durable notify-on-publish queue. What matters: enqueue writes a row and
// nothing else on the caller's path, the claim keeps kick and sweeper from both mailing, and a
// job whose post stopped being publicly published fails honestly instead of mailing a dead link.
public class BlogNotifyJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly BlogSubscriberNotifier _notifier;

    public BlogNotifyJobTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(_connection));
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<ResendEmailProvider>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", TenantUsername = "tenant1" });
        db.SaveChanges();

        _notifier = new BlogSubscriberNotifier(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<BlogSubscriberNotifier>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private CedarDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<CedarDbContext>();

    private Draft AddPublishedDraft(CedarDbContext db)
    {
        var draft = new Draft
        {
            OwnerId = "o1", Title = "Devlog #1", IsBlogPublished = true,
            BlogSlug = "devlog-1", BlogPublishedAt = DateTime.UtcNow,
        };
        db.Drafts.Add(draft);
        db.SaveChanges();
        return draft;
    }

    [Fact]
    public async Task Enqueue_writes_a_job_row_and_returns()
    {
        using var scope = _services.CreatePlatformScope();
        var db = Db(scope);
        var draft = AddPublishedDraft(db);

        await _notifier.EnqueueAsync(db, draft);

        // The kick runs in the background; the caller's guarantee is only that the row exists.
        Assert.True(await db.BlogNotifyJobs.AnyAsync(j => j.DraftId == draft.Id && j.OwnerId == "o1"));
    }

    [Fact]
    public async Task RunOne_marks_the_job_sent_for_a_published_post_with_confirmed_subscribers()
    {
        Guid jobId;
        using (var scope = _services.CreatePlatformScope())
        {
            var db = Db(scope);
            var draft = AddPublishedDraft(db);
            db.BlogSubscribers.Add(new BlogSubscriber
            {
                OwnerId = "o1", Email = "reader@example.com",
                ConfirmedAt = DateTime.UtcNow, UnsubscribeToken = "u1",
            });
            var job = new BlogNotifyJob { OwnerId = "o1", DraftId = draft.Id };
            db.BlogNotifyJobs.Add(job);
            db.SaveChanges();
            jobId = job.Id;
        }

        await _notifier.RunOneAsync(jobId, CancellationToken.None);

        using var check = _services.CreatePlatformScope();
        var done = await Db(check).BlogNotifyJobs.FirstAsync(j => j.Id == jobId);
        Assert.Null(done.Error);
        Assert.Equal(BlogNotifyJobStatus.Sent, done.Status);
        Assert.NotNull(done.SentAt);
    }

    [Fact]
    public async Task RunOne_fails_the_job_when_the_post_is_no_longer_public()
    {
        Guid jobId;
        using (var scope = _services.CreatePlatformScope())
        {
            var db = Db(scope);
            var draft = AddPublishedDraft(db);
            draft.IsBlogPublished = false;
            var job = new BlogNotifyJob { OwnerId = "o1", DraftId = draft.Id };
            db.BlogNotifyJobs.Add(job);
            db.SaveChanges();
            jobId = job.Id;
        }

        await _notifier.RunOneAsync(jobId, CancellationToken.None);

        using var check = _services.CreatePlatformScope();
        var done = await Db(check).BlogNotifyJobs.FirstAsync(j => j.Id == jobId);
        Assert.Equal(BlogNotifyJobStatus.Failed, done.Status);
        Assert.NotNull(done.Error);
    }

    [Fact]
    public async Task RunOne_never_touches_a_job_that_is_not_pending()
    {
        Guid jobId;
        using (var scope = _services.CreatePlatformScope())
        {
            var db = Db(scope);
            var draft = AddPublishedDraft(db);
            var job = new BlogNotifyJob { OwnerId = "o1", DraftId = draft.Id, Status = BlogNotifyJobStatus.Sent };
            db.BlogNotifyJobs.Add(job);
            db.SaveChanges();
            jobId = job.Id;
        }

        await _notifier.RunOneAsync(jobId, CancellationToken.None);

        using var check = _services.CreatePlatformScope();
        var untouched = await Db(check).BlogNotifyJobs.FirstAsync(j => j.Id == jobId);
        Assert.Equal(BlogNotifyJobStatus.Sent, untouched.Status);
        Assert.Null(untouched.SentAt);
    }

    [Fact]
    public async Task RunOne_fails_when_the_account_has_no_blog_host()
    {
        Guid jobId;
        using (var scope = _services.CreatePlatformScope())
        {
            var db = Db(scope);
            db.Users.Add(new ApplicationUser { Id = "o2", UserName = "o2" });
            var draft = new Draft
            {
                OwnerId = "o2", Title = "Post", IsBlogPublished = true,
                BlogSlug = "post", BlogPublishedAt = DateTime.UtcNow,
            };
            db.Drafts.Add(draft);
            var job = new BlogNotifyJob { OwnerId = "o2", DraftId = draft.Id };
            db.BlogNotifyJobs.Add(job);
            db.SaveChanges();
            jobId = job.Id;
        }

        await _notifier.RunOneAsync(jobId, CancellationToken.None);

        using var check = _services.CreatePlatformScope();
        var done = await Db(check).BlogNotifyJobs.FirstAsync(j => j.Id == jobId);
        Assert.Equal(BlogNotifyJobStatus.Failed, done.Status);
        Assert.Contains("host", done.Error);
    }
}
