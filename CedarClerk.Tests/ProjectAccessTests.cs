using CedarClerk.Core;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-301 / ADR-217 — the whole access rule, which is one paragraph of prose and the single place a
// widening would go unnoticed. Every case below is a sentence of it.
public class ProjectAccessTests
{
    [Fact]
    public async Task The_owner_resolves_as_owner_with_no_member_row()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);

        var access = await fx.AccessAsync(project, owner);

        Assert.True(access.IsOwner);
        Assert.Equal("", access.Role);
        Assert.Equal(ProjectAccess.OwnerRole, access.WireRole);
        Assert.True(access.CanWrite);

        await using var db = fx.Platform();
        Assert.Empty(db.ProjectMembers.Where(m => m.ProjectId == project));
    }

    [Fact]
    public async Task An_accepted_editor_resolves_with_write_access()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);

        var access = await fx.AccessAsync(project, mate);

        Assert.False(access.IsOwner);
        Assert.Equal(ProjectRoles.Editor, access.Role);
        Assert.True(access.CanWrite);
        // Every row the member goes on to write is stamped with the owner, never with themselves.
        Assert.Equal(owner, access.OwnerId);
    }

    [Fact]
    public async Task An_accepted_viewer_resolves_readable_but_not_writable()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, mate);

        var access = await fx.AccessAsync(project, mate);

        Assert.True(access.CanRead);
        Assert.False(access.CanWrite);
    }

    [Fact]
    public async Task A_pending_invitation_grants_nothing()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        // The row exists; AcceptedAt does not, and MemberUserId is still null.
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, memberUserId: null, token: "t");

        await using var db = fx.Platform();
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, project, mate));
    }

    [Fact]
    public async Task A_stranger_resolves_to_null()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var project = fx.Project(owner);

        await using var db = fx.Platform();
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, project, stranger));
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, Guid.NewGuid(), owner));
    }

    [Fact]
    public async Task A_removed_member_resolves_to_null_immediately()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var membership = fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);

        await using var db = fx.Platform();
        Assert.NotNull(await ProjectAccessResolver.ResolveAsync(db, project, mate));

        db.ProjectMembers.Remove(db.ProjectMembers.Single(m => m.Id == membership));
        await db.SaveChangesAsync();

        // No cache to expire: every persisting call re-resolves, so removal takes effect on the next one.
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, project, mate));
    }

    [Fact]
    public async Task An_archived_project_is_readable_but_never_writable_even_for_the_owner()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner, archived: true);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);

        var asOwner = await fx.AccessAsync(project, owner);
        Assert.True(asOwner.Archived);
        Assert.True(asOwner.CanRead);
        Assert.False(asOwner.CanWrite);

        var asEditor = await fx.AccessAsync(project, mate);
        Assert.False(asEditor.CanWrite);
    }

    [Fact]
    public async Task Access_is_resolved_per_project_not_per_account()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var shared = fx.Project(owner, "Shared");
        var private_ = fx.Project(owner, "Private");
        fx.Member(owner, shared, "mate@local.test", ProjectRoles.Editor, mate);

        await using var db = fx.Platform();
        Assert.NotNull(await ProjectAccessResolver.ResolveAsync(db, shared, mate));
        // Being on one of an owner's projects is not being on the owner's projects.
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, private_, mate));
    }

    [Fact]
    public async Task A_board_resolves_through_its_own_project_not_a_named_one()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var shared = fx.Project(owner, "Shared");
        var private_ = fx.Project(owner, "Private");
        fx.Member(owner, shared, "mate@local.test", ProjectRoles.Editor, mate);
        var hidden = fx.Board(owner, private_);

        // The board decides the project; a member of the owner's other project must not reach it,
        // even though the tenant filter would happily hand both to the owner's scope.
        await using var db = fx.Platform();
        var projectOfBoard = db.CanvasBoards.Single(b => b.Id == hidden).ProjectId;
        Assert.Equal(private_, projectOfBoard);
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, projectOfBoard, mate));
    }
}
