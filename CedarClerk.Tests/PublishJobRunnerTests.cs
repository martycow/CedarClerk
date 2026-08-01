using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Publishing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

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
        public Func<PublishOutcome> Next = () => PublishOutcome.Ok(new PublishReceipt("1", null));

        public Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Next());
        }
    }

    private static (ServiceProvider Provider, SqliteConnection Connection, FakeTarget Target) Build()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var target = new FakeTarget(PublishNetworks.Telegram);
        var services = new ServiceCollection();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IPublishTarget>(target);
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<CedarDbContext>().Database.EnsureCreated();

        return (provider, connection, target);
    }

    private static async Task<(Guid DraftId, Guid TargetId)> SeedAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
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

    private static async Task<PublishJob> QueueAsync(ServiceProvider provider, Guid draftId, Guid targetId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var job = new PublishJob
        {
            OwnerId = "owner-1", DraftId = draftId, TargetId = targetId,
            Network = PublishNetworks.Telegram, Language = Languages.Russian,
        };
        db.PublishJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static async Task<PublishJob> ReadAsync(ServiceProvider provider, Guid jobId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CedarDbContext>()
            .PublishJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
    }

    private static PublishJobRunner Runner(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PublishJobRunner>.Instance);

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
            using (var scope = provider.CreateScope())
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

        using (var scope = provider.CreateScope())
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
}
