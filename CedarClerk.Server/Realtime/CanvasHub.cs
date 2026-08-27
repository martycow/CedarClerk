using System.Collections.Concurrent;
using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

/// <summary>
/// The live half of a reference board (ADR-218). Everything an item can become goes through here;
/// REST owns boards, members and the read snapshot.
///
/// Three things shape the design:
/// <list type="bullet">
/// <item>A hub invocation does not run the request pipeline, so the injected scope has no tenant
/// and every call opens its own — a platform scope to answer "may this caller touch this board",
/// then the project owner's scope for the work itself.</item>
/// <item>Presence lives in this process and nowhere else. That is correct for one instance behind
/// one tunnel; a second instance needs a Redis backplane before this holds.</item>
/// <item>Ordering is the server's. SignalR delivers one connection's invocations in order, but two
/// connections have no order between them — so the server accepts both and stamps a Version, and
/// a client drops an echo older than what it already shows.</item>
/// </list>
/// </summary>
[Authorize]
public sealed class CanvasHub(IServiceScopeFactory scopes) : Hub
{
    public static string GroupOf(Guid boardId) => $"canvas:{boardId:D}";

    private static readonly ConcurrentDictionary<string, ConnectionState> Connections = new();

    /// <summary>
    /// How long a cursor or a drag frame may ride on the membership check the last call made.
    /// Re-resolving on every one of them would put a database read behind every pointer move —
    /// twenty a second per user, on a 1 vCPU droplet — and a cursor position discloses nothing.
    /// Anything that persists re-resolves unconditionally, and a removed member is also evicted
    /// the moment the owner removes them, so this window is what a stale *cursor* costs, not a
    /// stale write.
    /// </summary>
    private static readonly TimeSpan TransientAccessTtl = TimeSpan.FromSeconds(10);

    private sealed record ConnectionState(string UserId, Guid BoardId, string Role, string DisplayName, int ColorIndex)
    {
        public ProjectAccess? Access { get; set; }
        public DateTime CheckedAt { get; set; }
    }

    #region Joining and leaving

    public async Task<CanvasSnapshot> Join(Guid boardId)
    {
        var userId = UserId;
        var ct = Context.ConnectionAborted;
        var access = await ResolveAsync(boardId, write: false);

        using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var board = await CanvasWrites.BoardAsync(db, access, boardId, ct)
                    ?? throw new HubException(ErrorMessages.UnknownBoard);
        var items = await CanvasWrites.ItemsAsync(db, boardId, ct);
        var projectName = await db.Projects.Where(p => p.Id == access.ProjectId)
            .Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";
        var name = await DisplayNameAsync(db, userId, ct);

        // Cosmetic, and the members list is the only reader — a failure to stamp it must not cost
        // somebody their board.
        if (!access.IsOwner)
        {
            await db.ProjectMembers
                .Where(m => m.ProjectId == access.ProjectId && m.MemberUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastSeenAt, DateTime.UtcNow), ct);
        }

