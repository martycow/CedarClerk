using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// Indie-gamedev module, T-120 (ADR-101/102/103). Registered from Program.cs only when
// Cedar:Modules:IndieDev is on — see ProjectEndpoints.IsEnabled.
//
// Everything here is owner-scoped the same way the rest of the app is: every query filters by the
// caller's id, and a row belonging to someone else is a 404 rather than a 403, so the API never
// confirms that an id exists.
public static class ProjectEndpoints
{
    // DocumentType is optional because ProjectType already implies one (ProjectTypes
    // .StarterDocumentType); it stays overridable so the rule never becomes a wall.
    public record CreateProjectRequest(string Name, string? Description, string? ProjectType, string? DocumentType, string? DocumentTitle);
    public record UpdateProjectRequest(string Name, string? Description, string? CoverUrl);
    public record ArchiveProjectRequest(bool Archived);
    public record CreateDocumentRequest(string? DocumentType, string? Title);
    public record UpdateDocumentTypeRequest(string DocumentType);

    public const string EnabledKey = "Cedar:Modules:IndieDev";

    private const int NameMaxLength = 80;
    private const int DescriptionMaxLength = 2000;

    public static bool IsEnabled(IConfiguration config) => config.GetValue<bool>(EnabledKey);

    /// <summary>
    /// ADR-103 — a project always holds at least one document, enforced here rather than in the
    /// schema. Called from the module's detach path AND from the ordinary draft delete in
    /// <c>DraftEndpoints</c>, because a rule that only one of the two doors honours is not a rule.
    /// </summary>
    public static async Task<bool> IsLastDocumentOfProjectAsync(CedarDbContext db, Guid draftId, Guid? projectId, string ownerId)
    {
        if (projectId is null) return false;
        var siblings = await db.Drafts.CountAsync(d => d.ProjectId == projectId && d.OwnerId == ownerId && d.Id != draftId);
        return siblings == 0;
    }

    public static void MapIndieDevEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization();

