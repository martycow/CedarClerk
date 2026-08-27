using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// T-301 / ADR-217 — invitations. The token is the credential and the address is only where the link
// was posted, so what is pinned here is the token's lifecycle and the two unique indexes that keep
// a project from collecting duplicate rows for one person.
public class ProjectMemberTests
{
    [Fact]
    public void Two_invitations_to_one_address_collide_on_the_unique_index()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, memberUserId: null, token: "one");

        // A second invite to the same address is a resend, not a second row — and the schema says
        // so rather than leaving it to whichever endpoint remembers.
        var again = Assert.Throws<DbUpdateException>(() =>
            fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, memberUserId: null, token: "two"));
        Assert.NotNull(again);
    }

    [Fact]
    public void One_person_holds_one_row_per_project_once_accepted()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);

        Assert.Throws<DbUpdateException>(() =>
            fx.Member(owner, project, "second-address@local.test", ProjectRoles.Viewer, mate));
    }

    [Fact]
    public void Two_pending_invitations_do_not_collide_on_the_filtered_index()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);

        // Both have a null MemberUserId, which the filtered index deliberately ignores: most rows
        // on a project are pending, and they are told apart by address, not by user.
        fx.Member(owner, project, "a@local.test", ProjectRoles.Editor, memberUserId: null, token: "a");
        fx.Member(owner, project, "b@local.test", ProjectRoles.Editor, memberUserId: null, token: "b");

        using var db = fx.Platform();
        Assert.Equal(2, db.ProjectMembers.Count(m => m.ProjectId == project));
    }

    [Fact]
    public async Task Accepting_clears_the_token_and_stamps_the_user()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var membership = fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor,
            memberUserId: null, token: "spend-me");

        await using var db = fx.Platform();
        var row = db.ProjectMembers.Single(m => m.Id == membership);
        row.MemberUserId = mate;
        row.AcceptedAt = DateTime.UtcNow;
        row.InviteToken = null;
        await db.SaveChangesAsync();

        Assert.Null(row.InviteToken);
        Assert.NotNull(row.AcceptedAt);
        // The pair is written together and read together: non-null AcceptedAt means a real account.
        Assert.Equal(mate, row.MemberUserId);
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, project, "somebody-else"));
        Assert.NotNull(await ProjectAccessResolver.ResolveAsync(db, project, mate));
    }

    [Fact]
    public void An_invite_token_is_unique_while_it_is_unspent()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var one = fx.Project(owner, "One");
        var two = fx.Project(owner, "Two");
        fx.Member(owner, one, "a@local.test", ProjectRoles.Editor, memberUserId: null, token: "same");

        Assert.Throws<DbUpdateException>(() =>
            fx.Member(owner, two, "a@local.test", ProjectRoles.Editor, memberUserId: null, token: "same"));
    }

    [Fact]
    public void Spent_tokens_are_all_null_and_do_not_collide()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var first = fx.User("first");
        var second = fx.User("second");
        var project = fx.Project(owner);

        // The unique index on InviteToken is filtered for exactly this: every accepted row has a
        // null token, and a schema that treated those as equal would let one accept block the next.
        fx.Member(owner, project, "a@local.test", ProjectRoles.Editor, first);
        fx.Member(owner, project, "b@local.test", ProjectRoles.Viewer, second);

        using var db = fx.Platform();
        Assert.Equal(2, db.ProjectMembers.Count(m => m.ProjectId == project && m.InviteToken == null));
    }

    [Fact]
    public void The_owner_never_holds_a_membership_row_of_their_own_project()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);

        using var db = fx.Platform();
        Assert.Empty(db.ProjectMembers.Where(m => m.ProjectId == project && m.MemberUserId == owner));
        // Which is why the members list synthesises the owner's row with a null id: it must not
        // look like something the role picker or the Remove button can act on.
        Assert.Equal(ProjectAccess.OwnerRole, "owner");
    }

    [Fact]
    public void The_cap_is_a_number_the_endpoint_can_state()
    {
        Assert.Equal(10, Consts.Canvas.MembersPerProject);
        Assert.Equal(20, Consts.Canvas.BoardsPerProject);
    }
}
