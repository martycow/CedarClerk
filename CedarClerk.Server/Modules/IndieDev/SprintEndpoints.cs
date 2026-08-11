using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// Indie-gamedev module, T-124 (ADR-106/111) — the development planner. Sprints sit over the tasks
// that T-123 built; a task carries at most one sprint id, and the column for it already existed.
//
// Owner-scoped like the rest of the module: someone else's row answers 404, never 403.
public static class SprintEndpoints
{
    public record SaveSprintRequest(string Name, DateTime StartsAt, DateTime EndsAt);

    private const int NameMaxLength = 80;

    public static void MapSprintEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/sprints").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            return Results.Ok(await DescribeAllAsync(db, uid, projectId));
        });

        group.MapPost("/", async (Guid projectId, SaveSprintRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid);
            if (project is null) return Results.NotFound();
            if (Invalid(req) is { } bad) return bad;

            // Sequential within the project and never reused (ADR-111). Taken from the project's
            // own counter rather than MAX(Number) + 1: that expression hands the highest number
            // straight back out the moment its sprint is deleted, which is reuse — caught by
            // running it. A gap in the numbering is honest; a second "S3" is not.
            var number = project.NextSprintNumber;
            project.NextSprintNumber = number + 1;

            var sprint = new Sprint
            {
                OwnerId = uid,
                ProjectId = projectId,
                Number = number,
                Name = req.Name.Trim(),
                StartsAt = req.StartsAt.Date,
                EndsAt = req.EndsAt.Date,
            };

            db.Sprints.Add(sprint);
            await db.SaveChangesAsync();

            return Results.Ok(Describe(sprint, [], DateTime.UtcNow));
        });

        var single = app.MapGroup("/api/sprints/{id:guid}").RequireAuthorization();

        single.MapPut("/", async (Guid id, SaveSprintRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (sprint is null) return Results.NotFound();
            if (Invalid(req) is { } bad) return bad;

            sprint.Name = req.Name.Trim();
            sprint.StartsAt = req.StartsAt.Date;
            sprint.EndsAt = req.EndsAt.Date;
            await db.SaveChangesAsync();

            var tasks = await db.GameTasks
                .Where(t => t.SprintId == id && t.OwnerId == uid && t.ArchivedAt == null)
                .ToListAsync();
            return Results.Ok(Describe(sprint, tasks, DateTime.UtcNow));
        });

        single.MapDelete("/", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (sprint is null) return Results.NotFound();

            // The tasks come loose, they do not go with it (ADR-111): deleting a container is not
            // a request to delete what was in it. They land back in "No sprint", which the planner
            // shows as its own group rather than as nothing.
            await db.GameTasks.Where(t => t.SprintId == id && t.OwnerId == uid)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SprintId, t => null));

            db.Sprints.Remove(sprint);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Every sprint of a project with its progress, ordered current → planned → finished, and
    /// within a group by date. One query for the sprints and one for the tasks, not one per sprint.
    /// </summary>
    public static async Task<List<object>> DescribeAllAsync(CedarDbContext db, string ownerId, Guid projectId)
    {
        var sprints = await db.Sprints
            .Where(s => s.ProjectId == projectId && s.OwnerId == ownerId)
            .ToListAsync();

        var tasks = await db.GameTasks
            .Where(t => t.ProjectId == projectId && t.OwnerId == ownerId
                        && t.ArchivedAt == null && t.SprintId != null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var bySprint = tasks.GroupBy(t => t.SprintId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        return sprints
            .OrderBy(s => SprintStates.Order(SprintStates.Of(s.StartsAt, s.EndsAt, now)))
            // Within a group, by date — and the later start wins among overlapping current ones,
            // which is the tie-break ADR-111 leaves to the caller.
            .ThenByDescending(s => SprintStates.Of(s.StartsAt, s.EndsAt, now) == SprintStates.Current ? s.StartsAt : DateTime.MinValue)
            .ThenBy(s => s.StartsAt)
            .Select(s => Describe(s, bySprint.GetValueOrDefault(s.Id, []), now))
            .ToList();
    }

    /// <summary>
    /// The current sprint of a project, or null. Used by the dashboard's rail, where "current"
    /// means one card — so overlapping sprints resolve to the one that started most recently.
    /// </summary>
    public static async Task<object?> CurrentAsync(CedarDbContext db, string ownerId, Guid projectId)
    {
        var now = DateTime.UtcNow;
        var sprints = await db.Sprints
            .Where(s => s.ProjectId == projectId && s.OwnerId == ownerId)
            .ToListAsync();

        var current = sprints
            .Where(s => SprintStates.Of(s.StartsAt, s.EndsAt, now) == SprintStates.Current)
            .OrderByDescending(s => s.StartsAt)
            .FirstOrDefault();

        if (current is null) return null;

        var tasks = await db.GameTasks
            .Where(t => t.SprintId == current.Id && t.OwnerId == ownerId && t.ArchivedAt == null)
            .ToListAsync();

        return Describe(current, tasks, now);
    }

    private static object Describe(Sprint s, List<GameTask> tasks, DateTime now) => new
    {
        s.Id,
        s.ProjectId,
        s.Number,
        s.Name,
        s.StartsAt,
        s.EndsAt,
        state = SprintStates.Of(s.StartsAt, s.EndsAt, now),
        taskCount = tasks.Count,
        doneCount = tasks.Count(t => t.Status == TaskStatuses.Done),
        // A sprint is never itself "overdue" (ADR-111) — it contains tasks that are, and the UI
        // says so in those words instead of colouring the whole card.
        overdueCount = tasks.Count(t => t.Status != TaskStatuses.Done && t.DueAt is { } due && due.Date < now.Date),
    };

    private static IResult? Invalid(SaveSprintRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > NameMaxLength)
            return Results.BadRequest(new { error = ErrorMessages.SprintNameLength(NameMaxLength) });
        if (req.EndsAt.Date < req.StartsAt.Date)
            return Results.BadRequest(new { error = ErrorMessages.SprintEndsBeforeItStarts });
        return null;
    }
}