        // ?archived=true includes archived projects; by default they are out of the way but not gone.
        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db, bool archived = false) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var projects = await db.Projects
                .Where(p => p.OwnerId == uid && (archived || p.ArchivedAt == null))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            // Count and last activity in one pass. "Last activity" means the newest edit to any of
            // the project's documents — the project row itself never moves, so its CreatedAt would
            // have shown the day it was made under a column headed "Last activity".
            var stats = await db.Drafts
                .Where(d => d.OwnerId == uid && d.ProjectId != null)
                .GroupBy(d => d.ProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count(), LastActivity = g.Max(d => d.UpdatedAt) })
                .ToDictionaryAsync(g => g.ProjectId!.Value, g => g);

            return Results.Ok(projects.Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.ProjectType,
                p.CoverUrl,
                p.CreatedAt,
                p.ArchivedAt,
                documentCount = stats.GetValueOrDefault(p.Id)?.Count ?? 0,
                // Falls back to the project's own creation for the moment between the two writes
                // of a create — there is no state in which a project has no documents (ADR-103),
                // but a null here would still render as an empty cell rather than a date.
                lastActivityAt = stats.GetValueOrDefault(p.Id)?.LastActivity ?? p.CreatedAt,
            }));
        });

        // The project dashboard: the project plus its documents, which is the whole of what the
        // screen asks for today. Document bodies are deliberately not included — the list shows
        // titles, and CedarJson is the largest column in the database.
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            var documents = await db.Drafts
                .Where(d => d.ProjectId == id && d.OwnerId == uid)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => new { d.Id, d.Title, d.DocumentType, d.UpdatedAt, d.IsArchived, d.IsBlogPublished })
                .ToListAsync();

            return Results.Ok(new
            {
                project.Id,
                project.Name,
                project.Description,
                project.ProjectType,
                project.CoverUrl,
                project.CreatedAt,
                project.ArchivedAt,
                documents,
            });
        });

        // ADR-103 — creating a project creates its first document in the same transaction. There is
        // no moment at which an empty project exists, which is what makes the invariant true rather
        // than merely intended.
        group.MapPost("/", async (CreateProjectRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Invalid(req.Name, req.Description) is { } badRequest) return badRequest;

            var projectType = req.ProjectType ?? ProjectTypes.FullGame;
            if (!ProjectTypes.IsKnown(projectType))
                return Results.Json(new { error = ErrorMessages.UnknownProjectType(projectType) }, statusCode: StatusCodes.Status400BadRequest);

            // The project type decides the starter document unless the caller names one outright.
            var type = req.DocumentType ?? ProjectTypes.StarterDocumentType(projectType);
            if (!DocumentTypes.IsKnown(type))
                return Results.Json(new { error = ErrorMessages.UnknownDocumentType(type) }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var name = req.Name.Trim();
            var project = new Project { OwnerId = uid, Name = name, Description = req.Description?.Trim() ?? "", ProjectType = projectType };

            var title = string.IsNullOrWhiteSpace(req.DocumentTitle) ? name : req.DocumentTitle.Trim();
            var draft = new Draft { OwnerId = uid, Title = title, DocumentType = type, ProjectId = project.Id };

            db.Projects.Add(project);
            db.Drafts.Add(draft);
            await DraftRevisionService.RecordAsync(db, draft.Id, draft.PrimaryLanguage, draft.Title, draft.CedarJson);
            await db.SaveChangesAsync();

            return Results.Created($"/api/projects/{project.Id}", new { project.Id, project.Name, documentId = draft.Id });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateProjectRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Invalid(req.Name, req.Description) is { } badRequest) return badRequest;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            project.Name = req.Name.Trim();
            project.Description = req.Description?.Trim() ?? "";
            project.CoverUrl = req.CoverUrl;
            await db.SaveChangesAsync();
            return Results.Ok(new { project.Id, project.Name, project.Description, project.CoverUrl });
        });

        group.MapPost("/{id:guid}/archive", async (Guid id, ArchiveProjectRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            project.ArchivedAt = req.Archived ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
            return Results.Ok(new { project.Id, project.ArchivedAt });
        });

        // Documents are DETACHED, never deleted with the project — the same choice FolderEndpoints
        // made, and for the stronger reason here: a project holds the actual writing, and deleting
        // a container must not be a way to lose it by accident.
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            await db.Drafts.Where(d => d.ProjectId == id && d.OwnerId == uid)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ProjectId, d => null));

            db.Projects.Remove(project);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/documents", async (Guid id, CreateDocumentRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var type = req.DocumentType ?? DocumentTypes.Post;
            if (!DocumentTypes.IsKnown(type))
                return Results.Json(new { error = ErrorMessages.UnknownDocumentType(type) }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == id && p.OwnerId == uid)) return Results.NotFound();

            var draft = new Draft
            {
                OwnerId = uid,
                Title = string.IsNullOrWhiteSpace(req.Title) ? "Untitled" : req.Title.Trim(),
                DocumentType = type,
                ProjectId = id,
            };
            db.Drafts.Add(draft);
            await DraftRevisionService.RecordAsync(db, draft.Id, draft.PrimaryLanguage, draft.Title, draft.CedarJson);
            await db.SaveChangesAsync();
            return Results.Created($"/api/drafts/{draft.Id}", new { draft.Id, draft.Title, draft.DocumentType });
        });

        // Attaching an already-existing draft. Moving it out of another project is allowed and
        // silent, except when it would empty that other project — the invariant belongs to every
        // project, not only the one being edited.
        group.MapPut("/{id:guid}/documents/{draftId:guid}", async (Guid id, Guid draftId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == id && p.OwnerId == uid)) return Results.NotFound();

            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();
            if (draft.ProjectId == id) return Results.Ok(new { draft.Id, draft.ProjectId });

            if (await IsLastDocumentOfProjectAsync(db, draftId, draft.ProjectId, uid))
                return LastDocumentRefusal();

            draft.ProjectId = id;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.ProjectId });
        });

        group.MapDelete("/{id:guid}/documents/{draftId:guid}", async (Guid id, Guid draftId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == uid && d.ProjectId == id);
            if (draft is null) return Results.NotFound();

            if (await IsLastDocumentOfProjectAsync(db, draftId, id, uid))
                return LastDocumentRefusal();

            draft.ProjectId = null;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // A document's type, changeable after the fact — the first guess about what a document is
        // does not have to be the last word. Its own group because it addresses a draft, not a
        // project, and a draft need not be in one to have a type.
        var documents = app.MapGroup("/api/documents").RequireAuthorization();

        documents.MapPut("/{draftId:guid}/type", async (Guid draftId, UpdateDocumentTypeRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (!DocumentTypes.IsKnown(req.DocumentType))
                return Results.Json(new { error = ErrorMessages.UnknownDocumentType(req.DocumentType) }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            // Turning a published post into working material would leave a live blog page behind a
            // document that no longer claims to be publishable, so the refusal names the fix.
            if (!DocumentTypes.IsPublishable(req.DocumentType) && draft.IsBlogPublished)
                return Results.Json(new { error = ErrorMessages.DocumentTypeBlogPublished }, statusCode: StatusCodes.Status409Conflict);

            draft.DocumentType = req.DocumentType;
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.DocumentType });
        });
    }

    private static IResult LastDocumentRefusal() =>
        Results.Json(new { error = ErrorMessages.ProjectNeedsOneDocument }, statusCode: StatusCodes.Status409Conflict);

    private static IResult? Invalid(string name, string? description)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Length > NameMaxLength)
            return Results.Json(new { error = ErrorMessages.ProjectNameLength(NameMaxLength) }, statusCode: StatusCodes.Status400BadRequest);
        if (description is { Length: > DescriptionMaxLength })
            return Results.Json(new { error = ErrorMessages.ProjectDescriptionLength(DescriptionMaxLength) }, statusCode: StatusCodes.Status400BadRequest);
        return null;
    }
}
