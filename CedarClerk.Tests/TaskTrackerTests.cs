using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// T-123 / ADR-106 — the task tracker. What is pinned here is the part that is easy to get subtly
// wrong and impossible to notice: the order tasks come back in, and the link rows that outlive the
// thing they point at.
public class TaskTrackerTests
{
    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts, TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>
    /// Draft.OwnerId is a real foreign key (GameTask.OwnerId deliberately is not — see
    /// Entities.IndieDev.cs), so a test that stores a document has to store its owner first.
    /// </summary>
    private static async Task<string> NewUserAsync(CedarDbContext db, string id)
    {
        db.Users.Add(new ApplicationUser { Id = id, UserName = $"{id}@local.test", Email = $"{id}@local.test" });
        await db.SaveChangesAsync();
        return id;
    }

    private static GameTask Task(
        Guid projectId, string title, string status = TaskStatuses.Backlog,
        int priority = TaskPriorities.Normal, DateTime? due = null) => new()
        {
            OwnerId = "u1",
            ProjectId = projectId,
            Title = title,
            Status = status,
            Priority = priority,
            DueAt = due,
        };

    // ---- statuses and priorities -------------------------------------------------------------

    [Fact]
    public void Every_status_the_board_draws_is_a_known_one()
    {
        Assert.Equal(4, TaskStatuses.All.Count);
        Assert.All(TaskStatuses.All, s => Assert.True(TaskStatuses.IsKnown(s)));
        Assert.False(TaskStatuses.IsKnown("doing"));
        Assert.False(TaskStatuses.IsKnown(null));
    }

    [Fact]
    public void Open_means_everything_that_is_not_done()
    {
        // The project list's "Tasks" column counts these, so what it means is worth stating twice.
        Assert.True(TaskStatuses.IsOpen(TaskStatuses.Backlog));
        Assert.True(TaskStatuses.IsOpen(TaskStatuses.Planned));
        Assert.True(TaskStatuses.IsOpen(TaskStatuses.InProgress));
        Assert.False(TaskStatuses.IsOpen(TaskStatuses.Done));
    }

    [Fact]
    public void Status_order_is_the_boards_left_to_right_order()
    {
        Assert.True(TaskStatuses.Order(TaskStatuses.Backlog) < TaskStatuses.Order(TaskStatuses.Planned));
        Assert.True(TaskStatuses.Order(TaskStatuses.Planned) < TaskStatuses.Order(TaskStatuses.InProgress));
        Assert.True(TaskStatuses.Order(TaskStatuses.InProgress) < TaskStatuses.Order(TaskStatuses.Done));
    }

    [Fact]
    public void An_unknown_status_sorts_last_instead_of_throwing()
    {
        // A row written by a future version must not crash the board of an older one.
        Assert.Equal(TaskStatuses.All.Count, TaskStatuses.Order("whatever"));
        Assert.Equal(TaskStatuses.All.Count, TaskStatuses.Order(null));
    }

    [Fact]
    public void Priority_outside_one_to_three_falls_back_to_normal()
    {
        Assert.True(TaskPriorities.IsKnown(1));
        Assert.True(TaskPriorities.IsKnown(3));
        Assert.False(TaskPriorities.IsKnown(0));
        Assert.False(TaskPriorities.IsKnown(4));
        Assert.Equal(TaskPriorities.Normal, TaskPriorities.Clamp(0));
        Assert.Equal(TaskPriorities.Normal, TaskPriorities.Clamp(99));
        Assert.Equal(TaskPriorities.Highest, TaskPriorities.Clamp(1));
    }

    // ---- the "up next" rail ------------------------------------------------------------------

    [Fact]
    public async Task Up_next_puts_the_overdue_before_the_merely_urgent()
    {
        // The whole reason the rail is not sorted by priority: a P3 that was due last week needs
        // answering before a P1 due next month.
        using var db = NewDb();
        var p = Guid.NewGuid();
        db.GameTasks.AddRange(
            Task(p, "P1 due next month", priority: 1, due: DateTime.UtcNow.AddDays(30)),
            Task(p, "P3 overdue", priority: 3, due: DateTime.UtcNow.AddDays(-7)));
        await db.SaveChangesAsync();

        var next = await TaskEndpoints.UpNextAsync(db, "u1", p);
        Assert.Equal("P3 overdue", next[0].Title);
    }

