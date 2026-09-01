using System.Security.Claims;
using System.Security.Cryptography;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Email;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// Collaborators on a project, T-301 (ADR-217). Reading the list is open to anyone already on the
// project; inviting, changing a role and removing are the owner's alone — membership is not a
// power a member can hand on.
//
// The invitation token is the credential, and the address is only where the link was posted: an
// account with a second address, or a forwarded mail, is a person who was invited, and comparing
// the accepting account's address to the invited one would refuse them for no gain (the same model
// as ShowcaseFollower's tokens).
public static class ProjectMemberEndpoints
{
    public record InviteRequest(string Email, string? Role);
    public record RoleRequest(string Role);

    private const int EmailMaxLength = 200;

    public static void MapProjectMemberEndpoints(this WebApplication app)
    {
        var members = app.MapGroup("/api/projects/{projectId:guid}/members").RequireAuthorization();

        members.MapGet("/", async (Guid projectId, ClaimsPrincipal user, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, uid, ct);
            if (access is null) return Results.NotFound();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            return Results.Ok(await DescribeAllAsync(db, access, uid, ct));
        });

        members.MapPost("/", async (Guid projectId, InviteRequest req, ClaimsPrincipal user,
            IServiceScopeFactory scopes, IConfiguration cfg, ResendEmailProvider mailer, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, uid, ct);
            if (access is null) return Results.NotFound();
            if (!access.IsOwner) return CanvasEndpoints.Refused();

            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length is 0 or > EmailMaxLength || !email.Contains('@'))
                return CanvasEndpoints.Bad(ErrorMessages.InvalidEmail);

            var role = string.IsNullOrWhiteSpace(req.Role) ? ProjectRoles.Editor : req.Role.Trim();
            if (!ProjectRoles.IsKnown(role)) return CanvasEndpoints.Bad(ErrorMessages.UnknownProjectRole(role));

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var owner = await db.Users.Where(u => u.Id == access.OwnerId)
                .Select(u => new { u.Email, u.AuthorDisplayName, u.TenantUsername })
                .FirstOrDefaultAsync(ct);
            if (string.Equals(owner?.Email, email, StringComparison.OrdinalIgnoreCase))
                return CanvasEndpoints.Bad(ErrorMessages.CannotInviteYourself);

