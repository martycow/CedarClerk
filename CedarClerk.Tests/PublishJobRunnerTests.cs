using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Publishing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Tests;

// T-090 / ADR-081. The queue exists so a publish survives a browser and a restart — which means
// the tests that matter are about the two ways that goes wrong: a retry that posts twice, and a
// job that was sending when the process died.
public class PublishJobRunnerTests
{
    private sealed class FakeTarget(string network) : IPublishTarget
    {
        public string Network { get; } = network;
        public PublishCapabilities Capabilities { get; } = new() { Network = network };
        public int Calls;
        public PublishRequest? Last;
        public Func<PublishOutcome> Next = () => PublishOutcome.Ok(new PublishReceipt("1", null));

        public Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
        {
            Calls++;
            Last = request;
            return Task.FromResult(Next());
        }
    }

    private static (ServiceProvider Provider, SqliteConnection Connection, FakeTarget Target) Build()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var target = new FakeTarget(PublishNetworks.Telegram);
        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IPublishTarget>(target);
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreatePlatformScope())
            scope.ServiceProvider.GetRequiredService<CedarDbContext>().Database.EnsureCreated();

        return (provider, connection, target);
    }

    private static async Task<(Guid DraftId, Guid TargetId)> SeedAsync(ServiceProvider provider)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1", Email = "owner-1@test.local" });
        var draft = new Draft { OwnerId = "owner-1", Title = "T", CedarJson = "{}" };
        var target = new PublishTarget
        {
            OwnerId = "owner-1", Network = PublishNetworks.Telegram, DisplayName = "Channel", RemoteId = "-100",
        };
        db.Drafts.Add(draft);
        db.PublishTargets.Add(target);
        await db.SaveChangesAsync();
        return (draft.Id, target.Id);
    }

    private static async Task<PublishJob> QueueAsync(ServiceProvider provider, Guid draftId, Guid targetId,
        bool silent = false, bool pin = false)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var job = new PublishJob
        {
            OwnerId = "owner-1", DraftId = draftId, TargetId = targetId,
            Network = PublishNetworks.Telegram, Language = Languages.Russian,
            Silent = silent, PinAfterSend = pin,
        };
        db.PublishJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static async Task<PublishJob> ReadAsync(ServiceProvider provider, Guid jobId)
    {
        using var scope = provider.CreatePlatformScope();
        return await scope.ServiceProvider.GetRequiredService<CedarDbContext>()
            .PublishJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
    }

    private static async Task<PublishTarget> ReadTargetAsync(ServiceProvider provider, Guid targetId)
    {
        using var scope = provider.CreatePlatformScope();
        return await scope.ServiceProvider.GetRequiredService<CedarDbContext>()
            .PublishTargets.AsNoTracking().FirstAsync(t => t.Id == targetId);
    }

    private static PublishJobRunner Runner(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PublishJobRunner>.Instance,
            DisabledAnalytics);

    /// <summary>No client and no configuration, so <see cref="ProductAnalytics.Track"/> is a no-op.</summary>
    private static ProductAnalytics DisabledAnalytics =>
        new(null, new ConfigurationBuilder().Build(), NullLogger<ProductAnalytics>.Instance);

    [Fact]
    public async Task A_queued_job_runs_and_records_what_the_network_returned()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var job = await QueueAsync(provider, draftId, targetId);
        target.Next = () => PublishOutcome.Ok(new PublishReceipt("4242", "https://t.me/c/4242"));

        await Runner(provider).SweepAsync(CancellationToken.None);

        var stored = await ReadAsync(provider, job.Id);
        Assert.Equal(PublishJobStatus.Succeeded, stored.Status);
        Assert.Equal(1, target.Calls);
        Assert.Equal(1, stored.Attempts);
    }

    // A post that went out with its pictures left behind is a success with something to say, and
    // the target is where the author reads it. The X media-scope hint was written straight onto
    // the target and then overwritten with null by the success that followed, so Settings never
    // showed it (14.09.2026).
    [Fact]
    public async Task A_warning_on_a_successful_publish_lands_on_the_target_until_a_clean_one()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);

        await QueueAsync(provider, draftId, targetId);
        target.Next = () => PublishOutcome.Ok(new PublishReceipt("1", null), "pictures were left out");
        await Runner(provider).SweepAsync(CancellationToken.None);

        var warned = await ReadTargetAsync(provider, targetId);
        Assert.Equal("pictures were left out", warned.LastError);
        Assert.NotNull(warned.LastPublishedAt);

        await QueueAsync(provider, draftId, targetId);
        target.Next = () => PublishOutcome.Ok(new PublishReceipt("2", null));
        await Runner(provider).SweepAsync(CancellationToken.None);

        Assert.Null((await ReadTargetAsync(provider, targetId)).LastError);
    }

    [Fact]
    public async Task A_job_carries_silent_and_pin_to_the_target()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        await QueueAsync(provider, draftId, targetId, silent: true, pin: true);

        await Runner(provider).SweepAsync(CancellationToken.None);

        Assert.NotNull(target.Last);
        Assert.True(target.Last!.Silent);
        Assert.True(target.Last.PinAfterSend);
    }

    [Fact]
    public async Task A_finished_job_is_never_run_again_by_a_later_sweep()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        await QueueAsync(provider, draftId, targetId);

        await Runner(provider).SweepAsync(CancellationToken.None);
        await Runner(provider).SweepAsync(CancellationToken.None);
        await Runner(provider).SweepAsync(CancellationToken.None);

        // The whole point of the queue: three sweeps, one post.
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public async Task A_refusal_is_permanent_and_is_not_retried()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var job = await QueueAsync(provider, draftId, targetId);
        target.Next = () => PublishOutcome.Fail("Telegram rejected the post", StatusCodes.Status400BadRequest);

        await Runner(provider).SweepAsync(CancellationToken.None);
        await Runner(provider).SweepAsync(CancellationToken.None);

        var stored = await ReadAsync(provider, job.Id);
        Assert.Equal(PublishJobStatus.Failed, stored.Status);
        Assert.Equal(1, target.Calls);
        Assert.Contains("rejected", stored.Error);
    }

    [Fact]
    public async Task A_transport_failure_is_retried_after_a_backoff()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var job = await QueueAsync(provider, draftId, targetId);
        target.Next = () => PublishOutcome.Fail(ErrorMessages.BotNotRunning, StatusCodes.Status503ServiceUnavailable);

        await Runner(provider).SweepAsync(CancellationToken.None);

        var afterFirst = await ReadAsync(provider, job.Id);
        Assert.Equal(PublishJobStatus.Pending, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        // Not immediately: a backoff is what keeps a broken bot from being hammered every 15s.
        Assert.NotNull(afterFirst.NextAttemptAt);
        Assert.True(afterFirst.NextAttemptAt > DateTime.UtcNow);

        // Still in its backoff window, so the sweep must leave it alone.
        await Runner(provider).SweepAsync(CancellationToken.None);
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public async Task Retrying_stops_at_the_attempt_limit()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var job = await QueueAsync(provider, draftId, targetId);
        target.Next = () => PublishOutcome.Fail("gateway", StatusCodes.Status502BadGateway);

        for (var i = 0; i < PublishJobRunner.MaxAttempts + 2; i++)
        {
            // Pull each backoff forward rather than sleeping through it.
            using (var scope = provider.CreatePlatformScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
                var row = await db.PublishJobs.FirstAsync(j => j.Id == job.Id);
                row.NextAttemptAt = null;
                await db.SaveChangesAsync();
            }
            await Runner(provider).SweepAsync(CancellationToken.None);
        }

        var stored = await ReadAsync(provider, job.Id);
        Assert.Equal(PublishJobStatus.Failed, stored.Status);
        Assert.Equal(PublishJobRunner.MaxAttempts, target.Calls);
    }

    // The one that a naive queue gets wrong: the process died mid-send. The request had already
    // left, so nothing here can know whether it arrived — and retrying would be how one post
    // becomes two.
    [Fact]
    public async Task A_job_abandoned_mid_send_becomes_Unknown_and_is_never_resent()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var job = await QueueAsync(provider, draftId, targetId);

        using (var scope = provider.CreatePlatformScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
            var row = await db.PublishJobs.FirstAsync(j => j.Id == job.Id);
            row.Status = PublishJobStatus.Running;
            row.StartedAt = DateTime.UtcNow - PublishJobRunner.AbandonedAfter - TimeSpan.FromMinutes(1);
            await db.SaveChangesAsync();
        }

        await Runner(provider).SweepAsync(CancellationToken.None);

        var stored = await ReadAsync(provider, job.Id);
        Assert.Equal(PublishJobStatus.Unknown, stored.Status);
        Assert.Equal(0, target.Calls);
        Assert.Contains("check the destination", stored.Error);
    }

    // Found in production 01.08.2026: a document Telegram had *refused* (400) was sent three times.
    // TelegramPublishTarget mapped every ApiRequestException to 502, and 502 reads as "could not
    // have posted". The status a network returns has to be the network's own verdict.
    [Fact]
    public async Task A_rate_limit_is_retried_but_a_content_refusal_is_not()
    {
        foreach (var (status, expected, expectedCalls) in new[]
                 {
                     (StatusCodes.Status429TooManyRequests, PublishJobStatus.Pending, 1),
                     (StatusCodes.Status400BadRequest, PublishJobStatus.Failed, 1),
                 })
        {
            var (provider, connection, target) = Build();
            using var _ = connection;
            var (draftId, targetId) = await SeedAsync(provider);
            var job = await QueueAsync(provider, draftId, targetId);
            target.Next = () => PublishOutcome.Fail("refused", status);

            await Runner(provider).SweepAsync(CancellationToken.None);

            var stored = await ReadAsync(provider, job.Id);
            Assert.Equal(expected, stored.Status);
            Assert.Equal(expectedCalls, target.Calls);
        }
    }

    private static async Task<List<PublishJob>> QueueThreadAsync(ServiceProvider provider, Guid draftId, Guid targetId, int parts)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var threadId = Guid.NewGuid();
        var jobs = Enumerable.Range(0, parts).Select(i => new PublishJob
        {
            OwnerId = "owner-1", DraftId = draftId, TargetId = targetId,
            Network = PublishNetworks.Telegram, Language = Languages.Russian,
            ThreadId = threadId, PartIndex = i, PartCount = parts,
        }).ToList();
        db.PublishJobs.AddRange(jobs);
        await db.SaveChangesAsync();
        return jobs;
    }

    // T-106 — a thread goes out in order, and a part never goes out on its own. The sweep is what
    // this asserts against: it picks jobs by age, so without the ordering rule part 3 would be
    // sent while part 2 was still running.
    [Fact]
    public async Task Thread_parts_go_out_in_order_and_never_ahead_of_each_other()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var jobs = await QueueThreadAsync(provider, draftId, targetId, 3);

        // One sweep sends part 1 and, through the kick, its successors — so drive it explicitly.
        for (var i = 0; i < 3; i++) await Runner(provider).SweepAsync(CancellationToken.None);

        foreach (var job in jobs)
            Assert.Equal(PublishJobStatus.Succeeded, (await ReadAsync(provider, job.Id)).Status);
        Assert.Equal(3, target.Calls);
    }

    // The case that decides whether one job per part was worth it: part 2 fails, and 3 must not go
    // out — a channel with parts 1 and 3 of a document is worse than one with part 1 alone.
    [Fact]
    public async Task A_broken_thread_holds_back_the_parts_after_it()
    {
        var (provider, connection, target) = Build();
        using var _ = connection;
        var (draftId, targetId) = await SeedAsync(provider);
        var jobs = await QueueThreadAsync(provider, draftId, targetId, 3);

        var call = 0;
        target.Next = () => ++call == 2
            ? PublishOutcome.Fail("Telegram rejected the post", StatusCodes.Status400BadRequest)
            : PublishOutcome.Ok(new PublishReceipt(call.ToString(), null));

        for (var i = 0; i < 3; i++) await Runner(provider).SweepAsync(CancellationToken.None);

        Assert.Equal(PublishJobStatus.Succeeded, (await ReadAsync(provider, jobs[0].Id)).Status);
        Assert.Equal(PublishJobStatus.Failed, (await ReadAsync(provider, jobs[1].Id)).Status);

        var third = await ReadAsync(provider, jobs[2].Id);
        Assert.Equal(PublishJobStatus.Failed, third.Status);
        Assert.Contains("gap", third.Error);
        // Two sends: the successful first part and the failing second. The third never went out.
        Assert.Equal(2, target.Calls);
    }
}
