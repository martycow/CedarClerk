using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// ADR-317 §4 and T-420: Telegram times and links are read from ChannelPost, and the calendar draws
// what went out without a schedule beside what was scheduled.
public class PublishEventsTests
{
    private const string A = "owner-a";
    private const string B = "owner-b";

    private static readonly DateTime Noon = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = A, UserName = "a@x.test", Email = "a@x.test" });
        db.Users.Add(new ApplicationUser { Id = B, UserName = "b@x.test", Email = "b@x.test" });
        db.SaveChanges();
        return db;
    }

    private static Guid AddProject(CedarDbContext db, string owner = A)
    {
        var project = new Project { OwnerId = owner, Name = "Game" };
        db.Projects.Add(project);
        return project.Id;
    }

    private static Draft AddDraft(CedarDbContext db, string title, string owner = A, Guid? project = null)
    {
        var draft = new Draft { OwnerId = owner, Title = title, ProjectId = project };
        db.Drafts.Add(draft);
        return draft;
    }

    private static Channel AddChannel(CedarDbContext db, string? username, string owner = A)
    {
        var channel = new Channel { OwnerId = owner, Title = "Testing", Username = username, TelegramChatId = -100 };
        db.Channels.Add(channel);
        return channel;
    }

    private static ChannelPost Send(Draft draft, Channel channel, int messageId, DateTime at) => new()
    {
        OwnerId = draft.OwnerId, DraftId = draft.Id, ChannelId = channel.Id, TelegramMessageId = messageId, PublishedAt = at,
    };

    [Fact]
    public async Task Every_telegram_send_is_an_event_with_its_own_time_and_link()
    {
        using var db = NewDb();
        var draft = AddDraft(db, "Devlog");
        // The draft's own columns name the latest send only, and a later edit moves UpdatedAt.
        draft.LastTelegramMessageId = 20;
        draft.UpdatedAt = Noon.AddDays(5);
        var channel = AddChannel(db, "testingandfun");
        db.ChannelPosts.AddRange(Send(draft, channel, 10, Noon.AddDays(-3)), Send(draft, channel, 20, Noon));
        await db.SaveChangesAsync();

        var events = await PublishedPosts.EventsAsync(db, A);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(PublishNetworks.Telegram, e.Network));
        Assert.Equal([Noon, Noon.AddDays(-3)], events.Select(e => e.PublishedAt));
        Assert.Equal("https://t.me/testingandfun/20", events[0].PublicUrl);
        Assert.Equal("https://t.me/testingandfun/10", events[1].PublicUrl);
        Assert.Equal("Testing", events[0].TargetName);
    }

    [Fact]
    public async Task A_thread_is_one_event_at_its_head()
    {
        using var db = NewDb();
        var draft = AddDraft(db, "Long read");
        var channel = AddChannel(db, "testingandfun");
        for (var part = 0; part < 4; part++)
            db.ChannelPosts.Add(Send(draft, channel, 100 + part, Noon.AddSeconds(part * 3)));
        await db.SaveChangesAsync();

        var only = Assert.Single(await PublishedPosts.EventsAsync(db, A));

        Assert.Equal(4, only.PartCount);
        Assert.Equal(Noon, only.PublishedAt);
        Assert.Equal("https://t.me/testingandfun/100", only.PublicUrl);
    }

    [Fact]
    public async Task A_channel_without_a_username_has_a_time_and_no_link()
    {
        using var db = NewDb();
        var draft = AddDraft(db, "Private channel post");
        db.ChannelPosts.Add(Send(draft, AddChannel(db, null), 5, Noon));
        await db.SaveChangesAsync();

        var only = Assert.Single(await PublishedPosts.EventsAsync(db, A));

        Assert.Null(only.PublicUrl);
        Assert.Equal(Noon, only.PublishedAt);
    }

    [Fact]
    public async Task Other_networks_come_from_succeeded_jobs_and_the_blog_from_its_publish_date()
    {
        using var db = NewDb();
        var draft = AddDraft(db, "Everywhere");
        draft.IsBlogPublished = true;
        draft.BlogPublishedAt = Noon.AddHours(-2);
        var withdrawn = AddDraft(db, "Withdrawn");
        withdrawn.BlogPublishedAt = Noon.AddDays(-9);
        var target = new PublishTarget { OwnerId = A, Network = PublishNetworks.Bluesky, DisplayName = "@marty", RemoteId = "did" };
        db.PublishTargets.Add(target);
        db.PublishJobs.AddRange(
            Job(draft, PublishNetworks.Bluesky, target.Id, 0, PublishJobStatus.Succeeded, "https://bsky.app/p/1"),
            Job(draft, PublishNetworks.Bluesky, target.Id, 1, PublishJobStatus.Succeeded, "https://bsky.app/p/2"),
            Job(draft, PublishNetworks.X, Guid.NewGuid(), 0, PublishJobStatus.Failed, null),
            // The send a Telegram job made is a ChannelPost already.
            Job(draft, PublishNetworks.Telegram, Guid.NewGuid(), 0, PublishJobStatus.Succeeded, "https://t.me/c/1"));
        await db.SaveChangesAsync();

        var events = await PublishedPosts.EventsAsync(db, A);

        Assert.Equal([PublishNetworks.Bluesky, PublishedPosts.BlogNetwork], events.Select(e => e.Network));
        Assert.Equal("@marty", events[0].TargetName);
        Assert.Equal("https://bsky.app/p/1", events[0].PublicUrl);
        Assert.Equal(Noon.AddHours(-2), events[1].PublishedAt);
    }

    [Fact]
    public async Task A_send_made_by_a_schedule_is_marked_so_the_calendar_draws_it_once()
    {
        using var db = NewDb();
        var draft = AddDraft(db, "Planned");
        var channel = AddChannel(db, "testingandfun");
        db.ChannelPosts.AddRange(Send(draft, channel, 7, Noon), Send(draft, channel, 9, Noon.AddDays(1)));
        db.ScheduledPosts.Add(new ScheduledPost
        {
            OwnerId = A, DraftId = draft.Id, ChatId = "-100", ScheduledAtUtc = Noon, Status = "Sent", MessageId = 7,
        });
        await db.SaveChangesAsync();

        var events = await PublishedPosts.EventsAsync(db, A);

        Assert.True(events.Single(e => e.PublishedAt == Noon).Scheduled);
        Assert.False(events.Single(e => e.PublishedAt == Noon.AddDays(1)).Scheduled);
    }

    [Fact]
    public async Task A_project_narrows_the_events_to_its_documents_and_another_owner_is_never_read()
    {
        using var db = NewDb();
        var project = AddProject(db);
        var inside = AddDraft(db, "Inside", project: project);
        var outside = AddDraft(db, "Outside", project: AddProject(db));
        var foreign = AddDraft(db, "Foreign", owner: B, project: AddProject(db, B));
        foreach (var draft in new[] { inside, outside, foreign })
        {
            draft.IsBlogPublished = true;
            draft.BlogPublishedAt = Noon;
        }
        var channel = AddChannel(db, "testingandfun");
        db.ChannelPosts.AddRange(Send(inside, channel, 1, Noon), Send(outside, channel, 2, Noon));
        db.ChannelPosts.Add(Send(foreign, AddChannel(db, "stranger", B), 3, Noon));
        await db.SaveChangesAsync();

        var scoped = await PublishedPosts.EventsAsync(db, A, project);
        var all = await PublishedPosts.EventsAsync(db, A);

        Assert.Equal(2, scoped.Count);
        Assert.All(scoped, e => Assert.Equal(inside.Id, e.DraftId));
        Assert.Equal(4, all.Count);
        Assert.DoesNotContain(all, e => e.DraftId == foreign.Id);
    }

    [Fact]
    public async Task The_publishing_streak_counts_one_projects_documents_when_asked()
    {
        using var db = NewDb();
        var project = AddProject(db);
        var inside = AddDraft(db, "Inside", project: project);
        var outside = AddDraft(db, "Outside");
        inside.BlogPublishedAt = Noon;
        outside.BlogPublishedAt = Noon.AddDays(-1);
        var channel = AddChannel(db, "testingandfun");
        db.ChannelPosts.AddRange(Send(inside, channel, 1, Noon.AddHours(1)), Send(outside, channel, 2, Noon.AddHours(2)));
        db.PublishJobs.AddRange(
            Job(inside, PublishNetworks.X, Guid.NewGuid(), 0, PublishJobStatus.Succeeded, "https://x.com/a/1"),
            Job(outside, PublishNetworks.X, Guid.NewGuid(), 0, PublishJobStatus.Succeeded, "https://x.com/a/2"));
        await db.SaveChangesAsync();

        Assert.Equal(3, (await StatsInsightsEndpoints.PublishDatesAsync(db, A, project)).Count);
        Assert.Equal(6, (await StatsInsightsEndpoints.PublishDatesAsync(db, A, null)).Count);
    }

    private static PublishJob Job(Draft draft, string network, Guid targetId, int part, string status, string? url) => new()
    {
        OwnerId = draft.OwnerId, DraftId = draft.Id, TargetId = targetId, Network = network,
        Status = status, PartIndex = part, PartCount = 2, PublicUrl = url, FinishedAt = Noon,
    };
}
