using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// Indie-gamedev module, T-123 (ADR-106) — the task tracker. Registered from Program.cs behind the
// same Cedar:Modules:IndieDev flag as the rest of the module.
//
// Owner-scoped exactly like ProjectEndpoints: every query filters by the caller, and someone else's
// row answers 404 rather than 403, so the API never confirms that an id exists.
public static class TaskEndpoints
{
    // Nullable everywhere on update: the modal edits one field at a time, and a null means "leave
    // it alone". The two fields that can be *cleared* (due date, sprint) therefore need their own
    // flags — without them, "no due date" and "don't touch the due date" are the same request.
    public record CreateTaskRequest(
        string Title, string? Description, string? Status, int? Priority,
        string? Assignee, DateTime? DueAt, Guid? SprintId, Guid? BuildId);

    public record UpdateTaskRequest(
        string? Title, string? Description, string? Status, int? Priority,
        string? Assignee, DateTime? DueAt, bool? ClearDueAt, Guid? SprintId, bool? ClearSprint,
        Guid? BuildId, bool? ClearBuild, bool? Archived);

    public record LinkRequest(string Type, Guid Id);

    private const int TitleMaxLength = 200;
    private const int DescriptionMaxLength = 4000;
    private const int AssigneeMaxLength = 80;

    /// <summary>How many tasks the dashboard's "Up next" rail asks for — five rows in the design.</summary>
    public const int UpNextCount = 5;

    private static readonly Dictionary<Guid, List<(string Type, Guid Id)>> NoLinks = [];
    private static readonly Dictionary<(string Type, Guid Id), string> NoLabels = [];

    public static void MapTaskEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/tasks").RequireAuthorization();

        // The whole board in one request. Tasks are counted in tens per project, not thousands —
        // paging a kanban board would mean a column that lies about its own count, so the board
        // gets everything and filters on the client. The asset index is the screen that pages
        // (tens of thousands of rows); this one is deliberately not.
        group.MapGet("/", async (
            Guid projectId, ClaimsPrincipal user, CedarDbContext db, bool archived = false) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await OwnsProjectAsync(db, projectId, uid)) return Results.NotFound();

            var tasks = await db.GameTasks
                .Where(t => t.ProjectId == projectId && t.OwnerId == uid && (archived || t.ArchivedAt == null))
                .ToListAsync();

            var links = await ProjectLinks.LinkedIdsForManyAsync(
                db, uid, LinkTargets.Task, tasks.Select(t => t.Id).ToList());

            var labels = await ResolveLabelsAsync(db, uid, links.Values.SelectMany(v => v));

