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
    public record CreateProjectRequest(string Name, string? Description, string? ProjectType, string? DocumentType, string? DocumentTitle, string? Language = null);
    public record CreateExampleRequest(string? Language);
    public record ShowcaseRequest(bool Enabled, string? Slug, string? Links);
    public record UpdateProjectRequest(string Name, string? Description, string? CoverUrl);
    public record ArchiveProjectRequest(bool Archived);
    public record CreateDocumentRequest(string? DocumentType, string? Title);
    public record UpdateDocumentTypeRequest(string DocumentType);

    public const string EnabledKey = "Cedar:Modules:IndieDev";

    private const int NameMaxLength = 80;
    private const int DescriptionMaxLength = 2000;
    private const int ShowcaseLinksMaxLength = 2000;

    public static bool IsEnabled(IConfiguration config) => config.IsOn(EnabledKey);

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

            // T-123 — the "Tasks" column of the list is the *open* count, not every task ever
            // written: a finished project would otherwise show its largest number on the day it
            // stopped having anything left to do.
            var openTasks = await db.GameTasks
                .Where(t => t.OwnerId == uid && t.ArchivedAt == null && t.Status != TaskStatuses.Done)
                .GroupBy(t => t.ProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.ProjectId, g => g.Count);

            var assetCounts = await db.AssetEntries
                .Where(a => a.OwnerId == uid)
                .GroupBy(a => a.ProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.ProjectId, g => g.Count);

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
                openTaskCount = openTasks.GetValueOrDefault(p.Id),
                assetCount = assetCounts.GetValueOrDefault(p.Id),
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

            // T-123 — the dashboard's right rail. Five tasks, sorted by urgency in one place so the
            // rail and the board cannot disagree about what "next" means (TaskEndpoints.UpNextAsync).
            var upNext = await TaskEndpoints.UpNextAsync(db, uid, id);
            var upNextLinks = await ProjectLinks.LinkedIdsForManyAsync(
                db, uid, LinkTargets.Task, upNext.Select(t => t.Id).ToList());
            var upNextLabels = await TaskEndpoints.ResolveLabelsAsync(
                db, uid, upNextLinks.Values.SelectMany(v => v));

            var taskCounts = await db.GameTasks
                .Where(t => t.ProjectId == id && t.OwnerId == uid && t.ArchivedAt == null)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Status, g => g.Count);

            return Results.Ok(new
            {
                project.Id,
                project.Name,
                project.Description,
                project.ProjectType,
                project.CoverUrl,
                project.CreatedAt,
                project.ArchivedAt,
                project.ShowcaseSlug,
                project.ShowcaseLinks,
                documents,
                upNext = upNext.Select(t => TaskEndpoints.Describe(t, upNextLinks, upNextLabels)),
                // T-124 — the rail's sprint card. Null means no sprint covers today, which the
                // card says in words rather than rendering an empty progress bar.
                currentSprint = await SprintEndpoints.CurrentAsync(db, uid, id),
                taskCounts,
                openTaskCount = taskCounts.Where(c => TaskStatuses.IsOpen(c.Key)).Sum(c => c.Value),
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
            // T-160 (ADR-133) — the starter document is born with a skeleton, not blank, in the
            // language the client asked for (the interface language, most usefully).
            if (!string.IsNullOrWhiteSpace(req.Language) && Languages.IsContentLanguage(req.Language))
                draft.PrimaryLanguage = req.Language;
            draft.CedarJson = StarterTemplates.For(type, projectType, draft.PrimaryLanguage);

            db.Projects.Add(project);
            db.Drafts.Add(draft);
            await DraftRevisionService.RecordAsync(db, draft.Id, draft.PrimaryLanguage, draft.Title, draft.CedarJson);
            await db.SaveChangesAsync();

            return Results.Created($"/api/projects/{project.Id}", new { project.Id, project.Name, documentId = draft.Id });
        });

        // T-160 (ADR-133) — the example project, on demand from the empty state rather than seeded
        // silently at registration: an account that starts its life cleaning up data it never asked
        // for is worse than an empty screen with two honest buttons. The material demonstrates the
        // loop that sells the product: tasks → sprint → build → a devlog written from them (T-158).
        group.MapPost("/example", async (CreateExampleRequest? req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var language = !string.IsNullOrWhiteSpace(req?.Language) && Languages.IsContentLanguage(req.Language)
                ? req.Language
                : Languages.English;
            var ru = language == Languages.Russian;
            var now = DateTime.UtcNow;

            var project = new Project
            {
                OwnerId = uid,
                Name = "Cedar Quest",
                Description = ru
                    ? "Пример проекта — потыкайте и удалите, когда надоест."
                    : "An example project — poke around, then delete it when you're done.",
                ProjectType = ProjectTypes.FullGame,
                NextSprintNumber = 2,
            };

            var gdd = new Draft
            {
                OwnerId = uid,
                ProjectId = project.Id,
                DocumentType = DocumentTypes.Design,
                Title = ru ? "Cedar Quest — дизайн-документ" : "Cedar Quest — design doc",
                PrimaryLanguage = language,
            };
            gdd.CedarJson = StarterTemplates.For(DocumentTypes.Design, ProjectTypes.FullGame, language);

            var sprint = new Sprint
            {
                OwnerId = uid,
                ProjectId = project.Id,
                Number = 1,
                Name = ru ? "Первый плейабл" : "First playable",
                StartsAt = now.Date.AddDays(-7),
                EndsAt = now.Date.AddDays(6),
            };

            var build = new Build
            {
                OwnerId = uid,
                ProjectId = project.Id,
                Version = "0.1.0",
                Notes = ru ? "Первый играбельный билд" : "First playable build",
                ReleasedAt = now.AddDays(-2),
            };

            GameTask Task(string en, string ruTitle, string status, int priority,
                DateTime? completedAt = null, DateTime? dueAt = null, bool inSprint = true, bool inBuild = false) => new()
            {
                OwnerId = uid,
                ProjectId = project.Id,
                Title = ru ? ruTitle : en,
                Status = status,
                Priority = priority,
                SprintId = inSprint ? sprint.Id : null,
                BuildId = inBuild ? build.Id : null,
                CompletedAt = completedAt,
                DueAt = dueAt,
            };

            var tasks = new List<GameTask>
            {
                Task("Player movement & camera", "Движение игрока и камера", TaskStatuses.Done, TaskPriorities.Highest,
                    completedAt: now.AddDays(-5), inBuild: true),
                Task("Pixel-art tileset for the forest", "Пиксель-арт тайлсет леса", TaskStatuses.Done, TaskPriorities.Normal,
                    completedAt: now.AddDays(-3), inBuild: true),
                Task("Main menu music sketch", "Набросок музыки главного меню", TaskStatuses.Done, TaskPriorities.Lowest,
                    completedAt: now.AddDays(-1)),
                Task("Enemy AI: patrol and chase", "ИИ врагов: патруль и погоня", TaskStatuses.InProgress, TaskPriorities.Highest,
                    dueAt: now.Date.AddDays(3)),
                Task("Sound effects for jumps and hits", "Звуки прыжков и ударов", TaskStatuses.Planned, TaskPriorities.Normal),
                Task("Steam page draft", "Черновик страницы в Steam", TaskStatuses.Backlog, TaskPriorities.Lowest, inSprint: false),
            };

            var done = tasks.Where(t => t.Status == TaskStatuses.Done)
                .OrderBy(t => t.CompletedAt).Select(t => t.Title).ToList();
            var open = tasks.Where(t => t.Status != TaskStatuses.Done && t.SprintId != null)
                .Select(t => t.Title).ToList();

            var devlogBody = new System.Text.Json.Nodes.JsonArray
            {
                DocJson.Paragraph(ru
                    ? "Семь дней от пустой сцены до билда, по которому можно ходить. Вот что произошло."
                    : "Seven days from an empty scene to a build you can actually walk around in. Here's what happened."),
                DocJson.Heading(ru ? "Что сделано" : "What got done"),
                DocJson.BulletList(done),
                DocJson.Heading(ru ? "Релизы" : "Released"),
                DocJson.Paragraph($"0.1.0 — {build.Notes}"),
                DocJson.Heading(ru ? "Что дальше" : "What's next"),
                DocJson.BulletList(open),
            };
            var devlog = new Draft
            {
                OwnerId = uid,
                ProjectId = project.Id,
                DocumentType = DocumentTypes.Post,
                Title = ru ? "Девлог #1 — первый плейабл" : "Devlog #1 — first playable",
                PrimaryLanguage = language,
                CedarJson = DocJson.Doc(devlogBody),
            };

            db.Projects.Add(project);
            db.Drafts.AddRange(gdd, devlog);
            db.Sprints.Add(sprint);
            db.Builds.Add(build);
            db.GameTasks.AddRange(tasks);
            await DraftRevisionService.RecordAsync(db, gdd.Id, gdd.PrimaryLanguage, gdd.Title, gdd.CedarJson);
            await DraftRevisionService.RecordAsync(db, devlog.Id, devlog.PrimaryLanguage, devlog.Title, devlog.CedarJson);
            await db.SaveChangesAsync();

            return Results.Created($"/api/projects/{project.Id}", new { project.Id, project.Name });
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

        // T-159 (ADR-134) — the public game page's switch. The slug is slugified server-side and
        // globally unique (one blog host); turning the page off clears the slug and keeps the
        // links, so switching it back on does not mean re-typing them.
        group.MapPut("/{id:guid}/showcase", async (Guid id, ShowcaseRequest req, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (project is null) return Results.NotFound();

            var links = (req.Links ?? "").Trim();
            if (links.Length > ShowcaseLinksMaxLength)
                return Results.BadRequest(new { error = $"Store links are too long ({ShowcaseLinksMaxLength} characters maximum)" });
            project.ShowcaseLinks = links;

            if (!req.Enabled)
            {
                project.ShowcaseSlug = null;
                await db.SaveChangesAsync();
                return Results.Ok(new { showcaseSlug = (string?)null, url = (string?)null });
            }

            var slug = SlugGenerator.Slugify(string.IsNullOrWhiteSpace(req.Slug) ? project.Name : req.Slug);
            if (slug.Length == 0)
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseSlugEmpty });
            if (await db.Projects.AnyAsync(p => p.ShowcaseSlug == slug && p.Id != id))
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseSlugTaken(slug) });

            project.ShowcaseSlug = slug;
            await db.SaveChangesAsync();

            var blogBase = $"https://{cfg[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost}";
            return Results.Ok(new { showcaseSlug = slug, url = $"{blogBase}/games/{slug}" });
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

            // Documents survive the project and are simply unfiled — they are the user's writing,
            // and deleting a container is not a request to delete what was in it.
            await db.Drafts.Where(d => d.ProjectId == id && d.OwnerId == uid)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ProjectId, d => null));

            // Everything that only means anything *inside* this project does go, though. None of
            // these three has a navigation property, so EF cascades none of them, and each was
            // being left behind: an asset index of a folder nobody is indexing any more, tasks with
            // no board to appear on, and links naming both.
            await db.AssetEntries.Where(a => a.ProjectId == id && a.OwnerId == uid).ExecuteDeleteAsync();
            await db.GameTasks.Where(t => t.ProjectId == id && t.OwnerId == uid).ExecuteDeleteAsync();
            await db.EntityLinks.Where(l => l.ProjectId == id && l.OwnerId == uid).ExecuteDeleteAsync();

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
            // ADR-204 — the pictures inside it come along, unless another project already claimed them.
            var filed = await AssetFiling.FileAsync(db, uid, id, draft.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new { draft.Id, draft.ProjectId, filedAssets = filed });
        });

        // ADR-204 — the backlog, on demand and never silently: a filing rule applied retroactively
        // without asking is indistinguishable from files moving on their own.
        group.MapPost("/{id:guid}/assets/refile", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == id && p.OwnerId == uid)) return Results.NotFound();

            var filed = await AssetFiling.SweepProjectAsync(db, uid, id);
            if (filed > 0) await db.SaveChangesAsync();
            return Results.Ok(new { filed });
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