    [Fact]
    public async Task Up_next_puts_dated_tasks_before_undated_ones()
    {
        using var db = NewDb();
        var p = Guid.NewGuid();
        db.GameTasks.AddRange(
            Task(p, "no deadline", priority: 1),
            Task(p, "due friday", priority: 3, due: DateTime.UtcNow.AddDays(3)));
        await db.SaveChangesAsync();

        var next = await TaskEndpoints.UpNextAsync(db, "u1", p);
        Assert.Equal("due friday", next[0].Title);
    }

    [Fact]
    public async Task Up_next_ignores_done_archived_and_other_peoples_tasks()
    {
        using var db = NewDb();
        var p = Guid.NewGuid();
        var mine = Task(p, "mine");
        var done = Task(p, "done", status: TaskStatuses.Done);
        var archived = Task(p, "archived");
        archived.ArchivedAt = DateTime.UtcNow;
        var theirs = Task(p, "theirs");
        theirs.OwnerId = "u2";

        db.GameTasks.AddRange(mine, done, archived, theirs);
        await db.SaveChangesAsync();

        var next = await TaskEndpoints.UpNextAsync(db, "u1", p);
        Assert.Equal(["mine"], next.Select(t => t.Title));
    }

    [Fact]
    public async Task Up_next_stops_at_five()
    {
        using var db = NewDb();
        var p = Guid.NewGuid();
        for (var i = 0; i < 9; i++) db.GameTasks.Add(Task(p, $"task {i}"));
        await db.SaveChangesAsync();

        Assert.Equal(TaskEndpoints.UpNextCount, (await TaskEndpoints.UpNextAsync(db, "u1", p)).Count);
    }

    // ---- links ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_link_is_found_from_either_end()
    {
        using var db = NewDb();
        var project = Guid.NewGuid();
        var task = Guid.NewGuid();
        var document = Guid.NewGuid();

        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Task, task, LinkTargets.Document, document);

