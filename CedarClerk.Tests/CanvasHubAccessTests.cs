using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-301 / ADR-218 — the hub's access path, against the code it actually calls rather than a live
// socket. A hub invocation does not run the request pipeline, so nothing here may rely on a tenant
// having been set for it: every check below is the resolve-then-open-the-owner's-scope sequence.
public class CanvasHubAccessTests
{
    [Fact]
    public void The_group_name_is_derived_from_the_board_id_alone()
    {
        var board = Guid.Parse("bb350265-b88d-413d-a32b-1525daded50e");

        Assert.Equal("canvas:bb350265-b88d-413d-a32b-1525daded50e", CanvasHub.GroupOf(board));
        // Two clients that agree on the board agree on the group without agreeing on anything else
        // — no project, no owner, nothing a client could have lied about.
        Assert.Equal(CanvasHub.GroupOf(board), CanvasHub.GroupOf(Guid.Parse(board.ToString())));
        Assert.NotEqual(CanvasHub.GroupOf(board), CanvasHub.GroupOf(Guid.NewGuid()));
    }

    [Fact]
    public async Task Join_by_a_stranger_finds_nothing_to_resolve()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);

        await using var db = fx.Platform();
        var projectOfBoard = db.CanvasBoards.Single(b => b.Id == board).ProjectId;

        // Null is what the hub turns into HubException(UnknownBoard): the 404 discipline, over the
        // socket. A stranger is never told whether the board exists.
        Assert.Null(await ProjectAccessResolver.ResolveAsync(db, projectOfBoard, stranger));
    }

    [Fact]
    public async Task A_viewer_may_join_and_may_not_write()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, mate);
        var board = fx.Board(owner, project);

        var access = await fx.AccessAsync(project, mate);
        Assert.True(access.CanRead);
        Assert.False(access.CanWrite);
        Assert.Equal(ProjectRoles.Viewer, access.WireRole);

        // The refusal a viewer gets is a sentence about looking, not about existing — they can see
        // the board they were just refused, so "no such board" would deny what is on their screen.
        Assert.Contains("look", ErrorMessages.NoWriteAccessToProject, StringComparison.OrdinalIgnoreCase);

        // What the snapshot they do get holds is real: reading is not gated.
        await using var db = fx.As(access.OwnerId);
        var ownerAccess = await fx.AccessAsync(project, owner);
        await CanvasWrites.AddAsync(db, ownerAccess, board, owner, [CanvasFixture.Note(Guid.NewGuid())]);
        Assert.Single(await CanvasWrites.ItemsAsync(db, board));
    }

    [Fact]
    public async Task A_write_to_a_board_of_another_project_is_refused()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var shared = fx.Project(owner, "Shared");
        var other = fx.Project(owner, "Other");
        fx.Member(owner, shared, "mate@local.test", ProjectRoles.Editor, mate);
        var elsewhere = fx.Board(owner, other);

        var access = await fx.AccessAsync(shared, mate);

        await using var db = fx.As(access.OwnerId);
        var (error, _) = await CanvasWrites.AddAsync(db, access, elsewhere, mate, [CanvasFixture.Note(Guid.NewGuid())]);

        Assert.Equal(ErrorMessages.UnknownBoard, error);
        Assert.Empty(await CanvasWrites.ItemsAsync(db, elsewhere));
    }

    [Fact]
    public void A_peer_colour_is_stable_across_a_restart()
    {
        // Not string.GetHashCode: that is randomised per process, so everybody would change colour
        // whenever the service restarted, and the test could not say what the answer should be.
        var first = CanvasHub.ColorOf("some-user-id");
        Assert.Equal(first, CanvasHub.ColorOf("some-user-id"));
        Assert.InRange(first, 0, Consts.Canvas.PresenceColors - 1);
        Assert.All(new[] { "a", "b", "c", "d", "" },
            id => Assert.InRange(CanvasHub.ColorOf(id), 0, Consts.Canvas.PresenceColors - 1));
    }
}
