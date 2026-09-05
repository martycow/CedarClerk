using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.AspNetCore.SignalR;

namespace CedarClerk.Tests.Realtime;

// T-307 / ADR-218 — the hub's own methods, driven the way SignalR drives them: a caller context,
// the group manager and the client proxies are the fakes beside this file, and the database
// behind Join/Leave is the same scope factory shape production hands the hub.
public class CanvasHubTests
{
    private static CanvasHub Hub(CanvasFixture fx, string userId, out FakeGroupManager groups, out FakeHubCallerClients clients)
    {
        groups = new FakeGroupManager();
        clients = new FakeHubCallerClients();
        return new CanvasHub(fx.Scopes())
        {
            // Unique per hub: presence is a process-wide table keyed by connection id.
            Context = new FakeHubCallerContext("conn-" + Guid.NewGuid().ToString("N")[..8], userId),
            Groups = groups,
            Clients = clients,
        };
    }

    [Fact]
    public async Task Join_hands_back_the_snapshot_joins_the_group_and_tells_the_others()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner, "Cedar Quest");
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        await using (var db = fx.As(owner))
            await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(Guid.NewGuid())]);

        using var hub = Hub(fx, owner, out var groups, out var clients);
        var snapshot = await hub.Join(board);

        Assert.Equal(board, snapshot.Board.Id);
        Assert.Equal("Cedar Quest", snapshot.ProjectName);
        Assert.Single(snapshot.Items);
        Assert.Equal(ProjectAccess.OwnerRole, snapshot.Role);
        Assert.True(snapshot.CanWrite);
        Assert.Empty(snapshot.Peers);

        Assert.Equal([(hub.Context.ConnectionId, CanvasHub.GroupOf(board))], groups.Added);
        var joined = Assert.Single(clients.Of("peerJoined"));
        Assert.Equal("others-in-group:" + CanvasHub.GroupOf(board), joined.To);
        Assert.Equal(hub.Context.ConnectionId, Assert.IsType<PresenceDto>(joined.Args[0]).ConnectionId);
    }

    [Fact]
    public async Task A_viewer_joins_with_read_only_in_the_snapshot_and_sees_who_is_there()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, mate);
        var board = fx.Board(owner, project);

        using var first = Hub(fx, owner, out _, out _);
        await first.Join(board);

        using var second = Hub(fx, mate, out _, out _);
        var snapshot = await second.Join(board);

        Assert.Equal(ProjectRoles.Viewer, snapshot.Role);
        Assert.False(snapshot.CanWrite);
        Assert.False(snapshot.Board.CanWrite);
        var peer = Assert.Single(snapshot.Peers);
        Assert.Equal(first.Context.ConnectionId, peer.ConnectionId);
        Assert.Equal(owner, peer.UserId);
        Assert.Equal(ProjectAccess.OwnerRole, peer.Role);

        await first.Leave(board);
        await second.Leave(board);
    }

    [Fact]
    public async Task Join_by_a_stranger_is_refused_as_an_unknown_board()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);

        using var hub = Hub(fx, stranger, out var groups, out var clients);
        var refused = await Assert.ThrowsAsync<HubException>(() => hub.Join(board));

        // The 404 discipline over the socket: a stranger is never told the board exists.
        Assert.Equal(ErrorMessages.UnknownBoard, refused.Message);
        Assert.Empty(groups.Added);
        Assert.Empty(clients.Messages);
    }

    [Fact]
    public async Task Leave_drops_the_presence_leaves_the_group_and_is_idempotent()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);

        using var hub = Hub(fx, owner, out var groups, out var clients);
        await hub.Join(board);

        await hub.Leave(board);

        Assert.Equal([(hub.Context.ConnectionId, CanvasHub.GroupOf(board))], groups.Removed);
        var left = Assert.Single(clients.Of("peerLeft"));
        Assert.Equal("group:" + CanvasHub.GroupOf(board), left.To);
        Assert.Equal([hub.Context.ConnectionId], left.Args);

        // Gone is gone: a second Leave has nothing to remove and says nothing to anyone.
        await hub.Leave(board);
        Assert.Single(groups.Removed);
        Assert.Single(clients.Of("peerLeft"));

        // And the connection is no longer live for the board, so presence traffic is refused.
        var refused = await Assert.ThrowsAsync<HubException>(() => hub.Cursor(board, 1, 1));
        Assert.Equal(ErrorMessages.UnknownBoard, refused.Message);
    }

    [Fact]
    public async Task Leave_for_a_board_the_connection_is_not_on_changes_nothing()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var elsewhere = fx.Board(owner, project, "Elsewhere");

        using var hub = Hub(fx, owner, out var groups, out var clients);
        await hub.Join(board);

        // The connection's own board decides what is left, not the argument: acting on the id the
        // client sent would drop the presence record while the subscription stayed — an invisible
        // listener nothing could evict.
        await hub.Leave(elsewhere);

        Assert.Empty(groups.Removed);
        Assert.Empty(clients.Of("peerLeft"));

        await hub.Cursor(board, 3, 4);
        var cursor = Assert.Single(clients.Of("cursor"));
        Assert.Equal("others-in-group:" + CanvasHub.GroupOf(board), cursor.To);

        await hub.Leave(board);
    }

    [Fact]
    public async Task Joining_a_second_board_leaves_the_first()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var first = fx.Board(owner, project, "First");
        var second = fx.Board(owner, project, "Second");

        using var hub = Hub(fx, owner, out var groups, out var clients);
        await hub.Join(first);
        await hub.Join(second);

        Assert.Equal([(hub.Context.ConnectionId, CanvasHub.GroupOf(first))], groups.Removed);
        var left = Assert.Single(clients.Of("peerLeft"));
        Assert.Equal("group:" + CanvasHub.GroupOf(first), left.To);
        Assert.Equal(
            [CanvasHub.GroupOf(first), CanvasHub.GroupOf(second)],
            groups.Added.Select(a => a.Group));

        await hub.Leave(second);
    }
}
