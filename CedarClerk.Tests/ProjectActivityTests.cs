using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.Extensions.Configuration;
using static CedarClerk.Server.Modules.IndieDev.ProjectEndpoints;

namespace CedarClerk.Tests;

// T-249. The hub's journal is a union of timestamps the module already writes; what is worth
// testing is the shape — every kind can appear, newest wins, a thread is one send, and nothing
// from another tenant leaks in even when a row is filed under this project's id.
public class ProjectActivityTests
{
    private static readonly IConfiguration Cfg = new ConfigurationBuilder().Build();
    private static readonly DateTime T0 = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Task<List<ActivityItem>> Activity(CedarDbContext db, string owner, Guid project, int take = 20) =>
        ActivityAsync(db, Cfg, owner, project, take);

    [Fact]
    public async Task Every_kind_appears_with_its_link_and_subtitle()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        Guid draftId;
        await using (var seed = fx.Platform())
        {
            seed.Users.Single(u => u.Id == owner).TenantUsername = "owner";
            var draft = new Draft
            {
                OwnerId = owner, ProjectId = project, Title = "Devlog 1", DocumentType = DocumentTypes.Post,
                CreatedAt = T0, UpdatedAt = T0.AddHours(2), BlogSlug = "devlog-1", IsBlogPublished = true,
                BlogPublishedAt = T0.AddHours(3),
            };
            var channel = new Channel { OwnerId = owner, Title = "My Channel", Username = "mychan" };
            draftId = draft.Id;
            seed.Drafts.Add(draft);
            seed.Channels.Add(channel);
            seed.GameTasks.Add(new GameTask
            {
                OwnerId = owner, ProjectId = project, Title = "Fix ferry", Status = TaskStatuses.Done, Assignee = "composer",
                CreatedAt = T0.AddHours(4), CompletedAt = T0.AddHours(5),
            });
            seed.Builds.Add(new Build { OwnerId = owner, ProjectId = project, Version = "0.4.2", CreatedAt = T0.AddHours(6), ReleasedAt = T0.AddHours(7) });
            seed.ChannelPosts.Add(new ChannelPost { OwnerId = owner, ChannelId = channel.Id, DraftId = draft.Id, TelegramMessageId = 77, PublishedAt = T0.AddHours(8) });
            seed.PublishJobs.Add(new PublishJob
            {
                OwnerId = owner, DraftId = draft.Id, Network = PublishNetworks.Bluesky, Status = PublishJobStatus.Succeeded,
                PublicUrl = "https://bsky.app/profile/x/post/1", FinishedAt = T0.AddHours(9),
            });
            seed.PublishJobs.Add(new PublishJob
            {
                OwnerId = owner, DraftId = draft.Id, Network = PublishNetworks.X, Status = PublishJobStatus.Failed,
                Error = "401 Unauthorized", FinishedAt = T0.AddHours(10),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var items = await Activity(db, owner, project);

        Assert.Equal(ActivityKinds.All.OrderBy(k => k), items.Select(i => i.Kind).Distinct().OrderBy(k => k));
        Assert.Equal(ActivityKinds.All.Count, items.Count);

        var byKind = items.ToDictionary(i => i.Kind);
        Assert.Equal($"/editor?draft={draftId}", byKind[ActivityKinds.DocumentCreated].Href);
        Assert.Equal(DocumentTypes.Post, byKind[ActivityKinds.DocumentUpdated].Subtitle);
        Assert.Equal("blog", byKind[ActivityKinds.BlogPublished].Subtitle);
        Assert.EndsWith("/devlog-1", byKind[ActivityKinds.BlogPublished].Href);
        Assert.StartsWith("https://owner.", byKind[ActivityKinds.BlogPublished].Href);
        Assert.Equal("composer", byKind[ActivityKinds.TaskCreated].Actor);
        Assert.Equal(TaskStatuses.Done, byKind[ActivityKinds.TaskCompleted].Subtitle);
        Assert.Equal($"/projects/{project}/tasks", byKind[ActivityKinds.TaskCompleted].Href);
        Assert.Equal("0.4.2", byKind[ActivityKinds.BuildReleased].Title);
        Assert.Equal($"/projects/{project}/builds", byKind[ActivityKinds.BuildCreated].Href);
        Assert.Null(byKind[ActivityKinds.BuildCreated].Subtitle);
        Assert.Equal("My Channel", byKind[ActivityKinds.TelegramPublished].Subtitle);
        Assert.Equal("https://t.me/mychan/77", byKind[ActivityKinds.TelegramPublished].Href);
        Assert.Equal(PublishNetworks.Bluesky, byKind[ActivityKinds.Published].Subtitle);
        Assert.Equal("https://bsky.app/profile/x/post/1", byKind[ActivityKinds.Published].Href);
        Assert.Equal("x: 401 Unauthorized", byKind[ActivityKinds.PublishFailed].Subtitle);
        Assert.Null(byKind[ActivityKinds.PublishFailed].Href);
    }

    [Fact]
    public async Task Newest_first_and_take_caps_the_union()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            for (var i = 0; i < 5; i++)
                seed.GameTasks.Add(new GameTask { OwnerId = owner, ProjectId = project, Title = $"Task {i}", CreatedAt = T0.AddDays(i) });
            seed.Builds.Add(new Build { OwnerId = owner, ProjectId = project, Version = "0.1", CreatedAt = T0.AddDays(2).AddHours(1) });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var items = await Activity(db, owner, project, take: 3);

        Assert.Equal(3, items.Count);
        Assert.Equal(["Task 4", "Task 3", "0.1"], items.Select(i => i.Title));
        Assert.True(items.Zip(items.Skip(1)).All(pair => pair.First.At >= pair.Second.At));
    }

    [Fact]
    public async Task An_edit_within_a_minute_of_creation_is_not_an_update()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            seed.Drafts.Add(new Draft { OwnerId = owner, ProjectId = project, Title = "Fresh", CreatedAt = T0, UpdatedAt = T0.AddSeconds(30) });
            seed.Drafts.Add(new Draft { OwnerId = owner, ProjectId = project, Title = "Edited", CreatedAt = T0, UpdatedAt = T0.AddMinutes(5) });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var items = await Activity(db, owner, project);

        var updated = Assert.Single(items, i => i.Kind == ActivityKinds.DocumentUpdated);
        Assert.Equal("Edited", updated.Title);
        Assert.Equal(2, items.Count(i => i.Kind == ActivityKinds.DocumentCreated));
    }

