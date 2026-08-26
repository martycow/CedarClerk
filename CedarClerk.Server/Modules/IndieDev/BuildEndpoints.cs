using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// Indie-gamedev module, T-126 (ADR-112) — build and version records.
//
// A build knows nothing about git: no repository tags, no CI, no artefacts. It is a record the
// author keeps of which version exists and what went into it, and the one thing it does beyond
// recording is turn that into a real changelog document.
public static class BuildEndpoints
{
    public record SaveBuildRequest(string Version, string? Notes, DateTime? ReleasedAt,
        bool IsPublic = false, string? DownloadUrl = null);
    public record ChangelogRequest(string? Title, string? Language);

    private const int VersionMaxLength = 40;
    private const int NotesMaxLength = 2000;

    public static void MapBuildEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/builds").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();

            var builds = await db.Builds
                .Where(b => b.ProjectId == projectId && b.OwnerId == uid)
                .ToListAsync();

            var tasks = await db.GameTasks
                .Where(t => t.ProjectId == projectId && t.OwnerId == uid && t.ArchivedAt == null && t.BuildId != null)
                .Select(t => new { t.BuildId, t.Status })
                .ToListAsync();

            var byBuild = tasks.GroupBy(t => t.BuildId!.Value)
                .ToDictionary(g => g.Key, g => (Total: g.Count(), Done: g.Count(t => t.Status == TaskStatuses.Done)));

            var links = await ProjectLinks.LinkedIdsForManyAsync(
                db, uid, LinkTargets.Build, builds.Select(b => b.Id).ToList());
            var labels = await TaskEndpoints.ResolveLabelsAsync(db, uid, links.Values.SelectMany(v => v));

