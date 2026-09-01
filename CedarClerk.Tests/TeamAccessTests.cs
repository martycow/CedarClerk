using CedarClerk.Core;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-358 — teams widen ProjectAccessResolver, which is the one place in the app where a widening
// would go unnoticed (ADR-217's own words about the per-project half). Every case below is a
// sentence of the rule teams add.
public class TeamAccessTests
{
    [Fact]
    public async Task A_team_member_reaches_a_project_handed_to_their_team()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var team = fx.Team(owner);
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate);
        fx.HandProjectToTeam(project, team);

        var access = await fx.AccessAsync(project, mate);

        Assert.False(access.IsOwner);
        Assert.Equal(ProjectRoles.Editor, access.Role);
        Assert.True(access.CanWrite);
        // Everything the member writes is stamped with the project's owner, never with themselves.
        Assert.Equal(owner, access.OwnerId);
    }

    [Fact]
    public async Task A_team_member_reaches_nothing_the_team_was_not_handed()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var shared = fx.Project(owner, "Shared");
        var mine = fx.Project(owner, "Mine");
        var team = fx.Team(owner);
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate);
        fx.HandProjectToTeam(shared, team);

        Assert.NotNull(await fx.TryAccessAsync(shared, mate));
        Assert.Null(await fx.TryAccessAsync(mine, mate));
    }

    [Fact]
    public async Task A_pending_team_invitation_grants_nothing()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var project = fx.Project(owner);
        var team = fx.Team(owner);
        // The row exists and names the address; AcceptedAt does not, and that is the whole test.
        fx.TeamMember(owner, team, "stranger@local.test", ProjectRoles.Editor, memberUserId: null);
        fx.HandProjectToTeam(project, team);

        Assert.Null(await fx.TryAccessAsync(project, stranger));
    }

    [Fact]
    public async Task A_restricted_member_keeps_access_and_loses_writing()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var team = fx.Team(owner);
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate,
            status: TeamMemberStatuses.Restricted);
        fx.HandProjectToTeam(project, team);

        var access = await fx.AccessAsync(project, mate);

        // Restriction is expressed as a read-only role rather than as a third role, so the stored
        // Editor is still there to come back to when the restriction is lifted.
        Assert.Equal(ProjectRoles.Viewer, access.Role);
        Assert.False(access.CanWrite);
    }

    [Fact]
    public async Task A_banned_member_resolves_to_nothing()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var team = fx.Team(owner);
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate,
            status: TeamMemberStatuses.Banned);
        fx.HandProjectToTeam(project, team);

        Assert.Null(await fx.TryAccessAsync(project, mate));
    }

    [Fact]
    public async Task A_per_project_invitation_decides_over_the_team()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var team = fx.Team(owner);
        // Editor on the team, viewer on this one project: the narrower grant is the deliberate one
        // and must not be widened by the broader one.
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, mate);
        fx.HandProjectToTeam(project, team);

        var access = await fx.AccessAsync(project, mate);

        Assert.Equal(ProjectRoles.Viewer, access.Role);
        Assert.False(access.CanWrite);
    }

    [Fact]
    public async Task Taking_the_team_away_takes_the_access_with_it()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var team = fx.Team(owner);
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate);
        fx.HandProjectToTeam(project, team);
        Assert.NotNull(await fx.TryAccessAsync(project, mate));

        fx.HandProjectToTeam(project, null);

        Assert.Null(await fx.TryAccessAsync(project, mate));
    }

    [Fact]
    public async Task An_archived_project_is_readable_through_a_team_and_not_writable()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner, archived: true);
        var team = fx.Team(owner);
        fx.TeamMember(owner, team, "mate@local.test", ProjectRoles.Editor, mate);
        fx.HandProjectToTeam(project, team);

        var access = await fx.AccessAsync(project, mate);

        Assert.True(access.Archived);
        Assert.False(access.CanWrite);
    }

    [Fact]
    public void The_status_rules_are_the_two_sentences_they_look_like()
    {
        Assert.True(TeamMemberStatuses.GrantsAccess(TeamMemberStatuses.Active));
        Assert.True(TeamMemberStatuses.GrantsAccess(TeamMemberStatuses.Restricted));
        Assert.False(TeamMemberStatuses.GrantsAccess(TeamMemberStatuses.Banned));

        Assert.True(TeamMemberStatuses.AllowsWrite(TeamMemberStatuses.Active));
        Assert.False(TeamMemberStatuses.AllowsWrite(TeamMemberStatuses.Restricted));

        Assert.Equal(ProjectRoles.Editor,
            TeamMemberStatuses.EffectiveRole(TeamMemberStatuses.Active, ProjectRoles.Editor));
        Assert.Equal(ProjectRoles.Viewer,
            TeamMemberStatuses.EffectiveRole(TeamMemberStatuses.Restricted, ProjectRoles.Editor));
        // An unknown status is not a licence: anything that is not Active is read-only.
        Assert.Equal(ProjectRoles.Viewer, TeamMemberStatuses.EffectiveRole("whatever", ProjectRoles.Editor));
    }
}