        Assert.Equal([document], await ProjectLinks.LinkedIdsAsync(db, "u1", LinkTargets.Task, task, LinkTargets.Document));
        Assert.Equal([task], await ProjectLinks.LinkedIdsAsync(db, "u1", LinkTargets.Document, document, LinkTargets.Task));
    }

    [Fact]
    public async Task Linking_the_same_pair_from_the_other_end_does_not_make_a_second_row()
    {
        // LinkTargets.Order is what makes this true; without it the unique index cannot tell the
        // two directions apart and one fact becomes two rows.
        using var db = NewDb();
        var project = Guid.NewGuid();
        var task = Guid.NewGuid();
        var document = Guid.NewGuid();

        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Task, task, LinkTargets.Document, document);
        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Document, document, LinkTargets.Task, task);

        Assert.Equal(1, await db.EntityLinks.CountAsync());
    }

    [Fact]
    public async Task A_task_cannot_be_linked_to_itself()
    {
        using var db = NewDb();
        var task = Guid.NewGuid();
        Assert.False(await ProjectLinks.AddAsync(db, "u1", Guid.NewGuid(), LinkTargets.Task, task, LinkTargets.Task, task));
        Assert.Equal(0, await db.EntityLinks.CountAsync());
    }

    [Fact]
    public async Task Two_tasks_linked_to_each_other_both_show_the_link()
    {
        // The case the batch reader gets wrong if it only looks at one column: both sides of this
        // row are tasks, so one row has to appear on two cards.
        using var db = NewDb();
        var project = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Task, a, LinkTargets.Task, b);

        var links = await ProjectLinks.LinkedIdsForManyAsync(db, "u1", LinkTargets.Task, [a, b]);
        Assert.Equal([(LinkTargets.Task, b)], links[a]);
        Assert.Equal([(LinkTargets.Task, a)], links[b]);
    }

    [Fact]
    public async Task Links_of_many_tasks_come_back_keyed_by_task()
    {
        using var db = NewDb();
        var project = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var asset = Guid.NewGuid();

        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Task, t1, LinkTargets.Document, doc);
        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Task, t1, LinkTargets.Asset, asset);

        var links = await ProjectLinks.LinkedIdsForManyAsync(db, "u1", LinkTargets.Task, [t1, t2]);
        Assert.Equal(2, links[t1].Count);
        Assert.Empty(links[t2]);
    }

    [Fact]
    public async Task Removing_a_things_links_takes_both_directions()
    {
        using var db = NewDb();
        var project = Guid.NewGuid();
        var task = Guid.NewGuid();

        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Task, task, LinkTargets.Document, Guid.NewGuid());
        await ProjectLinks.AddAsync(db, "u1", project, LinkTargets.Asset, Guid.NewGuid(), LinkTargets.Task, task);

        Assert.Equal(2, await ProjectLinks.RemoveAllForAsync(db, "u1", LinkTargets.Task, task));
        Assert.Equal(0, await db.EntityLinks.CountAsync());
    }

    [Fact]
    public async Task One_persons_links_are_never_another_persons()
    {
        using var db = NewDb();
        var task = Guid.NewGuid();
        var doc = Guid.NewGuid();
        await ProjectLinks.AddAsync(db, "u1", Guid.NewGuid(), LinkTargets.Task, task, LinkTargets.Document, doc);

        Assert.Empty(await ProjectLinks.LinkedIdsAsync(db, "u2", LinkTargets.Task, task, LinkTargets.Document));
        Assert.False(await ProjectLinks.RemoveAsync(db, "u2", LinkTargets.Task, task, LinkTargets.Document, doc));
    }

    [Fact]
    public async Task Link_labels_come_from_the_thing_each_link_points_at()
    {
        using var db = NewDb();
        await NewUserAsync(db, "u1");
        var project = Guid.NewGuid();
        var draft = new Draft { OwnerId = "u1", Title = "Ferry terminal layout", ProjectId = project };
        var asset = new AssetEntry { OwnerId = "u1", ProjectId = project, FileName = "opossum_idle.png" };
        var attachment = new Asset { OwnerId = "u1", FileName = "capture-notes.pdf", LocalPath = "asset_notes.pdf" };
        var other = Task(project, "Bake lightmaps");
        db.Drafts.Add(draft);
        db.AssetEntries.Add(asset);
        db.Assets.Add(attachment);
        db.GameTasks.Add(other);
        await db.SaveChangesAsync();

        var labels = await TaskEndpoints.ResolveLabelsAsync(db, "u1", [
            (LinkTargets.Document, draft.Id),
            (LinkTargets.Asset, asset.Id),
            (LinkTargets.Attachment, attachment.Id),
            (LinkTargets.Task, other.Id),
        ]);

        Assert.Equal("Ferry terminal layout", labels[(LinkTargets.Document, draft.Id)]);
        Assert.Equal("opossum_idle.png", labels[(LinkTargets.Asset, asset.Id)]);
        Assert.Equal("capture-notes.pdf", labels[(LinkTargets.Attachment, attachment.Id)]);
        Assert.Equal("Bake lightmaps", labels[(LinkTargets.Task, other.Id)]);
    }

    [Fact]
    public async Task A_label_for_something_gone_is_simply_absent()
    {
        // Absent, not blank-with-a-row: the endpoint renders a missing label as an empty string,
        // and the point of this test is that resolving does not throw on a dangling id.
        using var db = NewDb();
        var labels = await TaskEndpoints.ResolveLabelsAsync(db, "u1", [(LinkTargets.Document, Guid.NewGuid())]);
        Assert.Empty(labels);
    }

    [Fact]
    public async Task A_label_of_someone_elses_document_is_not_resolved()
    {
        using var db = NewDb();
        await NewUserAsync(db, "u2");
        var draft = new Draft { OwnerId = "u2", Title = "Their secret" };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();

        Assert.Empty(await TaskEndpoints.ResolveLabelsAsync(db, "u1", [(LinkTargets.Document, draft.Id)]));
    }
}