            if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.Email == email, ct))
                return Results.Json(new { error = ErrorMessages.ProjectMemberAlreadyInvited },
                    statusCode: StatusCodes.Status409Conflict);

            if (await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId, ct) >= Consts.Canvas.MembersPerProject)
                return CanvasEndpoints.Bad(ErrorMessages.ProjectMemberLimitReached(Consts.Canvas.MembersPerProject));

            var member = new ProjectMember
            {
                OwnerId = access.OwnerId,
                ProjectId = projectId,
                Email = email,
                Role = role,
                InviteToken = NewToken(),
                InvitedByUserId = uid,
            };
            db.ProjectMembers.Add(member);
            await db.SaveChangesAsync(ct);

            var projectName = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";
            var link = InviteUrl(cfg, member.InviteToken!);
            await SendInviteAsync(mailer, email, projectName, NameOf(owner?.AuthorDisplayName, owner?.TenantUsername, owner?.Email), link);

            // The link comes back whether or not mail is configured: an owner who can copy it is
            // not blocked by a missing API key, and SendAsync answers false rather than throwing.
            return Results.Created($"/api/projects/{projectId}/members/{member.Id}", new
            {
                member = Describe(member, null, uid),
                inviteUrl = link,
            });
        });

        // "Send again" on a pending invitation. Creating a second one collides with the unique
        // index by design (one row per address per project), so the resend is its own route rather
        // than a POST that quietly means two different things.
        members.MapPost("/{memberId:guid}/resend", async (Guid projectId, Guid memberId, ClaimsPrincipal user,
            IServiceScopeFactory scopes, IConfiguration cfg, ResendEmailProvider mailer, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, uid, ct);
            if (access is null) return Results.NotFound();
            if (!access.IsOwner) return CanvasEndpoints.Refused();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId, ct);
            if (member is null || member.AcceptedAt != null) return Results.NotFound();

            // The same token, so a mail that did arrive keeps working — a resend is usually an
            // answer to "it went to spam", not to a leak.
            member.InviteToken ??= NewToken();
            member.InvitedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var owner = await db.Users.Where(u => u.Id == access.OwnerId)
                .Select(u => new { u.Email, u.AuthorDisplayName, u.TenantUsername }).FirstOrDefaultAsync(ct);
            var projectName = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";
            var link = InviteUrl(cfg, member.InviteToken!);
            await SendInviteAsync(mailer, member.Email, projectName, NameOf(owner?.AuthorDisplayName, owner?.TenantUsername, owner?.Email), link);

            return Results.Ok(new { member = Describe(member, null, uid), inviteUrl = link });
        });

        members.MapPut("/{memberId:guid}", async (Guid projectId, Guid memberId, RoleRequest req,
            ClaimsPrincipal user, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, uid, ct);
            if (access is null) return Results.NotFound();
            if (!access.IsOwner) return CanvasEndpoints.Refused();

            var role = (req.Role ?? "").Trim();
            if (!ProjectRoles.IsKnown(role)) return CanvasEndpoints.Bad(ErrorMessages.UnknownProjectRole(role));

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId, ct);
            if (member is null) return Results.NotFound();

            member.Role = role;
            await db.SaveChangesAsync(ct);

            var account = member.MemberUserId is null ? null : await db.Users
                .Where(u => u.Id == member.MemberUserId)
                .Select(u => new Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
                .FirstOrDefaultAsync(ct);

            // A demotion to viewer takes hold on the member's next hub call, which re-resolves;
            // nothing they already sent needs undoing.
            return Results.Ok(Describe(member, account, uid));
        });

        members.MapDelete("/{memberId:guid}", async (Guid projectId, Guid memberId, ClaimsPrincipal user,
            IServiceScopeFactory scopes, IHubContext<CanvasHub> hub, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, uid, ct);
            if (access is null) return Results.NotFound();
            if (!access.IsOwner) return CanvasEndpoints.Refused();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId, ct);
            if (member is null) return Results.NotFound();

            var removedUserId = member.MemberUserId;
            db.ProjectMembers.Remove(member);
            await db.SaveChangesAsync(ct);

            // Their images stay on the boards — deleting the bytes would blank a board somebody
            // else is still using. What goes is the access, and it goes now rather than on their
            // next write: a connection already open would otherwise keep watching.
            if (removedUserId is not null)
            {
                var boardIds = await db.CanvasBoards.Where(b => b.ProjectId == projectId).Select(b => b.Id).ToListAsync(ct);
                await CanvasHub.RevokeAsync(hub, removedUserId, boardIds);
            }

            return Results.NoContent();
        });

        // T-301 — what THIS caller is to this project, in one word. The shell draws a wall of
        // tools for whatever project is open, and until it could ask this it drew the owner's wall
        // for everyone: a member saw seven hooks and every one of them answered 404. Deliberately
        // its own tiny route rather than a field on GET /api/projects/{id}, which is owner-only and
        // 404s for the very callers that need the answer.
        app.MapGet("/api/projects/{projectId:guid}/access", async (Guid projectId, ClaimsPrincipal user,
            IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, uid, ct);
            return access is null
                ? Results.NotFound()
                : Results.Ok(new { role = access.WireRole, canWrite = access.CanWrite, access.Archived });
        }).RequireAuthorization();

        var invites = app.MapGroup("/api/project-invites").RequireAuthorization();

        invites.MapGet("/{token}", async (string token, ClaimsPrincipal user, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var invite = await FindInviteAsync(db, token, ct);
            if (invite is null) return InviteGone();

            var project = await db.Projects.Where(p => p.Id == invite.ProjectId)
                .Select(p => new { p.Name, p.OwnerId }).FirstOrDefaultAsync(ct);
            if (project is null) return InviteGone();

            var inviter = await db.Users.Where(u => u.Id == invite.InvitedByUserId)
                .Select(u => new Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
                .FirstOrDefaultAsync(ct);

            var alreadyMember = project.OwnerId == uid || await db.ProjectMembers
                .AnyAsync(m => m.ProjectId == invite.ProjectId && m.MemberUserId == uid && m.AcceptedAt != null, ct);

            return Results.Ok(new
            {
                projectName = project.Name,
                invitedBy = NameOf(inviter),
                role = invite.Role,
                alreadyMember,
            });
        });

        invites.MapPost("/{token}/accept", async (string token, ClaimsPrincipal user,
            IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var invite = await FindInviteAsync(db, token, ct);
            if (invite is null) return InviteGone();

            var ownerId = await db.Projects.Where(p => p.Id == invite.ProjectId).Select(p => p.OwnerId).FirstOrDefaultAsync(ct);
            if (ownerId is null) return InviteGone();

            // The owner following their own link, or a second link to somebody already on the
            // project: both are answered with what they already have. The pending row is left for
            // the owner to revoke — accepting it would break the one-row-per-person index.
            if (ownerId == uid) return Results.Ok(new { projectId = invite.ProjectId, role = ProjectAccess.OwnerRole });

            var mine = await db.ProjectMembers
                .FirstOrDefaultAsync(m => m.ProjectId == invite.ProjectId && m.MemberUserId == uid && m.AcceptedAt != null, ct);
            if (mine is not null) return Results.Ok(new { projectId = mine.ProjectId, role = mine.Role });

            if (invite.MemberUserId is not null)
                return Results.Json(new { error = ErrorMessages.InviteAlreadyAccepted },
                    statusCode: StatusCodes.Status409Conflict);

            invite.MemberUserId = uid;
            invite.AcceptedAt = DateTime.UtcNow;
            invite.InviteToken = null;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { projectId = invite.ProjectId, role = invite.Role });
        });

        // Deliberately not extra rows on GET /api/projects: an owner's list is their own work, and
        // a shared project is a different thing with a different set of doors open on it.
        app.MapGet("/api/projects/shared", async (ClaimsPrincipal user, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = CanvasEndpoints.UserId(user);
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var memberships = await db.ProjectMembers
                .Where(m => m.MemberUserId == uid && m.AcceptedAt != null)
                .Select(m => new { m.ProjectId, m.Role })
                .ToListAsync(ct);

            // T-358 — a project reached through a team is shared with this account just as surely
            // as one invited to directly, and the screen that lists "what am I in" has to say so or
            // the team's whole point is invisible. A per-project invitation wins where both exist,
            // which is the same precedence ProjectAccessResolver applies.
            var teamRoles = await db.TeamMembers
                .Where(m => m.MemberUserId == uid && m.AcceptedAt != null && m.Status != TeamMemberStatuses.Banned)
                .Select(m => new { m.TeamId, m.Role, m.Status })
                .ToListAsync(ct);
            if (teamRoles.Count > 0)
            {
                var teamIds = teamRoles.Select(t => t.TeamId).ToList();
                var teamProjects = await db.Projects
                    .Where(p => p.TeamId != null && teamIds.Contains(p.TeamId!.Value))
                    .Select(p => new { p.Id, TeamId = p.TeamId!.Value })
                    .ToListAsync(ct);

                var direct = memberships.Select(m => m.ProjectId).ToHashSet();
                memberships = memberships.Concat(teamProjects
                        .Where(p => !direct.Contains(p.Id))
                        .Select(p => teamRoles.First(t => t.TeamId == p.TeamId) is var t
                            ? new { ProjectId = p.Id, Role = TeamMemberStatuses.EffectiveRole(t.Status, t.Role) }
                            : null!))
                    .ToList();
            }

            if (memberships.Count == 0) return Results.Ok(Array.Empty<object>());

            var ids = memberships.Select(m => m.ProjectId).ToList();

            var projects = await db.Projects
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.OwnerId, p.CreatedAt })
                .ToListAsync(ct);

            var boards = await db.CanvasBoards
                .Where(b => ids.Contains(b.ProjectId))
                .GroupBy(b => b.ProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count(), LastActivity = g.Max(b => b.UpdatedAt) })
                .ToDictionaryAsync(g => g.ProjectId, g => g, ct);

            var ownerIds = projects.Select(p => p.OwnerId).Distinct().ToList();
            var owners = await db.Users.Where(u => ownerIds.Contains(u.Id))
                .Select(u => new Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
                .ToDictionaryAsync(p => p.Id, ct);

            return Results.Ok(projects.Select(p => new
            {
                p.Id,
                p.Name,
                ownerName = NameOf(owners.GetValueOrDefault(p.OwnerId)),
                role = memberships.First(m => m.ProjectId == p.Id).Role,
                boardCount = boards.GetValueOrDefault(p.Id)?.Count ?? 0,
                lastActivityAt = boards.GetValueOrDefault(p.Id)?.LastActivity ?? p.CreatedAt,
            }));
        }).RequireAuthorization();
    }

    /// <summary>The owner first, then the people they invited — pending ones last, newest first.</summary>
    private static async Task<List<object>> DescribeAllAsync(
        CedarDbContext db, ProjectAccess access, string callerId, CancellationToken ct)
    {
        var rows = await db.ProjectMembers.Where(m => m.ProjectId == access.ProjectId).ToListAsync(ct);

        var userIds = rows.Where(m => m.MemberUserId != null).Select(m => m.MemberUserId!).Append(access.OwnerId).ToList();
        var people = await db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
            .ToDictionaryAsync(p => p.Id, ct);

        var owner = people.GetValueOrDefault(access.OwnerId);
        var list = new List<object>
        {
            // Synthesised, with a null id: the owner has no ProjectMember row and must not look
            // like one the client could edit or remove.
            new
            {
                id = (Guid?)null,
                userId = access.OwnerId,
                email = owner?.Email ?? "",
                role = ProjectAccess.OwnerRole,
                pending = false,
                invitedAt = (DateTime?)null,
                acceptedAt = (DateTime?)null,
                lastSeenAt = (DateTime?)null,
                isYou = access.OwnerId == callerId,
            },
        };

        list.AddRange(rows
            .OrderBy(m => m.AcceptedAt == null)
            .ThenByDescending(m => m.AcceptedAt ?? m.InvitedAt)
            .Select(m => Describe(m, m.MemberUserId is null ? null : people.GetValueOrDefault(m.MemberUserId), callerId)));

        return list;
    }

    private static object Describe(ProjectMember member, Person? account, string callerId) => new
    {
        id = (Guid?)member.Id,
        userId = member.MemberUserId,
        // A pending row shows where the invitation went; an accepted one shows the account that
        // took it, which is not always the same address (the token is the credential, not the mail).
        email = account?.Email ?? member.Email,
        role = member.Role,
        pending = member.AcceptedAt == null,
        invitedAt = (DateTime?)member.InvitedAt,
        acceptedAt = member.AcceptedAt,
        lastSeenAt = member.LastSeenAt,
        isYou = member.MemberUserId == callerId,
    };

    private static Task<ProjectMember?> FindInviteAsync(CedarDbContext db, string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<ProjectMember?>(null)
            : db.ProjectMembers.FirstOrDefaultAsync(m => m.InviteToken == token, ct);

    private static IResult InviteGone() =>
        Results.Json(new { error = ErrorMessages.InviteNotFound }, statusCode: StatusCodes.Status404NotFound);

    private static string InviteUrl(IConfiguration cfg, string token) =>
        $"{cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost}/invite/{token}";

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(Consts.Canvas.InviteTokenBytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public sealed record Person(string Id, string? AuthorDisplayName, string? TenantUsername, string? Email);

    /// <summary>
    /// What to call somebody on a shared board. One rule, used by the members list, the invitation
    /// preview and the hub's presence — three different names for one person would read as three
    /// people.
    /// </summary>
    public static string NameOf(Person? person) => NameOf(person?.AuthorDisplayName, person?.TenantUsername, person?.Email);

    public static string NameOf(string? authorDisplayName, string? tenantUsername, string? email)
    {
        if (!string.IsNullOrWhiteSpace(authorDisplayName)) return authorDisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(tenantUsername)) return tenantUsername;

        var address = email ?? "";
        var at = address.IndexOf('@');
        return at > 0 ? address[..at] : address;
    }

    private static async Task SendInviteAsync(
        ResendEmailProvider mailer, string email, string projectName, string inviterName, string link)
    {
        await mailer.SendAsync(email, EmailTexts.ProjectInviteSubject(projectName),
            EmailTexts.ProjectInviteBody(projectName, inviterName, link));
    }
}