            return Results.Ok(Sorted(tasks).Select(t => Describe(t, links, labels)));
        });

        group.MapPost("/", async (
            Guid projectId, CreateTaskRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await OwnsProjectAsync(db, projectId, uid)) return Results.NotFound();

            var title = (req.Title ?? "").Trim();
            if (title.Length is 0 or > TitleMaxLength)
                return Results.BadRequest(new { error = ErrorMessages.TaskTitleLength(TitleMaxLength) });

            var status = req.Status ?? TaskStatuses.Backlog;
            if (!TaskStatuses.IsKnown(status))
                return Results.BadRequest(new { error = ErrorMessages.UnknownTaskStatus(status) });

            var priority = req.Priority ?? TaskPriorities.Normal;
            if (!TaskPriorities.IsKnown(priority))
                return Results.BadRequest(new { error = ErrorMessages.UnknownTaskPriority(priority) });

            var description = (req.Description ?? "").Trim();
            if (description.Length > DescriptionMaxLength)
                return Results.BadRequest(new { error = ErrorMessages.TaskDescriptionLength(DescriptionMaxLength) });

            var assignee = (req.Assignee ?? "").Trim();
            if (assignee.Length > AssigneeMaxLength)
                return Results.BadRequest(new { error = ErrorMessages.TaskAssigneeLength(AssigneeMaxLength) });

            Guid? sprintId = null;
            if (req.SprintId is { } wanted)
            {
                if (!await db.Sprints.AnyAsync(s => s.Id == wanted && s.ProjectId == projectId && s.OwnerId == uid))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownSprint });
                sprintId = wanted;
            }

            Guid? buildId = null;
            if (req.BuildId is { } wantedBuild)
            {
                if (!await db.Builds.AnyAsync(b => b.Id == wantedBuild && b.ProjectId == projectId && b.OwnerId == uid))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownBuild });
                buildId = wantedBuild;
            }

            var task = new GameTask
            {
                OwnerId = uid,
                ProjectId = projectId,
                Title = title,
                Description = description,
                Status = status,
                Priority = priority,
                Assignee = assignee,
                DueAt = req.DueAt,
                SprintId = sprintId,
                BuildId = buildId,
                // A task created straight into Done is finished now, not never — the board's Done
                // column is a legitimate place to write something down after doing it.
                CompletedAt = status == TaskStatuses.Done ? DateTime.UtcNow : null,
            };

            db.GameTasks.Add(task);
            await db.SaveChangesAsync();

            // A brand new task has no links yet, so no lookup is worth doing for it.
            return Results.Ok(Describe(task, NoLinks, NoLabels));
        });

        var single = app.MapGroup("/api/tasks/{id:guid}").RequireAuthorization();

        single.MapGet("/", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var task = await db.GameTasks.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid);
            if (task is null) return Results.NotFound();

            var links = await ProjectLinks.LinkedIdsForManyAsync(db, uid, LinkTargets.Task, [task.Id]);
            var labels = await ResolveLabelsAsync(db, uid, links.Values.SelectMany(v => v));

            return Results.Ok(Describe(task, links, labels));
        });

        single.MapPut("/", async (Guid id, UpdateTaskRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var task = await db.GameTasks.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid);
            if (task is null) return Results.NotFound();

            if (req.Title is not null)
            {
                var title = req.Title.Trim();
                if (title.Length is 0 or > TitleMaxLength)
                    return Results.BadRequest(new { error = ErrorMessages.TaskTitleLength(TitleMaxLength) });
                task.Title = title;
            }

            if (req.Description is not null)
            {
                var description = req.Description.Trim();
                if (description.Length > DescriptionMaxLength)
                    return Results.BadRequest(new { error = ErrorMessages.TaskDescriptionLength(DescriptionMaxLength) });
                task.Description = description;
            }

            if (req.Assignee is not null)
            {
                var assignee = req.Assignee.Trim();
                if (assignee.Length > AssigneeMaxLength)
                    return Results.BadRequest(new { error = ErrorMessages.TaskAssigneeLength(AssigneeMaxLength) });
                task.Assignee = assignee;
            }

            if (req.Priority is { } priority)
            {
                if (!TaskPriorities.IsKnown(priority))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownTaskPriority(priority) });
                task.Priority = priority;
            }

            if (req.Status is { } status)
            {
                if (!TaskStatuses.IsKnown(status))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownTaskStatus(status) });

                // Only a crossing of the Done line moves the completion date. Re-saving a finished
                // task, or dragging it from Done to Done, must not restamp the day it was finished.
                if (status == TaskStatuses.Done && task.Status != TaskStatuses.Done)
                    task.CompletedAt = DateTime.UtcNow;
                else if (status != TaskStatuses.Done)
                    task.CompletedAt = null;

                task.Status = status;
            }

            if (req.ClearDueAt == true) task.DueAt = null;
            else if (req.DueAt is { } due) task.DueAt = due;

            if (req.ClearSprint == true) task.SprintId = null;
            else if (req.SprintId is { } sprint)
            {
                // T-124 — a task may only join a sprint of its own project. Without this a sprint
                // id from another project would be accepted and the task would vanish from every
                // planner: its own would not list it, and the other one cannot see it.
                if (!await db.Sprints.AnyAsync(s => s.Id == sprint && s.ProjectId == task.ProjectId && s.OwnerId == uid))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownSprint });
                task.SprintId = sprint;
            }

            if (req.ClearBuild == true) task.BuildId = null;
            else if (req.BuildId is { } build)
            {
                // Same rule as the sprint: only a build of this task's own project.
                if (!await db.Builds.AnyAsync(b => b.Id == build && b.ProjectId == task.ProjectId && b.OwnerId == uid))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownBuild });
                task.BuildId = build;
            }

            if (req.Archived is { } archived)
                task.ArchivedAt = archived ? task.ArchivedAt ?? DateTime.UtcNow : null;

            task.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var links = await ProjectLinks.LinkedIdsForManyAsync(db, uid, LinkTargets.Task, [task.Id]);
            var labels = await ResolveLabelsAsync(db, uid, links.Values.SelectMany(v => v));
            return Results.Ok(Describe(task, links, labels));
        });

        single.MapDelete("/", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var task = await db.GameTasks.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid);
            if (task is null) return Results.NotFound();

            // The links go with it. A row pointing at a task that no longer exists would render as
            // a chip with no label on every document and asset it touched.
            await ProjectLinks.RemoveAllForAsync(db, uid, LinkTargets.Task, id);
            db.GameTasks.Remove(task);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        single.MapPost("/links", async (Guid id, LinkRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var task = await db.GameTasks.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid);
            if (task is null) return Results.NotFound();

            if (!LinkTargets.IsKnown(req.Type))
                return Results.BadRequest(new { error = ErrorMessages.UnknownLinkTarget(req.Type) });
            if (req.Type == LinkTargets.Task && req.Id == id)
                return Results.BadRequest(new { error = ErrorMessages.TaskCannotLinkToItself });

            // The target has to exist and be the caller's, or the chip would name nothing.
            if (!await TargetExistsAsync(db, uid, req.Type, req.Id)) return Results.NotFound();

            await ProjectLinks.AddAsync(db, uid, task.ProjectId, LinkTargets.Task, id, req.Type, req.Id);
            return Results.NoContent();
        });

        single.MapDelete("/links/{type}/{targetId:guid}", async (
            Guid id, string type, Guid targetId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!LinkTargets.IsKnown(type))
                return Results.BadRequest(new { error = ErrorMessages.UnknownLinkTarget(type) });

            var removed = await ProjectLinks.RemoveAsync(db, uid, LinkTargets.Task, id, type, targetId);
            return removed ? Results.NoContent() : Results.NotFound();
        });
    }

    /// <summary>
    /// The dashboard's "Up next" rail: the most urgent open tasks of a project. Overdue first, then
    /// by due date, and only then by priority — a P3 that was due last week needs answering before
    /// a P1 due next month, which is the whole reason a rail sorted by priority alone would be a
    /// worse rail.
    /// </summary>
    public static async Task<List<GameTask>> UpNextAsync(
        CedarDbContext db, string ownerId, Guid projectId, int count = UpNextCount)
    {
        var open = await db.GameTasks
            .Where(t => t.ProjectId == projectId && t.OwnerId == ownerId
                        && t.ArchivedAt == null && t.Status != TaskStatuses.Done)
            .ToListAsync();

        return open
            .OrderBy(t => t.DueAt is null)          // dated tasks before undated ones
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Board order within a column: priority first, then the nearest deadline, then oldest. Applied
    /// server-side so the list view, the board and the dashboard cannot disagree about what "next"
    /// means — three sorts in three components is three chances to drift.
    /// </summary>
    private static IEnumerable<GameTask> Sorted(IEnumerable<GameTask> tasks) => tasks
        .OrderBy(t => TaskStatuses.Order(t.Status))
        .ThenBy(t => t.Priority)
        .ThenBy(t => t.DueAt is null)
        .ThenBy(t => t.DueAt)
        .ThenBy(t => t.CreatedAt);

    public static object Describe(
        GameTask t,
        IReadOnlyDictionary<Guid, List<(string Type, Guid Id)>> links,
        IReadOnlyDictionary<(string Type, Guid Id), string> labels) => new
    {
        t.Id,
        t.ProjectId,
        t.Title,
        t.Status,
        t.Priority,
        t.Description,
        t.Assignee,
        t.SprintId,
        t.BuildId,
        t.DueAt,
        t.CreatedAt,
        t.UpdatedAt,
        t.CompletedAt,
        t.ArchivedAt,
        links = links.GetValueOrDefault(t.Id, [])
            .Select(l => new
            {
                type = l.Type,
                id = l.Id,
                // A link whose target has gone says so rather than rendering blank. Deleting a
                // document takes its links with it, so this is the disappearing case that route
                // does not cover — a row written before that rule existed, or a future one.
                label = labels.GetValueOrDefault(l, ""),
            })
            .OrderBy(l => l.type)
            .ThenBy(l => l.label)
            .ToList(),
    };

    /// <summary>
    /// Names for link chips, in one query per kind rather than one per chip. Anything not found is
    /// simply absent from the dictionary, which the caller renders as an empty label.
    /// </summary>
    public static async Task<Dictionary<(string Type, Guid Id), string>> ResolveLabelsAsync(
        CedarDbContext db, string ownerId, IEnumerable<(string Type, Guid Id)> targets)
    {
        var wanted = targets.Distinct().ToList();
        var labels = new Dictionary<(string, Guid), string>();
        if (wanted.Count == 0) return labels;

        var documentIds = wanted.Where(t => t.Type == LinkTargets.Document).Select(t => t.Id).ToList();
        if (documentIds.Count > 0)
        {
            foreach (var d in await db.Drafts
                         .Where(d => documentIds.Contains(d.Id) && d.OwnerId == ownerId)
                         .Select(d => new { d.Id, d.Title })
                         .ToListAsync())
                labels[(LinkTargets.Document, d.Id)] = d.Title;
        }

        var assetIds = wanted.Where(t => t.Type == LinkTargets.Asset).Select(t => t.Id).ToList();
        if (assetIds.Count > 0)
        {
            foreach (var a in await db.AssetEntries
                         .Where(a => assetIds.Contains(a.Id) && a.OwnerId == ownerId)
                         .Select(a => new { a.Id, a.FileName })
                         .ToListAsync())
                labels[(LinkTargets.Asset, a.Id)] = a.FileName;
        }

        var taskIds = wanted.Where(t => t.Type == LinkTargets.Task).Select(t => t.Id).ToList();
        if (taskIds.Count > 0)
        {
            foreach (var t in await db.GameTasks
                         .Where(t => taskIds.Contains(t.Id) && t.OwnerId == ownerId)
                         .Select(t => new { t.Id, t.Title })
                         .ToListAsync())
                labels[(LinkTargets.Task, t.Id)] = t.Title;
        }

        return labels;
    }

    private static Task<bool> OwnsProjectAsync(CedarDbContext db, Guid projectId, string ownerId) =>
        db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == ownerId);

    private static Task<bool> TargetExistsAsync(CedarDbContext db, string ownerId, string type, Guid id) => type switch
    {
        LinkTargets.Document => db.Drafts.AnyAsync(d => d.Id == id && d.OwnerId == ownerId),
        LinkTargets.Asset => db.AssetEntries.AnyAsync(a => a.Id == id && a.OwnerId == ownerId),
        LinkTargets.Task => db.GameTasks.AnyAsync(t => t.Id == id && t.OwnerId == ownerId),
        _ => Task.FromResult(false),
    };
}