    [Fact]
    public async Task Parts_of_one_thread_are_one_send()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            var draft = new Draft { OwnerId = owner, ProjectId = project, Title = "Thread", CreatedAt = T0, UpdatedAt = T0 };
            var channel = new Channel { OwnerId = owner, Title = "Chan", Username = "chan" };
            seed.Drafts.Add(draft);
            seed.Channels.Add(channel);
            for (var part = 0; part < 4; part++)
                seed.ChannelPosts.Add(new ChannelPost
                {
                    OwnerId = owner, ChannelId = channel.Id, DraftId = draft.Id,
                    TelegramMessageId = 100 + part, PublishedAt = T0.AddHours(1).AddSeconds(part * 20),
                });
            seed.ChannelPosts.Add(new ChannelPost { OwnerId = owner, ChannelId = channel.Id, DraftId = draft.Id, TelegramMessageId = 200, PublishedAt = T0.AddHours(3) });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var sends = (await Activity(db, owner, project)).Where(i => i.Kind == ActivityKinds.TelegramPublished).ToList();

        Assert.Equal(2, sends.Count);
        Assert.Equal("https://t.me/chan/200", sends[0].Href);
        Assert.Equal("https://t.me/chan/100", sends[1].Href);
    }

    [Fact]
    public async Task Another_tenants_rows_never_appear_even_when_filed_under_the_project()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            seed.Drafts.Add(new Draft { OwnerId = owner, ProjectId = project, Title = "Mine", CreatedAt = T0, UpdatedAt = T0 });
            seed.Drafts.Add(new Draft { OwnerId = stranger, ProjectId = project, Title = "Theirs", CreatedAt = T0.AddDays(1), UpdatedAt = T0.AddDays(1) });
            seed.GameTasks.Add(new GameTask { OwnerId = stranger, ProjectId = project, Title = "Their task", CreatedAt = T0.AddDays(1) });
            seed.Builds.Add(new Build { OwnerId = stranger, ProjectId = project, Version = "9.9", CreatedAt = T0.AddDays(1) });
            await seed.SaveChangesAsync();
        }

        // Read unfiltered on purpose: the explicit owner predicate has to do the work by itself.
        await using var db = fx.Platform();
        var items = await Activity(db, owner, project);

        var only = Assert.Single(items);
        Assert.Equal("Mine", only.Title);

        Assert.Null(await fx.TryAccessAsync(project, stranger));
    }

    [Fact]
    public async Task A_member_reads_the_journal_under_the_owners_id()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, mate);
        await using (var seed = fx.Platform())
        {
            seed.GameTasks.Add(new GameTask { OwnerId = owner, ProjectId = project, Title = "Shared task", CreatedAt = T0 });
            await seed.SaveChangesAsync();
        }

        var access = await fx.AccessAsync(project, mate);
        await using var db = fx.As(access.OwnerId);
        var items = await Activity(db, access.OwnerId, project);

        Assert.Equal("Shared task", Assert.Single(items).Title);
    }
}