            return Results.Ok(builds
                // Unreleased first — what is being worked towards matters more than what shipped —
                // then the most recent release. A version string sorts wrong ("0.10" before "0.9"),
                // so the date decides and never the text.
                .OrderBy(b => b.ReleasedAt.HasValue)
                .ThenByDescending(b => b.ReleasedAt)
                .ThenByDescending(b => b.CreatedAt)
                .Select(b => Describe(b, byBuild.GetValueOrDefault(b.Id), links, labels)));
        });

        group.MapPost("/", async (Guid projectId, SaveBuildRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OwnerId == uid)) return Results.NotFound();
            if (Invalid(req) is { } bad) return bad;

            var version = req.Version.Trim();
            if (await db.Builds.AnyAsync(b => b.ProjectId == projectId && b.Version == version))
                return Results.BadRequest(new { error = ErrorMessages.BuildVersionTaken(version) });

            var build = new Build
            {
                OwnerId = uid,
                ProjectId = projectId,
                Version = version,
                Notes = req.Notes?.Trim() ?? "",
                ReleasedAt = req.ReleasedAt,
                IsPublic = req.IsPublic,
                DownloadUrl = Download(req),
            };

            db.Builds.Add(build);
            await db.SaveChangesAsync();
            return Results.Ok(Describe(build, null, NoLinks, NoLabels));
        });

        var single = app.MapGroup("/api/builds/{id:guid}").RequireAuthorization();

        single.MapPut("/", async (Guid id, SaveBuildRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var build = await db.Builds.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == uid);
            if (build is null) return Results.NotFound();
            if (Invalid(req) is { } bad) return bad;

            var version = req.Version.Trim();
            if (await db.Builds.AnyAsync(b => b.ProjectId == build.ProjectId && b.Version == version && b.Id != id))
                return Results.BadRequest(new { error = ErrorMessages.BuildVersionTaken(version) });

            build.Version = version;
            build.Notes = req.Notes?.Trim() ?? "";
            build.ReleasedAt = req.ReleasedAt;
            build.IsPublic = req.IsPublic;
            build.DownloadUrl = Download(req);
            await db.SaveChangesAsync();

            return Results.Ok(Describe(build, null, NoLinks, NoLabels));
        });

        single.MapDelete("/", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var build = await db.Builds.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == uid);
            if (build is null) return Results.NotFound();

            // The tasks and documents survive it, exactly as with a project or a sprint: deleting
            // the record of a version is not a request to delete the work that went into it.
            await db.GameTasks.Where(t => t.BuildId == id && t.OwnerId == uid)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuildId, t => (Guid?)null));
            await ProjectLinks.RemoveAllForAsync(db, uid, LinkTargets.Build, id);

            db.Builds.Remove(build);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ADR-112 — the changelog is produced as a **document**, not as a blob of text to copy.
        // It then lives an ordinary document's life: edited, translated, published, versioned.
        // Handing back a string would be building a generator whose output has nowhere to go.
        single.MapPost("/changelog", async (
            Guid id, ChangelogRequest? req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var build = await db.Builds.FirstOrDefaultAsync(b => b.Id == id && b.OwnerId == uid);
            if (build is null) return Results.NotFound();

            var done = await db.GameTasks
                .Where(t => t.BuildId == id && t.OwnerId == uid
                            && t.ArchivedAt == null && t.Status == TaskStatuses.Done)
                .OrderBy(t => t.CompletedAt ?? t.UpdatedAt)
                .Select(t => t.Title)
                .ToListAsync();

            var draft = new Draft
            {
                OwnerId = uid,
                ProjectId = build.ProjectId,
                DocumentType = DocumentTypes.Changelog,
                Title = string.IsNullOrWhiteSpace(req?.Title) ? build.Version : req.Title.Trim(),
                CedarJson = ChangelogJson(build, done),
            };
            if (!string.IsNullOrWhiteSpace(req?.Language)) draft.PrimaryLanguage = req.Language;

            db.Drafts.Add(draft);
            await DraftRevisionService.RecordAsync(db, draft.Id, draft.PrimaryLanguage, draft.Title, draft.CedarJson);
            await db.SaveChangesAsync();

            // Linked, so the build and its changelog find each other afterwards from either side.
            await ProjectLinks.AddAsync(db, uid, build.ProjectId, LinkTargets.Build, build.Id, LinkTargets.Document, draft.Id);

            return Results.Ok(new { documentId = draft.Id, draft.Title, taskCount = done.Count });
        });
    }

    /// <summary>
    /// The changelog's body: the build's notes as a paragraph, then one bullet per finished task.
    /// A build with nothing finished still produces a document — an empty changelog the author
    /// fills in is more use than a refusal, and "nothing shipped in this version" is a thing that
    /// happens on the day a version is cut.
    /// </summary>
    private static string ChangelogJson(Build build, List<string> taskTitles)
    {
        var content = new JsonArray
        {
            DocJson.Heading(build.Version),
        };

        if (!string.IsNullOrWhiteSpace(build.Notes))
            content.Add(DocJson.Paragraph(build.Notes));

        if (taskTitles.Count > 0)
            content.Add(DocJson.BulletList(taskTitles));
        else
            content.Add(DocJson.Paragraph(""));

        return DocJson.Doc(content);
    }

    private static readonly Dictionary<Guid, List<(string Type, Guid Id)>> NoLinks = [];
    private static readonly Dictionary<(string Type, Guid Id), string> NoLabels = [];

    private static object Describe(
        Build b, (int Total, int Done)? tasks,
        IReadOnlyDictionary<Guid, List<(string Type, Guid Id)>> links,
        IReadOnlyDictionary<(string Type, Guid Id), string> labels) => new
    {
        b.Id,
        b.ProjectId,
        b.Version,
        b.Notes,
        b.ReleasedAt,
        b.CreatedAt,
        b.IsPublic,
        b.DownloadUrl,
        released = b.ReleasedAt.HasValue,
        taskCount = tasks?.Total ?? 0,
        doneCount = tasks?.Done ?? 0,
        documents = links.GetValueOrDefault(b.Id, [])
            .Where(l => l.Type == LinkTargets.Document)
            .Select(l => new { id = l.Id, title = labels.GetValueOrDefault(l, "") })
            .OrderBy(d => d.title)
            .ToList(),
    };

    private static IResult? Invalid(SaveBuildRequest req)
    {
        var version = (req.Version ?? "").Trim();
        if (version.Length is 0 or > VersionMaxLength)
            return Results.BadRequest(new { error = ErrorMessages.BuildVersionLength(VersionMaxLength) });
        if ((req.Notes ?? "").Trim().Length > NotesMaxLength)
            return Results.BadRequest(new { error = ErrorMessages.BuildNotesLength(NotesMaxLength) });
        // T-299 — the showcase only offers a build that has somewhere to be got from, so a public
        // build without a working link is refused here rather than rendered as a dead row.
        var url = (req.DownloadUrl ?? "").Trim();
        if (url.Length > Consts.Showcase.DownloadUrlMaxLength || (url.Length > 0 && !IsHttpUrl(url)))
            return Results.BadRequest(new { error = ErrorMessages.BuildDownloadUrlInvalid });
        if (req.IsPublic && url.Length == 0)
            return Results.BadRequest(new { error = ErrorMessages.BuildPublicNeedsUrl });
        return null;
    }

    private static string? Download(SaveBuildRequest req) =>
        (req.DownloadUrl ?? "").Trim() is { Length: > 0 } url ? url : null;

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
