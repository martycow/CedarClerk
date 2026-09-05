using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Ai;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Tenancy;
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
    // PresetId names a project preset (T-331): it supplies the type, the starter document and its
    // title in one pick, and anything the caller states outright still wins over it — the dialog
    // lets a preset be chosen and then edited before Create.
    public record CreateProjectRequest(string Name, string? Description, string? ProjectType, string? DocumentType, string? DocumentTitle, string? Language = null, Guid? PresetId = null);

    /// <summary>Count is every build of the project; LatestVersion is the newest *released* one, null
    /// while nothing has shipped — a planned build is a plan, not a version anyone can play.</summary>
    public sealed record BuildSummary(int Count, string? LatestVersion);

    public static async Task<Dictionary<Guid, BuildSummary>> BuildSummariesAsync(CedarDbContext db, string ownerId)
    {
        var builds = await db.Builds
            .Where(b => b.OwnerId == ownerId)
            .Select(b => new { b.ProjectId, b.Version, b.ReleasedAt })
            .ToListAsync();

        return builds.GroupBy(b => b.ProjectId).ToDictionary(g => g.Key, g => new BuildSummary(
            g.Count(),
            g.Where(b => b.ReleasedAt != null).OrderByDescending(b => b.ReleasedAt).Select(b => b.Version).FirstOrDefault()));
    }

    /// <summary>
    /// T-166 — the newest publish of any kind across each project's documents: blog, Telegram
    /// (ChannelPost) or a succeeded queue job. Absent from the result means never published.
    /// </summary>
    public static async Task<Dictionary<Guid, DateTime>> LastPublishedAsync(CedarDbContext db, string ownerId)
    {
        var filed = db.Drafts.Where(d => d.OwnerId == ownerId && d.ProjectId != null);

        var blog = await filed.Where(d => d.BlogPublishedAt != null)
            .GroupBy(d => d.ProjectId)
            .Select(g => new { ProjectId = g.Key!.Value, At = g.Max(d => d.BlogPublishedAt!.Value) })
            .ToListAsync();

        var telegram = await db.ChannelPosts.Where(p => p.OwnerId == ownerId)
            .Join(filed, p => p.DraftId, d => d.Id, (p, d) => new { d.ProjectId, p.PublishedAt })
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key!.Value, At = g.Max(x => x.PublishedAt) })
            .ToListAsync();

        var jobs = await db.PublishJobs
            .Where(j => j.OwnerId == ownerId && j.Status == PublishJobStatus.Succeeded && j.FinishedAt != null)
            .Join(filed, j => j.DraftId, d => d.Id, (j, d) => new { d.ProjectId, At = j.FinishedAt!.Value })
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key!.Value, At = g.Max(x => x.At) })
            .ToListAsync();

        return blog.Concat(telegram).Concat(jobs)
            .GroupBy(x => x.ProjectId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.At));
    }

    /// <summary>T-249 — the ten things a project journal can say. Strings on the wire, never an enum ordinal.</summary>
    public static class ActivityKinds
    {
        public const string DocumentCreated = "document-created";
        public const string DocumentUpdated = "document-updated";
        public const string TaskCreated = "task-created";
        public const string TaskCompleted = "task-completed";
        public const string BuildCreated = "build-created";
        public const string BuildReleased = "build-released";
        public const string BlogPublished = "blog-published";
        public const string TelegramPublished = "telegram-published";
        public const string Published = "published";
        public const string PublishFailed = "publish-failed";

        public static readonly IReadOnlyList<string> All =
        [
            DocumentCreated, DocumentUpdated, TaskCreated, TaskCompleted, BuildCreated, BuildReleased,
            BlogPublished, TelegramPublished, Published, PublishFailed,
        ];
    }

    public sealed record ActivityItem(DateTime At, string Kind, string Title, string? Subtitle, string? Href, string? Actor);

    public const int ActivityDefaultTake = 20;
    public const int ActivityMaxTake = 100;

    // An edit within a minute of creation is the starter template being saved, not a second event.
    private static readonly TimeSpan ActivityEditGap = TimeSpan.FromMinutes(1);
    // Parts of one Telegram thread land seconds apart; the journal names the send, not each part.
    private static readonly TimeSpan ActivityThreadWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// T-249 — newest first, at most <paramref name="take"/> items, read under the project owner's
    /// id. Every source is capped at <paramref name="take"/> before the union, so a project with a
    /// thousand tasks costs the same as one with ten.
    /// </summary>
    public static async Task<List<ActivityItem>> ActivityAsync(
        CedarDbContext db, IConfiguration cfg, string ownerId, Guid projectId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, ActivityMaxTake);
        var items = new List<ActivityItem>();
        var drafts = db.Drafts.Where(d => d.ProjectId == projectId && d.OwnerId == ownerId);

        var created = await drafts.OrderByDescending(d => d.CreatedAt).Take(take)
            .Select(d => new { d.Id, d.Title, d.DocumentType, d.CreatedAt }).ToListAsync(ct);
        items.AddRange(created.Select(d => new ActivityItem(
            d.CreatedAt, ActivityKinds.DocumentCreated, d.Title, d.DocumentType, $"/editor?draft={d.Id}", null)));

        var updated = await drafts.OrderByDescending(d => d.UpdatedAt).Take(take)
            .Select(d => new { d.Id, d.Title, d.DocumentType, d.CreatedAt, d.UpdatedAt }).ToListAsync(ct);
        items.AddRange(updated.Where(d => d.UpdatedAt > d.CreatedAt + ActivityEditGap).Select(d => new ActivityItem(
            d.UpdatedAt, ActivityKinds.DocumentUpdated, d.Title, d.DocumentType, $"/editor?draft={d.Id}", null)));

        var blogHost = await BlogTenant.HostForOwnerAsync(db, cfg, ownerId, ct);
        var blog = await drafts.Where(d => d.BlogPublishedAt != null).OrderByDescending(d => d.BlogPublishedAt).Take(take)
            .Select(d => new { d.Title, d.BlogSlug, At = d.BlogPublishedAt!.Value }).ToListAsync(ct);
        items.AddRange(blog.Select(d => new ActivityItem(
            d.At, ActivityKinds.BlogPublished, d.Title, "blog",
            blogHost is null || d.BlogSlug is null ? null : new BlogSite(ownerId, blogHost).PostUrl(d.BlogSlug), null)));

        var tasks = db.GameTasks.Where(t => t.ProjectId == projectId && t.OwnerId == ownerId);
        var tasksHref = $"/projects/{projectId}/tasks";
        var tasksCreated = await tasks.OrderByDescending(t => t.CreatedAt).Take(take)
            .Select(t => new { t.Title, t.Status, t.Assignee, t.CreatedAt }).ToListAsync(ct);
        items.AddRange(tasksCreated.Select(t => new ActivityItem(
            t.CreatedAt, ActivityKinds.TaskCreated, t.Title, t.Status, tasksHref, NullIfBlank(t.Assignee))));

        var tasksCompleted = await tasks.Where(t => t.CompletedAt != null).OrderByDescending(t => t.CompletedAt).Take(take)
            .Select(t => new { t.Title, t.Status, t.Assignee, At = t.CompletedAt!.Value }).ToListAsync(ct);
        items.AddRange(tasksCompleted.Select(t => new ActivityItem(
            t.At, ActivityKinds.TaskCompleted, t.Title, t.Status, tasksHref, NullIfBlank(t.Assignee))));

        var builds = db.Builds.Where(b => b.ProjectId == projectId && b.OwnerId == ownerId);
        var buildsHref = $"/projects/{projectId}/builds";
        var buildsCreated = await builds.OrderByDescending(b => b.CreatedAt).Take(take)
            .Select(b => new { b.Version, b.CreatedAt }).ToListAsync(ct);
        items.AddRange(buildsCreated.Select(b => new ActivityItem(
            b.CreatedAt, ActivityKinds.BuildCreated, b.Version, null, buildsHref, null)));

        var buildsReleased = await builds.Where(b => b.ReleasedAt != null).OrderByDescending(b => b.ReleasedAt).Take(take)
            .Select(b => new { b.Version, At = b.ReleasedAt!.Value }).ToListAsync(ct);
        items.AddRange(buildsReleased.Select(b => new ActivityItem(
            b.At, ActivityKinds.BuildReleased, b.Version, null, buildsHref, null)));

        // Over-fetched so a long thread collapsing into one item cannot starve this source.
        var posts = await db.ChannelPosts.Where(p => p.OwnerId == ownerId)
            .Join(drafts, p => p.DraftId, d => d.Id, (p, d) => new { p, d.Title })
            .Join(db.Channels, x => x.p.ChannelId, c => c.Id, (x, c) => new
            {
                x.p.DraftId, x.p.ChannelId, x.p.PublishedAt, x.p.TelegramMessageId, x.Title, Channel = c.Title, c.Username,
            })
            .OrderByDescending(x => x.PublishedAt).Take(take * 4)
            .ToListAsync(ct);
        foreach (var group in posts.GroupBy(x => (x.DraftId, x.ChannelId)))
        {
            DateTime? sendStart = null;
            foreach (var part in group.OrderBy(x => x.PublishedAt).ThenBy(x => x.TelegramMessageId))
            {
                if (sendStart is { } start && part.PublishedAt - start < ActivityThreadWindow) continue;
                sendStart = part.PublishedAt;
                items.Add(new ActivityItem(
                    part.PublishedAt, ActivityKinds.TelegramPublished, part.Title, part.Channel,
                    part.Username is null ? null : $"https://t.me/{part.Username}/{part.TelegramMessageId}", null));
            }
        }

        var jobs = db.PublishJobs.Where(j => j.OwnerId == ownerId && j.PartIndex == 0)
            .Join(drafts, j => j.DraftId, d => d.Id, (j, d) => new { j, d.Title });
        var succeeded = await jobs
            .Where(x => x.j.Status == PublishJobStatus.Succeeded && x.j.Network != PublishNetworks.Telegram)
            .OrderByDescending(x => x.j.FinishedAt).Take(take)
            .Select(x => new { x.Title, x.j.Network, x.j.PublicUrl, At = x.j.FinishedAt ?? x.j.CreatedAt }).ToListAsync(ct);
        items.AddRange(succeeded.Select(x => new ActivityItem(
            x.At, ActivityKinds.Published, x.Title, x.Network, x.PublicUrl, null)));

        var failed = await jobs
            .Where(x => x.j.Status == PublishJobStatus.Failed || x.j.Status == PublishJobStatus.Unknown)
            .OrderByDescending(x => x.j.FinishedAt ?? x.j.StartedAt ?? x.j.CreatedAt).Take(take)
            .Select(x => new { x.Title, x.j.Network, x.j.Error, At = x.j.FinishedAt ?? x.j.StartedAt ?? x.j.CreatedAt }).ToListAsync(ct);
        items.AddRange(failed.Select(x => new ActivityItem(
            x.At, ActivityKinds.PublishFailed, x.Title,
            string.IsNullOrWhiteSpace(x.Error) ? x.Network : $"{x.Network}: {x.Error}", null, null)));

        return items.OrderByDescending(i => i.At).ThenBy(i => i.Kind).Take(take).ToList();
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public record ShowcaseRequest(bool Enabled, string? Slug, string? Links, string? Gallery,
        string? TrailerUrl, string? CustomDomain,
        string? PressContactEmail = null, string? PressPrice = null, string? PressEngine = null,
        string? PressGenre = null, string? PressFactsheetRows = null,
        string? DiscoveryCategory = null, string? BlocksJson = null);
    public record ShowcaseAssistRequest(string Kind, string Text);
    public record UpdateProjectRequest(string Name, string? Description, string? CoverUrl);
    public record ArchiveProjectRequest(bool Archived);
    public record CreateDocumentRequest(string? DocumentType, string? Title, Guid? PresetId);
    public record UpdateDocumentTypeRequest(string DocumentType);

    public const string EnabledKey = "Cedar:Modules:IndieDev";

    private const int NameMaxLength = 80;
    private const int DescriptionMaxLength = 2000;
    private const int ShowcaseLinksMaxLength = 2000;
    private const int ShowcaseStatsDays = 30;
    private const int PressFieldMaxLength = 200;
    private const int PressFactsheetMaxLength = 2000;

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

            var builds = await BuildSummariesAsync(db, uid);
            var lastPublished = await LastPublishedAsync(db, uid);

            return Results.Ok(projects.Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.ProjectType,
                p.DiscoveryCategory,
                p.CoverUrl,
                p.CreatedAt,
                p.ArchivedAt,
                documentCount = stats.GetValueOrDefault(p.Id)?.Count ?? 0,
                openTaskCount = openTasks.GetValueOrDefault(p.Id),
                assetCount = assetCounts.GetValueOrDefault(p.Id),
                buildCount = builds.GetValueOrDefault(p.Id)?.Count ?? 0,
                latestBuildVersion = builds.GetValueOrDefault(p.Id)?.LatestVersion,
                lastPublishedAt = lastPublished.TryGetValue(p.Id, out var publishedAt) ? publishedAt : (DateTime?)null,
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
                project.DiscoveryCategory,
                project.CoverUrl,
                project.TeamId,
                project.CreatedAt,
                project.ArchivedAt,
                project.ShowcaseSlug,
                project.ShowcaseLinks,
                project.ShowcaseGallery,
                project.ShowcaseBlocksJson,
                project.ShowcaseTrailerUrl,
                project.CustomDomain,
                project.PressContactEmail,
                project.PressPrice,
                project.PressEngine,
                project.PressGenre,
                project.PressFactsheetRows,
                documents,
                upNext = upNext.Select(t => TaskEndpoints.Describe(t, upNextLinks, upNextLabels)),
                // T-124 — the rail's sprint card. Null means no sprint covers today, which the
                // card says in words rather than rendering an empty progress bar.
                currentSprint = await SprintEndpoints.CurrentAsync(db, uid, id),
                taskCounts,
                openTaskCount = taskCounts.Where(c => TaskStatuses.IsOpen(c.Key)).Sum(c => c.Value),
            });
        });

        // T-249 — readable by anyone the project resolves for, the way the canvas is: a member on
        // the hub sees the same journal as the owner, read under the owner's tenant.
        group.MapGet("/{id:guid}/activity", async (
            Guid id, ClaimsPrincipal user, IServiceScopeFactory scopes, IConfiguration cfg, CancellationToken ct,
            int take = ActivityDefaultTake) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var access = await ProjectAccessResolver.ResolveAsync(scopes, id, uid, ct);
            if (access is null) return Results.NotFound();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
            var items = await ActivityAsync(db, cfg, access.OwnerId, id, take, ct);
            return Results.Ok(new { items });
        });

        // ADR-103 — creating a project creates its first document in the same transaction. There is
        // no moment at which an empty project exists, which is what makes the invariant true rather
        // than merely intended.
        group.MapPost("/", async (CreateProjectRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Invalid(req.Name, req.Description) is { } badRequest) return badRequest;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

            // T-331 — a project preset is a named default for the three fields below. It is read
            // first so an explicit value in the request still overrides it.
            ProjectPresetConfig? preset = null;
            if (req.PresetId is { } presetId)
            {
                var row = await db.Presets.FirstOrDefaultAsync(p =>
                    p.Id == presetId && p.OwnerId == uid && p.Kind == PresetKinds.Project);
                if (row is null) return Results.NotFound();
                preset = ProjectPresetConfig.Parse(row.ConfigJson);
            }

            var projectType = req.ProjectType ?? preset?.ProjectType ?? ProjectTypes.FullGame;
            if (!ProjectTypes.IsKnown(projectType))
                return Results.Json(new { error = ErrorMessages.UnknownProjectType(projectType) }, statusCode: StatusCodes.Status400BadRequest);

            // The project type decides the starter document unless the caller or the preset names one.
            var type = req.DocumentType ?? preset?.DocumentType ?? ProjectTypes.StarterDocumentType(projectType);
            if (!DocumentTypes.IsKnown(type))
                return Results.Json(new { error = ErrorMessages.UnknownDocumentType(type) }, statusCode: StatusCodes.Status400BadRequest);

            var name = req.Name.Trim();
            var description = req.Description?.Trim();
            if (string.IsNullOrEmpty(description)) description = preset?.Description ?? "";
            var project = new Project
            {
                OwnerId = uid,
                Name = name,
                Description = description,
                ProjectType = projectType,
                DiscoveryCategory = DiscoveryCategories.ForProjectType(projectType),
            };

            var title = string.IsNullOrWhiteSpace(req.DocumentTitle)
                ? preset?.DocumentTitle ?? name
                : req.DocumentTitle.Trim();
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
            await GlossaryUsage.SyncForDraftAsync(db, uid, draft.Id);

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

            // ADR-216 — the gallery is stored the way it will be read: parsing here means a line the
            // page would skip is a line the owner never sees saved, instead of one that vanishes
            // silently at render time.
            if ((req.Gallery ?? "").Length > Consts.Showcase.GalleryMaxLength)
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseGalleryTooLong(Consts.Showcase.GalleryMaxLength) });
            project.ShowcaseGallery = string.Join("\n", ShowcaseGallery.Parse(req.Gallery));
            project.ShowcaseBlocksJson = ShowcaseLayouts.Serialize(ShowcaseLayouts.Parse(req.BlocksJson));

            var trailer = (req.TrailerUrl ?? "").Trim();
            if (trailer.Length > 0 && YouTubeLink.VideoId(trailer) is null)
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseTrailerNotYouTube });
            project.ShowcaseTrailerUrl = trailer.Length == 0 ? null : trailer;
            if (req.DiscoveryCategory is not null)
            {
                if (!DiscoveryCategories.IsKnown(req.DiscoveryCategory))
                    return Results.BadRequest(new { error = ErrorMessages.UnknownDiscoveryCategory(req.DiscoveryCategory) });
                project.DiscoveryCategory = req.DiscoveryCategory;
            }

            // Wave 1 item 6 — the press page's fields, every one optional. Bounded here, rendered
            // there; an empty field is a section the page omits.
            if (PressField(req.PressContactEmail, PressFieldMaxLength, out var pressContact)
                || PressField(req.PressPrice, PressFieldMaxLength, out var pressPrice)
                || PressField(req.PressEngine, PressFieldMaxLength, out var pressEngine)
                || PressField(req.PressGenre, PressFieldMaxLength, out var pressGenre)
                || PressField(req.PressFactsheetRows, PressFactsheetMaxLength, out var pressFactsheet))
                return Results.BadRequest(new { error = $"A press field is too long ({PressFieldMaxLength} characters maximum, {PressFactsheetMaxLength} for factsheet rows)" });
            project.PressContactEmail = pressContact;
            project.PressPrice = pressPrice;
            project.PressEngine = pressEngine;
            project.PressGenre = pressGenre;
            project.PressFactsheetRows = pressFactsheet;

            if (ShowcaseDomain.Normalize(req.CustomDomain) is var domain && domain.Rejected)
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseDomainInvalid });
            if (domain.Host is { } host)
            {
                if (host.EndsWith("." + (cfg[Consts.General.TenantHostCfg] ?? Consts.URLs.TenantHost), StringComparison.OrdinalIgnoreCase)
                    || host.Equals(cfg[Consts.General.TenantHostCfg] ?? Consts.URLs.TenantHost, StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { error = ErrorMessages.ShowcaseDomainIsOurs });
                if (await db.Projects.AnyAsync(p => p.CustomDomain == host && p.Id != id))
                    return Results.BadRequest(new { error = ErrorMessages.ShowcaseDomainTaken(host) });
            }
            project.CustomDomain = domain.Host;

            if (!req.Enabled)
            {
                project.ShowcaseSlug = null;
                await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    showcaseSlug = (string?)null,
                    url = (string?)null,
                    customDomain = project.CustomDomain,
                    discoveryCategory = project.DiscoveryCategory,
                    blocksJson = project.ShowcaseBlocksJson,
                });
            }

            var slug = SlugGenerator.Slugify(string.IsNullOrWhiteSpace(req.Slug) ? project.Name : req.Slug);
            if (slug.Length == 0)
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseSlugEmpty });
            if (await db.Projects.AnyAsync(p => p.ShowcaseSlug == slug && p.Id != id))
                return Results.BadRequest(new { error = ErrorMessages.ShowcaseSlugTaken(slug) });

            project.ShowcaseSlug = slug;
            await db.SaveChangesAsync();

            // Showcase slugs are per-owner like blog slugs, so the page only exists on this
            // owner's own host; an account with no host yet has a slug but no address.
            var blogHost = await BlogTenant.HostForOwnerAsync(db, cfg, project.OwnerId);
            return Results.Ok(new
            {
                showcaseSlug = slug,
                url = blogHost is null ? null : $"https://{blogHost}/showcase/{slug}",
                customDomain = project.CustomDomain,
                discoveryCategory = project.DiscoveryCategory,
                blocksJson = project.ShowcaseBlocksJson,
            });
        });

        group.MapPost("/{id:guid}/showcase/assist", async (
            Guid id, ShowcaseAssistRequest req, ClaimsPrincipal user, CedarDbContext db,
            IConfiguration cfg, IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory,
            AiJobService jobs, ProductAnalytics analytics) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == id && p.OwnerId == uid))
                return Results.NotFound();

            var text = (req.Text ?? "").Trim();
            if (text.Length == 0 || text.Length > ShowcaseLayouts.BodyMaxLength)
                return Results.BadRequest(new { error = $"Showcase text must be 1–{ShowcaseLayouts.BodyMaxLength} characters" });

            var kind = req.Kind switch
            {
                "polish" => AiEditKind.Polish,
                "shorten" => AiEditKind.Shorten,
                "ideas" => AiEditKind.Ideas,
                _ => (AiEditKind?)null,
            };
            if (kind is null) return Results.BadRequest(new { error = $"Unknown Showcase AI kind: {req.Kind}" });

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = ErrorMessages.AiEditProPlus }, statusCode: StatusCodes.Status403Forbidden);

            IAiEditProvider? provider;
            try { provider = AiEditProviderFactory.Create(cfg, httpFactory); }
            catch (AiEditException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is null)
                return Results.Json(new { error = ErrorMessages.AiEditNotConfigured }, statusCode: StatusCodes.Status501NotImplemented);

            if (await SubscriptionPlan.ChargeAiOrRefuseAsync(db, uid, CreditPacks.AiSmallCost, analytics, "showcase") is { } refusal)
                return refusal;

            var cedarJson = JsonSerializer.Serialize(new
            {
                type = "doc",
                content = new[]
                {
                    new { type = "paragraph", content = new[] { new { type = "text", text } } },
                },
            });
            var jobId = jobs.Start(uid, async ct =>
            {
                AiEditResult result;
                try { result = await provider.EditAsync("Showcase block", cedarJson, kind.Value, ct); }
                catch (AiEditException ex)
                {
                    return AiJobOutcome.Fail(ex.Message, StatusCodes.Status502BadGateway);
                }

                string suggestion;
                try { suggestion = string.Join("\n\n", CedarPlainText.Paragraphs(result.CedarJson)).Trim(); }
                catch (JsonException)
                {
                    return AiJobOutcome.Fail("AI returned invalid Showcase text", StatusCodes.Status502BadGateway);
                }
                if (suggestion.Length == 0 || suggestion.Length > ShowcaseLayouts.BodyMaxLength)
                    return AiJobOutcome.Fail("AI returned unusable Showcase text", StatusCodes.Status502BadGateway);

                return AiJobOutcome.Ok(new { suggestion });
            }, Consts.Anthropic.RequestTimeout,
            onFailure: async () =>
            {
                using var refundScope = scopeFactory.CreateTenantScope(uid);
                await SubscriptionPlan.RefundAiAsync(
                    refundScope.ServiceProvider.GetRequiredService<CedarDbContext>(), uid, CreditPacks.AiSmallCost);
            });

            return Results.Accepted(value: new { jobId });
        });

        // T-296/T-297 — what the public page did, for the owner who cannot see their own counters
        // any other way. Daily rows, so the answer is "how many on which day", never "who".
        group.MapGet("/{id:guid}/showcase/stats", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == id && p.OwnerId == uid)) return Results.NotFound();

            var since = DateTime.UtcNow.Date.AddDays(-ShowcaseStatsDays);
            var rows = await db.ShowcaseStatDailies
                .Where(s => s.ProjectId == id && s.OwnerId == uid && s.Day >= since)
                .ToListAsync();

            var followers = await db.ShowcaseFollowers
                .Where(f => f.ProjectId == id && f.OwnerId == uid)
                .Select(f => f.ConfirmedAt)
                .ToListAsync();

            return Results.Ok(new
            {
                days = ShowcaseStatsDays,
                views = rows.Where(r => r.Kind == ShowcaseStatKinds.View)
                    .OrderBy(r => r.Day)
                    .Select(r => new { day = r.Day, count = r.Count }),
                viewTotal = rows.Where(r => r.Kind == ShowcaseStatKinds.View).Sum(r => r.Count),
                // Grouped by the link's label rather than by day: "which store link works" is the
                // question a store link raises, and its answer is one number per label.
                clicks = rows.Where(r => r.Kind == ShowcaseStatKinds.LinkClick)
                    .GroupBy(r => r.Label)
                    .Select(g => new { label = g.Key, count = g.Sum(r => r.Count) })
                    .OrderByDescending(c => c.count),
                followerCount = followers.Count(c => c != null),
                pendingFollowerCount = followers.Count(c => c == null),
            });
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
            await db.Builds.Where(b => b.ProjectId == id && b.OwnerId == uid).ExecuteDeleteAsync();
            await db.Sprints.Where(sp => sp.ProjectId == id && sp.OwnerId == uid).ExecuteDeleteAsync();
            // The public page's rows go with the page. A follower kept past the project would be an
            // address subscribed to nothing, and its counters would answer about a page that is gone.
            await db.ShowcaseFollowers.Where(f => f.ProjectId == id && f.OwnerId == uid).ExecuteDeleteAsync();
            await db.ShowcaseStatDailies.Where(st => st.ProjectId == id && st.OwnerId == uid).ExecuteDeleteAsync();
            // Items go by project rather than by board: sweeping board by board would leave the items
            // of a board that was already gone, which is why CanvasItem carries ProjectId at all.
            await db.CanvasItems.Where(i => i.ProjectId == id && i.OwnerId == uid).ExecuteDeleteAsync();
            await db.CanvasBoards.Where(b => b.ProjectId == id && b.OwnerId == uid).ExecuteDeleteAsync();
            // A membership outliving its project would keep granting access to an id nothing answers for.
            await db.ProjectMembers.Where(m => m.ProjectId == id && m.OwnerId == uid).ExecuteDeleteAsync();

            db.Projects.Remove(project);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/documents", async (Guid id, CreateDocumentRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == id && p.OwnerId == uid)) return Results.NotFound();

            // T-331/T-355 — a preset decides the type AND the starter skeleton: its base type is what
            // publishability keys on (never a new stored string), and its headings are the body the
            // document is born with. Without a preset it is a bare document of the given type.
            var type = req.DocumentType ?? DocumentTypes.Post;
            string? cedarJson = null;
            if (req.PresetId is { } presetId)
            {
                var preset = await db.Presets.FirstOrDefaultAsync(p => p.Id == presetId && p.OwnerId == uid && p.Kind == PresetKinds.Document);
                if (preset is null) return Results.NotFound();
                var cfg = DocumentPresetConfig.Parse(preset.ConfigJson);
                type = cfg.BaseType;
                cedarJson = cfg.ToCedarJson();
            }
            if (!DocumentTypes.IsKnown(type))
                return Results.Json(new { error = ErrorMessages.UnknownDocumentType(type) }, statusCode: StatusCodes.Status400BadRequest);

            var draft = new Draft
            {
                OwnerId = uid,
                Title = string.IsNullOrWhiteSpace(req.Title) ? "Untitled" : req.Title.Trim(),
                DocumentType = type,
                ProjectId = id,
            };
            if (cedarJson is not null) draft.CedarJson = cedarJson;
            db.Drafts.Add(draft);
            await DraftRevisionService.RecordAsync(db, draft.Id, draft.PrimaryLanguage, draft.Title, draft.CedarJson);
            await db.SaveChangesAsync();
            await GlossaryUsage.SyncForDraftAsync(db, uid, draft.Id);
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

    /// <summary>Trims and null-blanks a press field; true means "too long" (the one refusal).</summary>
    private static bool PressField(string? raw, int maxLength, out string? value)
    {
        var trimmed = (raw ?? "").Trim();
        value = trimmed.Length == 0 ? null : trimmed;
        return trimmed.Length > maxLength;
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
