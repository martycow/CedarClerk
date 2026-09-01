using System.Security.Claims;
using System.Security.Cryptography;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Email;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Teams (T-358). The owner's own screen: make a team, invite people to it, set what each may do,
// hold one read-only for a while, or ban one outright. A team is then handed to a project, and its
// people reach every project it holds — which is the whole reason it exists, since inviting the
// same five people to every new project one at a time is the thing this replaces.
//
// Everything here is the owner's alone. There is no "team admin" who can invite: the moment a
// member can hand the team on, the team owns people rather than a person owning the team, and every
// question that keys on one owner (billing, quotas, the media gate) stops having one answer.
public static class TeamEndpoints
{
    public record SaveTeamRequest(string Name);
    public record InviteRequest(string Email, string? Role);
    public record RoleRequest(string Role);
    public record StatusRequest(string Status, string? Note);
    public record ProjectTeamRequest(Guid? TeamId);

    private const int EmailMaxLength = 200;

    public static void MapTeamEndpoints(this WebApplication app)
    {
        var teams = app.MapGroup("/api/teams").RequireAuthorization();

        teams.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db, CancellationToken ct) =>
        {
            var uid = Uid(user);
            var rows = await db.Teams.Where(t => t.OwnerId == uid).OrderBy(t => t.CreatedAt).ToListAsync(ct);
            var ids = rows.Select(t => t.Id).ToList();

            // Two counts per team, because "five people" and "five people who can actually get in"
            // are different numbers once anyone is banned or still pending.
            var members = await db.TeamMembers
                .Where(m => ids.Contains(m.TeamId))
                .Select(m => new { m.TeamId, m.AcceptedAt, m.Status })
                .ToListAsync(ct);
            var projects = await db.Projects
                .Where(p => p.TeamId != null && ids.Contains(p.TeamId!.Value))
                .GroupBy(p => p.TeamId!.Value)
                .Select(g => new { TeamId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TeamId, x => x.Count, ct);

            return Results.Ok(rows.Select(t => new
            {
                t.Id,
                t.Name,
                t.CreatedAt,
                memberCount = members.Count(m => m.TeamId == t.Id),
                activeCount = members.Count(m => m.TeamId == t.Id
                    && m.AcceptedAt != null && m.Status == TeamMemberStatuses.Active),
                projectCount = projects.GetValueOrDefault(t.Id),
            }));
        });

        teams.MapPost("/", async (SaveTeamRequest req, ClaimsPrincipal user, CedarDbContext db, CancellationToken ct) =>
        {
            if (BadName(req.Name) is { } error) return error;
            var uid = Uid(user);
            if (await db.Teams.CountAsync(t => t.OwnerId == uid, ct) >= Consts.Teams.TeamsPerOwner)
                return Bad(ErrorMessages.TeamLimitReached(Consts.Teams.TeamsPerOwner));

            var team = new Team { OwnerId = uid, Name = req.Name.Trim() };
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/teams/{team.Id}", new { team.Id, team.Name, team.CreatedAt });
        });

