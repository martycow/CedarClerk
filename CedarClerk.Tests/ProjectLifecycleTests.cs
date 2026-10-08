using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// ADR-318 / ADR-319 — a project may be empty, and Personal goes like any other project.
public class ProjectLifecycleTests : IDisposable
{
    private const string Owner = "u1";
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly CedarDbContext db;

    public ProjectLifecycleTests()
    {
        connection.Open();
        db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = Owner, Email = "u1@example.test" });
        db.SaveChanges();
    }

    public void Dispose() { db.Dispose(); connection.Dispose(); }

    private static ProjectEndpoints.CreateProjectRequest Request(string type, bool empty = false, string? cover = null, string? banner = null) =>
        new("Field Notes", "About", type, null, "First", StartWithoutDocuments: empty, CoverUrl: cover, BannerUrl: banner);

    [Fact]
    public async Task Start_without_documents_creates_the_project_alone()
    {
        var created = await ProjectEndpoints.CreateAsync(db, Owner, Request(ProjectTypes.Blog, empty: true));

        Assert.Null(created.Error);
        Assert.Null(created.Document);
        Assert.Equal(0, await db.Drafts.CountAsync());
        Assert.True(await db.ProjectModules.AnyAsync(m => m.ProjectId == created.Project!.Id));
    }

    [Fact]
    public async Task The_empty_type_creates_no_document()
    {
        var created = await ProjectEndpoints.CreateAsync(db, Owner, Request(ProjectTypes.Empty));

        Assert.Null(created.Document);
        Assert.Equal(0, await db.Drafts.CountAsync());
    }

    [Fact]
    public async Task A_saved_preset_built_on_the_empty_type_keeps_its_starter()
    {
        var preset = new Preset { OwnerId = Owner, Kind = PresetKinds.Project, Name = "Notebook", ConfigJson = """{"projectType":"empty","documentTitle":"Inbox"}""" };
        db.Presets.Add(preset);
        await db.SaveChangesAsync();

        var created = await ProjectEndpoints.CreateAsync(db, Owner, new("Notes", null, null, null, null, PresetId: preset.Id));

        Assert.Equal("Inbox", created.Document!.Title);
        Assert.Equal(DocumentTypes.Note, created.Document.DocumentType);
    }

    [Fact]
    public async Task Any_other_type_still_starts_with_its_document()
    {
        var created = await ProjectEndpoints.CreateAsync(db, Owner, Request(ProjectTypes.Blog));

        Assert.Equal("First", created.Document!.Title);
        Assert.Equal(created.Project!.Id, created.Document.ProjectId);
        Assert.Equal(DocumentTypes.Post, created.Document.DocumentType);
    }

    [Fact]
    public async Task Logo_and_banner_are_stored_only_as_library_paths()
    {
        var kept = await ProjectEndpoints.CreateAsync(db, Owner, Request(ProjectTypes.Empty, cover: "/media/logo.png", banner: "/media/banner-1.jpg"));
        var dropped = await ProjectEndpoints.CreateAsync(db, Owner, Request(ProjectTypes.Empty, banner: "https://example.test/x.png"));

        Assert.Equal("/media/logo.png", kept.Project!.CoverUrl);
        Assert.Equal("/media/banner-1.jpg", kept.Project.BannerUrl);
        Assert.Null(dropped.Project!.BannerUrl);
        Assert.Null(ProjectEndpoints.MediaPathOrNull("/media/../secret"));
        Assert.Null(ProjectEndpoints.MediaPathOrNull(null));
    }

    [Fact]
    public async Task Deleting_another_project_moves_its_documents_and_assets_into_personal()
    {
        var project = new Project { OwnerId = Owner, Name = "Game" };
        var draft = new Draft { OwnerId = Owner, ProjectId = project.Id, Title = "Design" };
        var asset = new Asset { OwnerId = Owner, ProjectId = project.Id, FileName = "a.png", LocalPath = "a.png" };
        db.Projects.Add(project);
        db.Drafts.Add(draft);
        db.Assets.Add(asset);
        db.GameTasks.Add(new GameTask { OwnerId = Owner, ProjectId = project.Id, Title = "Task" });
        await db.SaveChangesAsync();

        var files = await ProjectDeletion.DeleteAsync(db, Owner, project.Id);

        var personalId = DocumentProjects.PersonalId(Owner);
        Assert.Empty(files!);
        Assert.Equal(personalId, (await db.Drafts.AsNoTracking().SingleAsync()).ProjectId);
        Assert.Equal(personalId, (await db.Assets.AsNoTracking().SingleAsync()).ProjectId);
        Assert.Equal(personalId, (await db.Projects.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(0, await db.GameTasks.CountAsync());
    }

    [Fact]
    public async Task Deleting_an_empty_project_does_not_bring_personal_back()
    {
        var project = new Project { OwnerId = Owner, Name = "Empty" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        Assert.NotNull(await ProjectDeletion.DeleteAsync(db, Owner, project.Id));

        Assert.Equal(0, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task Deleting_personal_deletes_its_documents_and_filed_assets()
    {
        var personalId = await DocumentProjects.PersonalAsync(db, Owner);
        var other = new Project { OwnerId = Owner, Name = "Game", CoverUrl = "/media/gone.png", BannerUrl = "/media/gone.png" };
        var doomed = new Draft { OwnerId = Owner, ProjectId = personalId, Title = "Mine" };
        var child = new Draft { OwnerId = Owner, ProjectId = other.Id, Title = "Child", ParentDraftId = doomed.Id };
        var survivor = new Draft
        {
            OwnerId = Owner, ProjectId = other.Id, Title = "Uses a picture",
            CedarJson = """{"type":"doc","content":[{"type":"image","attrs":{"src":"/media/kept.png"}}]}""",
        };
        db.Projects.Add(other);
        db.Drafts.AddRange(doomed, child, survivor);
        db.DraftRevisions.Add(new DraftRevision { OwnerId = Owner, DraftId = doomed.Id, Title = "Mine", CedarJson = "{}" });
        db.Assets.AddRange(
            new Asset { OwnerId = Owner, ProjectId = personalId, FileName = "gone.png", LocalPath = "gone.png", TelegramLocalPath = "gone.tg.jpg" },
            new Asset { OwnerId = Owner, ProjectId = personalId, FileName = "kept.png", LocalPath = "kept.png" },
            new Asset { OwnerId = Owner, ProjectId = other.Id, FileName = "other.png", LocalPath = "other.png" });
        db.GlossaryTerms.Add(new GlossaryTerm { OwnerId = Owner, ProjectId = personalId, Term = "Cedar" });
        await db.SaveChangesAsync();
        Assert.Equal(new ProjectDeletion.Counts(1, 2), await ProjectDeletion.CountsAsync(db, Owner, personalId));

        var files = await ProjectDeletion.DeleteAsync(db, Owner, personalId);
        db.ChangeTracker.Clear();

        Assert.Equal(["gone.png", "gone.tg.jpg"], files!);
        Assert.Equal(other.Id, (await db.Projects.SingleAsync()).Id);
        Assert.Equal(["Child", "Uses a picture"], await db.Drafts.OrderBy(d => d.Title).Select(d => d.Title).ToListAsync());
        Assert.Null((await db.Drafts.SingleAsync(d => d.Title == "Child")).ParentDraftId);
        Assert.Equal(0, await db.DraftRevisions.CountAsync());
        var assets = await db.Assets.OrderBy(a => a.LocalPath).ToListAsync();
        Assert.Equal(["kept.png", "other.png"], assets.Select(a => a.LocalPath));
        Assert.Null(assets[0].ProjectId);
        Assert.Null((await db.Projects.SingleAsync()).CoverUrl);
        Assert.Null((await db.Projects.SingleAsync()).BannerUrl);
        Assert.Null((await db.GlossaryTerms.SingleAsync()).ProjectId);
    }

    [Fact]
    public async Task Someone_elses_project_is_not_found()
    {
        db.Users.Add(new ApplicationUser { Id = "u2", UserName = "u2", Email = "u2@example.test" });
        var theirs = new Project { OwnerId = "u2", Name = "Theirs" };
        db.Projects.Add(theirs);
        await db.SaveChangesAsync();

        Assert.Null(await ProjectDeletion.DeleteAsync(db, Owner, theirs.Id));
        Assert.Equal(1, await db.Projects.CountAsync());
    }

    [Fact]
    public async Task Analytics_summarises_the_last_week()
    {
        var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var project = new Project { OwnerId = Owner, Name = "Blog" };
        var post = new Draft { OwnerId = Owner, ProjectId = project.Id, Title = "Post", ViewCount = 120 };
        var fresh = new Draft { OwnerId = Owner, ProjectId = project.Id, Title = "Fresh", ViewCount = 5 };
        var elsewhere = new Draft { OwnerId = Owner, Title = "Elsewhere", ViewCount = 900 };
        var channel = new Channel { OwnerId = Owner, Title = "Channel", TelegramChatId = 1 };
        db.Projects.Add(project);
        db.Drafts.AddRange(post, fresh, elsewhere);
        db.Channels.Add(channel);
        db.DraftStatSnapshots.AddRange(
            new DraftStatSnapshot { OwnerId = Owner, DraftId = post.Id, ViewCount = 40, TakenAt = now.AddDays(-20) },
            new DraftStatSnapshot { OwnerId = Owner, DraftId = post.Id, ViewCount = 100, TakenAt = now.AddDays(-8) },
            new DraftStatSnapshot { OwnerId = Owner, DraftId = post.Id, ViewCount = 110, TakenAt = now.AddDays(-1) },
            new DraftStatSnapshot { OwnerId = Owner, DraftId = elsewhere.Id, ViewCount = 1, TakenAt = now.AddDays(-8) });
        db.Reactions.AddRange(
            new Reaction { OwnerId = Owner, DraftId = post.Id, Kind = "like", VisitorHash = "a", CreatedAt = now.AddDays(-30) },
            new Reaction { OwnerId = Owner, DraftId = post.Id, Kind = "like", VisitorHash = "b", CreatedAt = now.AddDays(-2) },
            new Reaction { OwnerId = Owner, DraftId = post.Id, Kind = "dislike", VisitorHash = "c", CreatedAt = now.AddDays(-2) },
            new Reaction { OwnerId = Owner, DraftId = elsewhere.Id, Kind = "like", VisitorHash = "d", CreatedAt = now.AddDays(-2) });
        db.ChannelPosts.Add(new ChannelPost { OwnerId = Owner, ChannelId = channel.Id, DraftId = post.Id, TelegramMessageId = 7, ReactionCount = 9 });
        db.ChannelStatSnapshots.AddRange(
            new ChannelStatSnapshot { OwnerId = Owner, ChannelId = channel.Id, MemberCount = 300, TakenAt = now.AddDays(-9) },
            new ChannelStatSnapshot { OwnerId = Owner, ChannelId = channel.Id, MemberCount = 312, TakenAt = now.AddDays(-1) });
        db.ChannelMemberDailies.AddRange(
            new ChannelMemberDaily { OwnerId = Owner, ChannelId = channel.Id, Day = now.Date.AddDays(-2), Joins = 15, Leaves = 3 },
            new ChannelMemberDaily { OwnerId = Owner, ChannelId = channel.Id, Day = now.Date.AddDays(-20), Joins = 50 });
        db.BlogSubscribers.AddRange(
            new BlogSubscriber { OwnerId = Owner, Email = "a@example.test", UnsubscribeToken = "t1", ConfirmedAt = now.AddDays(-40) },
            new BlogSubscriber { OwnerId = Owner, Email = "b@example.test", UnsubscribeToken = "t2", ConfirmedAt = now.AddDays(-3) },
            new BlogSubscriber { OwnerId = Owner, Email = "c@example.test", UnsubscribeToken = "t3" });
        db.BlogStatSnapshots.AddRange(
            new BlogStatSnapshot { OwnerId = Owner, ViewCount = 1000, TakenAt = now.AddDays(-8) },
            new BlogStatSnapshot { OwnerId = Owner, ViewCount = 1060, TakenAt = now.AddDays(-1) });
        await db.SaveChangesAsync();

        var summary = await ProjectAnalyticsSummary.ForAsync(db, Owner, project.Id, now);

        Assert.Equal(new ProjectAnalyticsSummary.Summary(
            Days: 7,
            Views: 125, ViewsGrowth: 25,
            Likes: 2, LikesGrowth: 1,
            TelegramReactions: 9,
            TelegramMembers: 312, TelegramMembersGrowth: 12,
            BlogSubscribers: 2, BlogSubscribersGrowth: 1,
            BlogViews: 1060, BlogViewsGrowth: 60), summary);
    }

    [Fact]
    public async Task Analytics_of_an_empty_project_is_zeroes_and_unknowns()
    {
        var project = new Project { OwnerId = Owner, Name = "Empty" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var summary = await ProjectAnalyticsSummary.ForAsync(db, Owner, project.Id, DateTime.UtcNow);

        Assert.Equal(new ProjectAnalyticsSummary.Summary(7, 0, 0, 0, 0, 0, null, 0, 0, 0, null, null), summary);
    }
}