        // One connection, one board: joining a second one leaves the first, so a peer list never
        // shows somebody who has already navigated away.
        await LeaveCurrentAsync(boardId);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(boardId), ct);
        var state = new ConnectionState(userId, boardId, access.WireRole, name, ColorOf(userId))
        {
            Access = access,
            CheckedAt = DateTime.UtcNow,
        };
        Connections[Context.ConnectionId] = state;

        var peers = Connections
            .Where(pair => pair.Value.BoardId == boardId && pair.Key != Context.ConnectionId)
            .Select(pair => Describe(pair.Key, pair.Value))
            .ToArray();

        await Clients.OthersInGroup(GroupOf(boardId)).SendAsync("peerJoined", Describe(Context.ConnectionId, state), ct);

        return new CanvasSnapshot(
            CanvasMapping.Describe(board, items.Count, access.CanWrite), projectName,
            items.Select(CanvasMapping.Describe).ToArray(), access.WireRole, access.CanWrite, peers);
    }

    /// <summary>
    /// The connection's own board decides what is left, not the argument. Acting on the id the
    /// client sent let a <c>Leave</c> for some other board drop the presence record while the
    /// connection stayed subscribed to the real one — an invisible listener, and one
    /// <see cref="RevokeAsync"/> could no longer find to evict.
    /// </summary>
    public async Task Leave(Guid boardId)
    {
        if (!Connections.TryGetValue(Context.ConnectionId, out var state) || state.BoardId != boardId) return;

        Connections.TryRemove(Context.ConnectionId, out _);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(state.BoardId));
        await Clients.Group(GroupOf(state.BoardId)).SendAsync("peerLeft", Context.ConnectionId);
    }

    /// <summary>
    /// Nothing is persisted on the way out. The last <c>UpdateItems</c> is already the truth, and a
    /// drag interrupted by a dropped tunnel simply never happened — the item stays where the last
    /// pointer-up put it, which is the only position everyone agreed on.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Connections.TryRemove(Context.ConnectionId, out var state))
            await Clients.Group(GroupOf(state.BoardId)).SendAsync("peerLeft", Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    #endregion

    #region Items

    public async Task<CanvasItemDto[]> AddItems(Guid boardId, CanvasItemInput[] items)
    {
        var access = await ResolveAsync(boardId, write: true);
        using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var (error, added) = await CanvasWrites.AddAsync(
            db, access, boardId, UserId, items ?? [], Context.ConnectionAborted);
        if (error is not null) throw new HubException(error);

        // Including the sender: it needs the server's version for the row it drew optimistically.
        if (added.Length > 0)
            await Clients.Group(GroupOf(boardId)).SendAsync("itemsAdded", added, Context.ConnectionAborted);

        Refresh(access);
        return added;
    }

    public async Task UpdateItems(Guid boardId, CanvasItemPatch[] patches)
    {
        var access = await ResolveAsync(boardId, write: true);
        using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var (error, changed) = await CanvasWrites.UpdateAsync(
            db, access, boardId, UserId, patches ?? [], Context.ConnectionAborted);
        if (error is not null) throw new HubException(error);

        if (changed.Length > 0)
            await Clients.Group(GroupOf(boardId)).SendAsync("itemsChanged", changed, Context.ConnectionAborted);

        Refresh(access);
    }

    /// <summary>
    /// The frame-by-frame half of a drag: broadcast, never written. Persisting every frame would be
    /// roughly twenty writes a second per dragging user against SQLite; the pointer-up sends one
    /// <see cref="UpdateItems"/> and that is what the board is made of. A peer that vanishes
    /// mid-drag leaves no trace to clean up — its ghosts go with its <c>peerLeft</c>.
    /// </summary>
    public async Task DragItems(Guid boardId, CanvasGeometry[] items)
    {
        var state = await RequireLiveAsync(boardId, write: true);
        if (items is not { Length: > 0 } || items.Length > CanvasWrites.MaxItemsPerCall) return;

        await Clients.OthersInGroup(GroupOf(state.BoardId))
            .SendAsync("itemsDragging", Context.ConnectionId, items, Context.ConnectionAborted);
    }

    public async Task DeleteItems(Guid boardId, Guid[] itemIds)
    {
        var access = await ResolveAsync(boardId, write: true);
        using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var (error, deleted) = await CanvasWrites.DeleteAsync(
            db, access, boardId, UserId, itemIds ?? [], Context.ConnectionAborted);
        if (error is not null) throw new HubException(error);

        if (deleted.Length > 0)
            await Clients.Group(GroupOf(boardId)).SendAsync("itemsDeleted", deleted, Context.ConnectionAborted);

        Refresh(access);
    }

    public async Task BringToFront(Guid boardId, Guid[] itemIds)
    {
        var access = await ResolveAsync(boardId, write: true);
        using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var (error, changed) = await CanvasWrites.BringToFrontAsync(
            db, access, boardId, UserId, itemIds ?? [], Context.ConnectionAborted);
        if (error is not null) throw new HubException(error);

        if (changed.Length > 0)
            await Clients.Group(GroupOf(boardId)).SendAsync("itemsChanged", changed, Context.ConnectionAborted);

        Refresh(access);
    }

    #endregion

    #region Presence

    public async Task Cursor(Guid boardId, double x, double y)
    {
        var state = await RequireLiveAsync(boardId, write: false);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        await Clients.OthersInGroup(GroupOf(state.BoardId))
            .SendAsync("cursor", Context.ConnectionId, x, y, Context.ConnectionAborted);
    }

    public async Task Select(Guid boardId, Guid[] itemIds)
    {
        var state = await RequireLiveAsync(boardId, write: false);

        await Clients.OthersInGroup(GroupOf(state.BoardId))
            .SendAsync("selection", Context.ConnectionId, itemIds ?? [], Context.ConnectionAborted);
    }

    #endregion

    #region What the REST side needs from the live connections

    /// <summary>
    /// A board's rename or background change, told to everyone looking at it. Sent per connection
    /// rather than to the group because the summary carries <c>canWrite</c>, and one payload for
    /// the group would hand a viewer the editor's answer.
    /// </summary>
    public static async Task BoardChangedAsync(IHubContext<CanvasHub> hub, CanvasBoardSummaryDto board)
    {
        foreach (var (connectionId, state) in Connections)
        {
            if (state.BoardId != board.Id) continue;
            await hub.Clients.Client(connectionId).SendAsync("boardChanged", board with { CanWrite = CanWrite(state.Role) });
        }
    }

    public static async Task BoardDeletedAsync(IHubContext<CanvasHub> hub, Guid boardId)
    {
        await hub.Clients.Group(GroupOf(boardId)).SendAsync("boardDeleted", boardId);

        foreach (var (connectionId, state) in Connections)
        {
            if (state.BoardId != boardId) continue;
            Connections.TryRemove(connectionId, out _);
            await hub.Groups.RemoveFromGroupAsync(connectionId, GroupOf(boardId));
        }
    }

    /// <summary>
    /// Removing a member takes effect now, not on their next write. The lazy path (every call
    /// re-resolves) is still the guarantee; this is what stops a removed collaborator from
    /// *watching* a board they no longer have.
    /// </summary>
    public static async Task RevokeAsync(IHubContext<CanvasHub> hub, string userId, IReadOnlyCollection<Guid> boardIds)
    {
        foreach (var (connectionId, state) in Connections)
        {
            if (state.UserId != userId || !boardIds.Contains(state.BoardId)) continue;

            Connections.TryRemove(connectionId, out _);
            await hub.Groups.RemoveFromGroupAsync(connectionId, GroupOf(state.BoardId));
            await hub.Clients.Group(GroupOf(state.BoardId)).SendAsync("peerLeft", connectionId);
            await hub.Clients.Client(connectionId).SendAsync("boardDeleted", state.BoardId);
        }
    }

    #endregion

    private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? throw new HubException(ErrorMessages.UnknownBoard);

    /// <summary>
    /// The full check, run before anything is written. A caller who lost access between two calls
    /// is refused here — with the board's own "no such board" when the project stopped resolving
    /// at all, which is the 404 discipline carried over the socket.
    /// </summary>
    private async Task<ProjectAccess> ResolveAsync(Guid boardId, bool write)
    {
        var access = await ProjectAccessResolver.ResolveBoardAsync(scopes, boardId, UserId, Context.ConnectionAborted)
                     ?? throw new HubException(ErrorMessages.UnknownBoard);
        if (write && !access.CanWrite) throw new HubException(ErrorMessages.NoWriteAccessToProject);
        return access;
    }

    private async Task<ConnectionState> RequireLiveAsync(Guid boardId, bool write)
    {
        if (!Connections.TryGetValue(Context.ConnectionId, out var state) || state.BoardId != boardId)
            throw new HubException(ErrorMessages.UnknownBoard);

        if (state.Access is null || DateTime.UtcNow - state.CheckedAt > TransientAccessTtl)
        {
            state.Access = await ProjectAccessResolver.ResolveBoardAsync(
                scopes, boardId, state.UserId, Context.ConnectionAborted);
            state.CheckedAt = DateTime.UtcNow;
        }

        if (state.Access is null) throw new HubException(ErrorMessages.UnknownBoard);
        if (write && !state.Access.CanWrite) throw new HubException(ErrorMessages.NoWriteAccessToProject);
        return state;
    }

    private void Refresh(ProjectAccess access)
    {
        if (!Connections.TryGetValue(Context.ConnectionId, out var state)) return;
        state.Access = access;
        state.CheckedAt = DateTime.UtcNow;
    }

    private async Task LeaveCurrentAsync(Guid exceptBoardId)
    {
        if (!Connections.TryGetValue(Context.ConnectionId, out var state) || state.BoardId == exceptBoardId) return;

        Connections.TryRemove(Context.ConnectionId, out _);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(state.BoardId));
        await Clients.Group(GroupOf(state.BoardId)).SendAsync("peerLeft", Context.ConnectionId);
    }

    private static PresenceDto Describe(string connectionId, ConnectionState state) =>
        new(connectionId, state.UserId, state.DisplayName, state.ColorIndex, state.Role);

    private static bool CanWrite(string wireRole) =>
        wireRole == ProjectAccess.OwnerRole || ProjectRoles.CanWrite(wireRole);

    private static async Task<string> DisplayNameAsync(CedarDbContext db, string userId, CancellationToken ct) =>
        ProjectMemberEndpoints.NameOf(await db.Users.Where(u => u.Id == userId)
            .Select(u => new ProjectMemberEndpoints.Person(u.Id, u.AuthorDisplayName, u.TenantUsername, u.Email))
            .FirstOrDefaultAsync(ct));

    /// <summary>
    /// The peer colour, from the account id rather than from arrival order: a reconnect must not
    /// repaint somebody mid-session, and two clients must agree about who is green. FNV-1a because
    /// <c>string.GetHashCode</c> is randomised per process — the same person would be a different
    /// colour after every restart, and a test could not name one.
    /// </summary>
    public static int ColorOf(string userId)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in userId) hash = (hash ^ c) * 16777619;
            return (int)(hash % (uint)Consts.Canvas.PresenceColors);
        }
    }
}
