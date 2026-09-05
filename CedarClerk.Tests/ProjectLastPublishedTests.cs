using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-166. The hub's "days since the last post" reads one date off the project list; the date is the
// newest of three publish records, and a project with none of them has no date rather than a zero.
public class ProjectLastPublishedTests
{
    private static readonly DateTime T0 = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_newest_of_blog_telegram_and_queue_publishes_wins()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var blogOnly = fx.Project(owner, "Blog");
        var mixed = fx.Project(owner, "Mixed");
        await using (var seed = fx.Platform())
        {
            seed.Drafts.Add(new Draft { OwnerId = owner, ProjectId = blogOnly, Title = "B", BlogPublishedAt = T0 });

            var draft = new Draft { OwnerId = owner, ProjectId = mixed, Title = "M", BlogPublishedAt = T0.AddDays(1) };
            var channel = new Channel { OwnerId = owner, Title = "Chan" };
            seed.Drafts.Add(draft);
            seed.Channels.Add(channel);
            seed.ChannelPosts.Add(new ChannelPost { OwnerId = owner, ChannelId = channel.Id, DraftId = draft.Id, TelegramMessageId = 1, PublishedAt = T0.AddDays(3) });
            seed.PublishJobs.Add(new PublishJob { OwnerId = owner, DraftId = draft.Id, Network = PublishNetworks.Bluesky, Status = PublishJobStatus.Succeeded, FinishedAt = T0.AddDays(2) });
            // A failed job is not a publish, however recent.
            seed.PublishJobs.Add(new PublishJob { OwnerId = owner, DraftId = draft.Id, Network = PublishNetworks.X, Status = PublishJobStatus.Failed, FinishedAt = T0.AddDays(9) });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var last = await ProjectEndpoints.LastPublishedAsync(db, owner);

        Assert.Equal(T0, last[blogOnly]);
        Assert.Equal(T0.AddDays(3), last[mixed]);
    }

    [Fact]
    public async Task A_project_that_never_published_has_no_date()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            seed.Drafts.Add(new Draft { OwnerId = owner, ProjectId = project, Title = "Draft only" });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var last = await ProjectEndpoints.LastPublishedAsync(db, owner);

        Assert.False(last.ContainsKey(project));
    }

    [Fact]
    public async Task Another_owners_publishes_do_not_count()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var theirs = fx.Project(stranger, "Theirs");
        await using (var seed = fx.Platform())
        {
            seed.Drafts.Add(new Draft { OwnerId = stranger, ProjectId = theirs, Title = "T", BlogPublishedAt = T0 });
            await seed.SaveChangesAsync();
        }

        await using var db = fx.Platform();
        Assert.Empty(await ProjectEndpoints.LastPublishedAsync(db, owner));
    }
}