        teams.MapPut("/{teamId:guid}", async (Guid teamId, SaveTeamRequest req, ClaimsPrincipal user,
            CedarDbContext db, CancellationToken ct) =>
        {
            if (BadName(req.Name) is { } error) return error;
            var team = await OwnTeamAsync(db, Uid(user), teamId, ct);
            if (team is null) return Results.NotFound();

            team.Name = req.Name.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { team.Id, team.Name, team.CreatedAt });
        });

        // Deleting a team does not delete its projects — it takes their team away. The projects
        // stay whole and private to their owner, which is the only outcome that cannot lose work.
        teams.MapDelete("/{teamId:guid}", async (Guid teamId, ClaimsPrincipal user, CedarDbContext db,
            CancellationToken ct) =>
        {
            var uid = Uid(user);
            var team = await OwnTeamAsync(db, uid, teamId, ct);
            if (team is null) return Results.NotFound();

            await db.Projects.Where(p => p.TeamId == teamId).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.TeamId, (Guid?)null), ct);
            await db.TeamMembers.Where(m => m.TeamId == teamId).ExecuteDeleteAsync(ct);
            db.Teams.Remove(team);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        teams.MapGet("/{teamId:guid}/members", async (Guid teamId, ClaimsPrincipal user,
            CedarDbContext db, CancellationToken ct) =>
        {
            var uid = Uid(user);
            if (await OwnTeamAsync(db, uid, teamId, ct) is null) return Results.NotFound();
            return Results.Ok(await DescribeAllAsync(db, teamId, uid, ct));
        });

        teams.MapPost("/{teamId:guid}/members", async (Guid teamId, InviteRequest req, ClaimsPrincipal user,
            CedarDbContext db, IConfiguration cfg, ResendEmailProvider mailer, CancellationToken ct) =>
        {
            var uid = Uid(user);
            var team = await OwnTeamAsync(db, uid, teamId, ct);
            if (team is null) return Results.NotFound();

            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length is 0 or > EmailMaxLength || !email.Contains('@'))
                return Bad(ErrorMessages.InvalidEmail);

            var role = string.IsNullOrWhiteSpace(req.Role) ? ProjectRoles.Editor : req.Role.Trim();
            if (!ProjectRoles.IsKnown(role)) return Bad(ErrorMessages.UnknownProjectRole(role));

            var ownerEmail = await db.Users.Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (string.Equals(ownerEmail, email, StringComparison.OrdinalIgnoreCase))
                return Bad(ErrorMessages.CannotInviteYourself);

            // A banned row answers as a ban rather than as "already invited": the two are the same
            // collision to the index and completely different news to the person pressing the button.
            var existing = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.Email == email, ct);
            if (existing is not null)
                return Results.Json(
                    new { error = existing.Status == TeamMemberStatuses.Banned
                        ? ErrorMessages.TeamMemberBanned
                        : ErrorMessages.TeamMemberAlreadyInvited },
                    statusCode: StatusCodes.Status409Conflict);

            if (await db.TeamMembers.CountAsync(m => m.TeamId == teamId, ct) >= Consts.Teams.MembersPerTeam)
                return Bad(ErrorMessages.TeamMemberLimitReached(Consts.Teams.MembersPerTeam));

            var member = new TeamMember
            {
                OwnerId = uid,
                TeamId = teamId,
                Email = email,
                Role = role,
                InviteToken = NewToken(),
                InvitedByUserId = uid,
            };
            db.TeamMembers.Add(member);
            await db.SaveChangesAsync(ct);

            var link = await SendInviteAsync(db, cfg, mailer, uid, team.Name, member, ct);
            // The link comes back whether or not mail is configured, exactly as a project invitation
            // does: an owner who can copy it is not blocked by a missing API key.
            return Results.Created($"/api/teams/{teamId}/members/{member.Id}", new
            {
                member = Describe(member, null, uid),
                inviteUrl = link,
            });
        });

        teams.MapPost("/{teamId:guid}/members/{memberId:guid}/resend", async (Guid teamId, Guid memberId,
            ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, ResendEmailProvider mailer,
            CancellationToken ct) =>
        {
            var uid = Uid(user);
            var team = await OwnTeamAsync(db, uid, teamId, ct);
            if (team is null) return Results.NotFound();

            var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId, ct);
            if (member is null || member.AcceptedAt != null) return Results.NotFound();

            // The same token, so a mail that did arrive keeps working — a resend usually answers
            // "it went to spam", not a leak.
            member.InviteToken ??= NewToken();
            member.InvitedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var link = await SendInviteAsync(db, cfg, mailer, uid, team.Name, member, ct);
            return Results.Ok(new { member = Describe(member, null, uid), inviteUrl = link });
        });

        teams.MapPut("/{teamId:guid}/members/{memberId:guid}", async (Guid teamId, Guid memberId,
            RoleRequest req, ClaimsPrincipal user, CedarDbContext db, CancellationToken ct) =>
        {
            var uid = Uid(user);
            if (await OwnTeamAsync(db, uid, teamId, ct) is null) return Results.NotFound();

            var role = (req.Role ?? "").Trim();
            if (!ProjectRoles.IsKnown(role)) return Bad(ErrorMessages.UnknownProjectRole(role));

            var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId, ct);
            if (member is null) return Results.NotFound();

            // The role is set even while restricted: it is what comes back when the restriction is
            // lifted, so an owner can fix somebody's permissions before letting them act again.
            member.Role = role;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Describe(member, await AccountAsync(db, member.MemberUserId, ct), uid));
        });

        // Restrict and ban, and the way back from both. Its own route rather than a field on the
        // role PUT: "what may they do" and "may they do anything" are different decisions, and a
        // client that meant to change one must not be able to change the other by omission.
        teams.MapPut("/{teamId:guid}/members/{memberId:guid}/status", async (Guid teamId, Guid memberId,
            StatusRequest req, ClaimsPrincipal user, CedarDbContext db, CancellationToken ct) =>
        {
            var uid = Uid(user);
            if (await OwnTeamAsync(db, uid, teamId, ct) is null) return Results.NotFound();

            var status = (req.Status ?? "").Trim();
            if (!TeamMemberStatuses.IsKnown(status)) return Bad(ErrorMessages.UnknownTeamMemberStatus(status));

            var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId, ct);
            if (member is null) return Results.NotFound();

            member.Status = status;
            member.StatusChangedAt = DateTime.UtcNow;
            var note = req.Note?.Trim();
            member.StatusNote = string.IsNullOrEmpty(note)
                ? null
                : note[..Math.Min(note.Length, Consts.Teams.StatusNoteMax)];

            // A ban has to survive the invitation too: a pending token left alive would let the
            // banned address walk back in through its own mail.
            if (status == TeamMemberStatuses.Banned) member.InviteToken = null;

            await db.SaveChangesAsync(ct);
            return Results.Ok(Describe(member, await AccountAsync(db, member.MemberUserId, ct), uid));
        });

        teams.MapDelete("/{teamId:guid}/members/{memberId:guid}", async (Guid teamId, Guid memberId,
            ClaimsPrincipal user, CedarDbContext db, CancellationToken ct) =>
        {
            var uid = Uid(user);
            if (await OwnTeamAsync(db, uid, teamId, ct) is null) return Results.NotFound();

            var deleted = await db.TeamMembers
                .Where(m => m.Id == memberId && m.TeamId == teamId)
                .ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        // Which team a project belongs to. On the project rather than on the team, because it is a
        // property of the project and the project's owner is the only one who may set it.
        app.MapPut("/api/projects/{projectId:guid}/team", async (Guid projectId, ProjectTeamRequest req,
            ClaimsPrincipal user, CedarDbContext db, CancellationToken ct) =>
        {
            var uid = Uid(user);
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == uid, ct);
            if (project is null) return Results.NotFound();

            if (req.TeamId is { } teamId && await OwnTeamAsync(db, uid, teamId, ct) is null)
                return Results.NotFound();

            project.TeamId = req.TeamId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { project.Id, project.TeamId });
        }).RequireAuthorization();

        var invites = app.MapGroup("/api/team-invites").RequireAuthorization();

        invites.MapGet("/{token}", async (string token, ClaimsPrincipal user, IServiceScopeFactory scopes,
            CancellationToken ct) =>
        {
            var uid = Uid(user);
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var invite = await FindInviteAsync(db, token, ct);
            if (invite is null) return InviteGone();

            var team = await db.Teams.Where(t => t.Id == invite.TeamId)
                .Select(t => new { t.Name, t.OwnerId }).FirstOrDefaultAsync(ct);
            if (team is null) return InviteGone();

            var inviter = await AccountAsync(db, invite.InvitedByUserId, ct);
            var alreadyMember = team.OwnerId == uid || await db.TeamMembers
                .AnyAsync(m => m.TeamId == invite.TeamId && m.MemberUserId == uid && m.AcceptedAt != null, ct);

            return Results.Ok(new
            {
                teamName = team.Name,
                invitedBy = ProjectMemberEndpoints.NameOf(inviter),
                role = invite.Role,
                alreadyMember,
            });
        });

        invites.MapPost("/{token}/accept", async (string token, ClaimsPrincipal user,
            IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var uid = Uid(user);
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var invite = await FindInviteAsync(db, token, ct);
            if (invite is null) return InviteGone();

            var ownerId = await db.Teams.Where(t => t.Id == invite.TeamId).Select(t => t.OwnerId).FirstOrDefaultAsync(ct);
            if (ownerId is null) return InviteGone();

            // The owner following their own link, or a second link to somebody already on the team:
            // both are answered with what they already have. The pending row is left for the owner
            // to revoke — accepting it would break the one-row-per-person index.
            if (ownerId == uid) return Results.Ok(new { teamId = invite.TeamId, role = ProjectAccess.OwnerRole });

            var mine = await db.TeamMembers
                .FirstOrDefaultAsync(m => m.TeamId == invite.TeamId && m.MemberUserId == uid && m.AcceptedAt != null, ct);
            if (mine is not null) return Results.Ok(new { teamId = mine.TeamId, role = mine.Role });

            if (invite.MemberUserId is not null)
                return Results.Json(new { error = ErrorMessages.InviteAlreadyAccepted },
                    statusCode: StatusCodes.Status409Conflict);

            invite.MemberUserId = uid;
            invite.AcceptedAt = DateTime.UtcNow;
            invite.InviteToken = null;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { teamId = invite.TeamId, role = invite.Role });
        });

        // The other side of a team: the teams this account was invited INTO, which is the only way a
        // member learns a team exists at all. Deliberately not folded into GET /api/teams, whose
        // rows are the caller's own and carry owner-only counts.
        app.MapGet("/api/teams/joined", async (ClaimsPrincipal user, IServiceScopeFactory scopes,
            CancellationToken ct) =>
        {
            var uid = Uid(user);
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var memberships = await db.TeamMembers
                .Where(m => m.MemberUserId == uid && m.AcceptedAt != null && m.Status != TeamMemberStatuses.Banned)
                .Select(m => new { m.TeamId, m.Role, m.Status })
                .ToListAsync(ct);
            if (memberships.Count == 0) return Results.Ok(Array.Empty<object>());

            var ids = memberships.Select(m => m.TeamId).ToList();
            var rows = await db.Teams.Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Name, t.OwnerId }).ToListAsync(ct);

            var ownerIds = rows.Select(t => t.OwnerId).Distinct().ToList();
            var owners = await db.Users.Where(u => ownerIds.Contains(u.Id))
                .Select(u => new ProjectMemberEndpoints.Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
                .ToDictionaryAsync(p => p.Id, ct);

            return Results.Ok(rows.Select(t => new
            {
                t.Id,
                t.Name,
                ownerName = ProjectMemberEndpoints.NameOf(owners.GetValueOrDefault(t.OwnerId)),
                role = memberships.First(m => m.TeamId == t.Id).Role,
                status = memberships.First(m => m.TeamId == t.Id).Status,
            }));
        }).RequireAuthorization();
    }

    /// <summary>
    /// T-304 — a live team invitation stands in for the registration invite code, so a stranger who
    /// was invited can make the account the invitation needs. Read in a platform scope: the caller
    /// has no account yet, so there is no tenant to read it in.
    /// </summary>
    public static async Task<bool> IsLiveInviteTokenAsync(CedarDbContext platformDb, string? token, CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(token)
        && (await platformDb.TeamMembers.AnyAsync(m => m.InviteToken == token && m.AcceptedAt == null, ct)
            || await platformDb.ProjectMembers.AnyAsync(m => m.InviteToken == token && m.AcceptedAt == null, ct));

    private static string Uid(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static Task<Team?> OwnTeamAsync(CedarDbContext db, string uid, Guid teamId, CancellationToken ct) =>
        db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && t.OwnerId == uid, ct);

    private static IResult Bad(string error) => Results.BadRequest(new { error });

    private static IResult? BadName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > Consts.Teams.NameMax
            ? Bad(ErrorMessages.TeamNameInvalid(Consts.Teams.NameMax))
            : null;

    private static IResult InviteGone() =>
        Results.Json(new { error = ErrorMessages.InviteNotFound }, statusCode: StatusCodes.Status404NotFound);

    private static Task<TeamMember?> FindInviteAsync(CedarDbContext db, string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<TeamMember?>(null)
            : db.TeamMembers.FirstOrDefaultAsync(m => m.InviteToken == token, ct);

    private static Task<ProjectMemberEndpoints.Person?> AccountAsync(CedarDbContext db, string? userId, CancellationToken ct) =>
        userId is null
            ? Task.FromResult<ProjectMemberEndpoints.Person?>(null)
            : db.Users.Where(u => u.Id == userId)
                .Select(u => new ProjectMemberEndpoints.Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
                .FirstOrDefaultAsync(ct)!;

    /// <summary>Accepted first, newest first; pending and banned rows after them.</summary>
    private static async Task<List<object>> DescribeAllAsync(
        CedarDbContext db, Guid teamId, string callerId, CancellationToken ct)
    {
        var rows = await db.TeamMembers.Where(m => m.TeamId == teamId).ToListAsync(ct);
        var userIds = rows.Where(m => m.MemberUserId != null).Select(m => m.MemberUserId!).ToList();
        var people = await db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new ProjectMemberEndpoints.Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
            .ToDictionaryAsync(p => p.Id, ct);

        return rows
            .OrderBy(m => m.Status == TeamMemberStatuses.Banned)
            .ThenBy(m => m.AcceptedAt == null)
            .ThenByDescending(m => m.AcceptedAt ?? m.InvitedAt)
            .Select(m => Describe(m, m.MemberUserId is null ? null : people.GetValueOrDefault(m.MemberUserId), callerId))
            .ToList();
    }

    private static object Describe(TeamMember member, ProjectMemberEndpoints.Person? account, string callerId) => new
    {
        id = member.Id,
        userId = member.MemberUserId,
        // A pending row shows where the invitation went; an accepted one shows the account that
        // took it, which is not always the same address (the token is the credential, not the mail).
        email = account?.Email ?? member.Email,
        name = ProjectMemberEndpoints.NameOf(account),
        role = member.Role,
        status = member.Status,
        statusNote = member.StatusNote,
        pending = member.AcceptedAt == null,
        invitedAt = member.InvitedAt,
        acceptedAt = member.AcceptedAt,
        isYou = member.MemberUserId == callerId,
    };

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(Consts.Canvas.InviteTokenBytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static async Task<string> SendInviteAsync(CedarDbContext db, IConfiguration cfg,
        ResendEmailProvider mailer, string ownerId, string teamName, TeamMember member, CancellationToken ct)
    {
        var owner = await AccountAsync(db, ownerId, ct);
        var link = $"{cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost}/team-invite/{member.InviteToken}";
        await mailer.SendAsync(member.Email, EmailTexts.TeamInviteSubject(teamName),
            EmailTexts.TeamInviteBody(teamName, ProjectMemberEndpoints.NameOf(owner), link));
        return link;
    }
}
