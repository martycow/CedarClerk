using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

/// <summary>
/// Who may act on a project, resolved once per request or hub call (ADR-217). The owner is told
/// apart from a member by identity rather than by a role string — a project has no "owner" row, so
/// no row can be edited into ownership.
/// </summary>
public sealed record ProjectAccess(Guid ProjectId, string OwnerId, string Role, bool IsOwner, bool Archived)
{
    /// <summary>Resolving at all means readable; an unreadable project comes back as null.</summary>
    public bool CanRead => true;

    public bool CanWrite => !Archived && (IsOwner || ProjectRoles.CanWrite(Role));

    /// <summary>What the wire calls this caller: "owner" is not a role, but the client renders it as one.</summary>
    public string WireRole => IsOwner ? OwnerRole : Role;

    public const string OwnerRole = "owner";
}

/// <summary>
/// The one place that reads across owners for the canvas. A project id is exactly what the
/// caller's own tenant cannot resolve — the global filter hides a project the caller collaborates
/// on — so membership is looked up in a platform scope, and everything after it runs in the
/// project owner's tenant scope, where the ordinary filter is right again.
/// </summary>
public static class ProjectAccessResolver
{
    public static async Task<ProjectAccess?> ResolveAsync(
        IServiceScopeFactory scopes, Guid projectId, string userId, CancellationToken ct = default)
    {
        using var scope = scopes.CreatePlatformScope();
        return await ResolveAsync(scope.ServiceProvider.GetRequiredService<CedarDbContext>(), projectId, userId, ct);
    }

    /// <summary>
    /// The same resolution against a context that is already unfiltered. Both entry points exist so
    /// a caller holding a platform scope does not open a second one inside it.
    /// </summary>
    public static async Task<ProjectAccess?> ResolveAsync(
        CedarDbContext platformDb, Guid projectId, string userId, CancellationToken ct = default)
    {
        var project = await platformDb.Projects
            .Where(p => p.Id == projectId)
            .Select(p => new { p.OwnerId, p.ArchivedAt })
            .FirstOrDefaultAsync(ct);
        if (project is null) return null;

        var archived = project.ArchivedAt != null;
        if (project.OwnerId == userId)
            return new ProjectAccess(projectId, project.OwnerId, "", true, archived);

        // Pending invitations grant nothing: the row exists, AcceptedAt does not.
        var role = await platformDb.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.MemberUserId == userId && m.AcceptedAt != null)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);

        return role is null ? null : new ProjectAccess(projectId, project.OwnerId, role, false, archived);
    }

    /// <summary>
    /// Access for a board id, which is what every canvas route and every hub call actually carries.
    /// The board decides the project; the client's opinion about which project a board belongs to
    /// is never consulted.
    /// </summary>
    public static async Task<ProjectAccess?> ResolveBoardAsync(
        IServiceScopeFactory scopes, Guid boardId, string userId, CancellationToken ct = default)
    {
        using var scope = scopes.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var projectId = await db.CanvasBoards
            .Where(b => b.Id == boardId)
            .Select(b => (Guid?)b.ProjectId)
            .FirstOrDefaultAsync(ct);

        return projectId is null ? null : await ResolveAsync(db, projectId.Value, userId, ct);
    }

    /// <summary>Opens the owner's tenant scope for the work that follows. Caller disposes.</summary>
    public static IServiceScope OpenOwnerScope(IServiceScopeFactory scopes, ProjectAccess access) =>
        scopes.CreateTenantScope(access.OwnerId);
}
