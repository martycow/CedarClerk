using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

/// <summary>
/// Every write an item can take, against a context already scoped to the project owner. The hub is
/// the only caller (ADR-218 keeps one write path for items), and it lives here rather than inside
/// the hub methods so the rules can be exercised without a socket.
///
/// Conflicts are last-writer-wins: a write from a permitted writer is always accepted, and
/// <c>Version</c> is the server's own counter so a client can drop an echo older than the state it
/// already holds. Nothing here rejects a write for being stale — that would be optimistic locking,
/// which two people dragging one sticky note would hit constantly and could do nothing useful about.
/// </summary>
public static class CanvasWrites
{
    /// <summary>
    /// How many items one call may carry. A board holds two thousand, but a single message holding
    /// two thousand payloads is eight megabytes of parsing on a 1 vCPU droplet with no swap; the
    /// editor batches a selection, never a whole board.
    /// </summary>
    public const int MaxItemsPerCall = 100;

    /// <summary>
    /// One writer at a time per board. <c>Version</c> is a read-modify-write and every hub call
    /// opens its own scope, so two overlapping writes read the same number, stamp it onto two
    /// different in-memory states and broadcast both — after which each client keeps whichever
    /// arrived last and none of them holds the row the database does. SQLite admits one writer
    /// anyway, so waiting here costs nothing that was not already being waited for.
    ///
    /// Striped rather than one gate per board: a deleted board leaves no entry behind, and two
    /// boards that share a stripe only ever wait for each other.
    /// </summary>
    private static readonly SemaphoreSlim[] Gates =
        Enumerable.Range(0, 16).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim GateOf(Guid boardId) =>
        Gates[(int)((uint)boardId.GetHashCode() % (uint)Gates.Length)];

    private static async Task<T> UnderGateAsync<T>(Guid boardId, Func<Task<T>> work, CancellationToken ct)
    {
        var gate = GateOf(boardId);
        await gate.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// False when the rows moved underneath this write — a delete that landed first. Zero rows
    /// affected is how EF says so, and it says it by throwing; on this path it is not an error,
    /// because the caller wanted those rows gone or moved and gone is the newer fact.
    /// </summary>
    private static async Task<bool> SaveAsync(CedarDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public static async Task<List<CanvasItem>> ItemsAsync(CedarDbContext db, Guid boardId, CancellationToken ct = default)
    {
        var items = await db.CanvasItems.Where(i => i.BoardId == boardId).ToListAsync(ct);
        return InRenderOrder(items);
    }

    /// <summary>
    /// Total and stable everywhere: ties on Z are possible under concurrency (two clients can be
    /// handed the same "next" value), and the two further keys make them render the same way for
    /// everyone rather than differently per client.
    /// </summary>
    public static List<CanvasItem> InRenderOrder(IEnumerable<CanvasItem> items) =>
        items.OrderBy(i => i.Z).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id).ToList();

    public static Task<(string? Error, CanvasItemDto[] Items)> AddAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<CanvasItemInput> inputs, CancellationToken ct = default) =>
        UnderGateAsync(boardId, () => AddCoreAsync(db, access, boardId, userId, inputs, ct), ct);

    private static async Task<(string? Error, CanvasItemDto[] Items)> AddCoreAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<CanvasItemInput> inputs, CancellationToken ct)
    {
        var board = await BoardAsync(db, access, boardId, ct);
        if (board is null) return (ErrorMessages.UnknownBoard, []);
        if (inputs.Count == 0) return (null, []);
        if (inputs.Count > MaxItemsPerCall) return (ErrorMessages.CanvasBatchLimitReached(MaxItemsPerCall), []);

        var payloads = new Dictionary<Guid, string>();
        foreach (var input in inputs)
        {
            if (!CanvasItemKinds.IsKnown(input.Kind)) return (ErrorMessages.UnknownCanvasItemKind(input.Kind ?? ""), []);
            if (Geometry(input.X, input.Y, input.Width, input.Height) is { } bad) return (bad, []);

            var parsed = Payload(input.Kind, input.Payload);
            if (parsed.Error is { } payloadError) return (payloadError, []);
            payloads[input.Id] = parsed.Json;
        }

        var ids = inputs.Select(i => i.Id).ToList();

        // A repeated send after a reconnect must not double the sticky note: the client generates
        // the id, so an id already on the board identifies the same item and is answered with it.
        var known = await db.CanvasItems.Where(i => i.BoardId == boardId && ids.Contains(i.Id)).ToListAsync(ct);
        var fresh = inputs.Where(i => known.All(k => k.Id != i.Id))
            .GroupBy(i => i.Id).Select(g => g.First()).ToList();

        if (fresh.Count > 0)
        {
            var count = await db.CanvasItems.CountAsync(i => i.BoardId == boardId, ct);
            if (count + fresh.Count > Consts.Canvas.ItemsPerBoard)
                return (ErrorMessages.CanvasItemLimitReached(Consts.Canvas.ItemsPerBoard), []);

            var top = await db.CanvasItems.Where(i => i.BoardId == boardId)
                .Select(i => (int?)i.Z).MaxAsync(ct) ?? 0;

            foreach (var input in fresh)
            {
                var item = new CanvasItem
                {
                    Id = input.Id,
                    OwnerId = access.OwnerId,
                    ProjectId = access.ProjectId,
                    BoardId = boardId,
                    Kind = input.Kind,
                    X = input.X,
                    Y = input.Y,
                    Width = input.Width,
                    Height = input.Height,
                    Rotation = Normalize(input.Rotation),
                    Z = ++top,
                    Color = input.Color?.Trim() ?? "",
                    Payload = payloads[input.Id],
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                };
                db.CanvasItems.Add(item);
                known.Add(item);
            }

            Touch(board, userId);
            // The only row here that another writer can have taken is the board itself, and a board
            // that is gone is the one thing an add cannot be honest about.
            if (!await SaveAsync(db, ct)) return (ErrorMessages.UnknownBoard, []);
        }

        var order = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return (null, known.OrderBy(i => order.GetValueOrDefault(i.Id, int.MaxValue))
            .Select(CanvasMapping.Describe).ToArray());
    }

    public static Task<(string? Error, CanvasItemDto[] Items)> UpdateAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<CanvasItemPatch> patches, CancellationToken ct = default) =>
        UnderGateAsync(boardId, () => UpdateCoreAsync(db, access, boardId, userId, patches, ct), ct);

    private static async Task<(string? Error, CanvasItemDto[] Items)> UpdateCoreAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<CanvasItemPatch> patches, CancellationToken ct)
    {
        var board = await BoardAsync(db, access, boardId, ct);
        if (board is null) return (ErrorMessages.UnknownBoard, []);
        if (patches.Count == 0) return (null, []);
        if (patches.Count > MaxItemsPerCall) return (ErrorMessages.CanvasBatchLimitReached(MaxItemsPerCall), []);

        var ids = patches.Select(p => p.Id).ToList();
        var items = await db.CanvasItems.Where(i => i.BoardId == boardId && ids.Contains(i.Id)).ToListAsync(ct);
        // An id that is gone is not an error: somebody deleted it while this drag was in the air,
        // and the deletion is the newer fact. The client learns it from itemsDeleted, not from a
        // refusal it would have to undo.
        if (items.Count == 0) return (null, []);

        foreach (var patch in patches)
        {
            var item = items.FirstOrDefault(i => i.Id == patch.Id);
            if (item is null) continue;

            var x = patch.X ?? item.X;
            var y = patch.Y ?? item.Y;
            var width = patch.Width ?? item.Width;
            var height = patch.Height ?? item.Height;
            if (Geometry(x, y, width, height) is { } bad) return (bad, []);

            var json = item.Payload;
            if (patch.Payload is { } supplied)
            {
                var parsed = Payload(item.Kind, supplied);
                if (parsed.Error is { } payloadError) return (payloadError, []);
                json = parsed.Json;
            }

            item.X = x;
            item.Y = y;
            item.Width = width;
            item.Height = height;
            if (patch.Rotation is { } rotation) item.Rotation = Normalize(rotation);
            if (patch.Color is { } color) item.Color = color.Trim();
            item.Payload = json;
            Stamp(item, userId);
        }

        Touch(board, userId);
        if (!await SaveAsync(db, ct))
        {
            // One of these was deleted between the read and the save. What the caller needs back is
            // what survived; the deletion reaches the client as itemsDeleted, not as a refusal.
            var surviving = await db.CanvasItems.AsNoTracking()
                .Where(i => i.BoardId == boardId && ids.Contains(i.Id)).ToListAsync(ct);
            return (null, InRenderOrder(surviving).Select(CanvasMapping.Describe).ToArray());
        }

        return (null, InRenderOrder(items).Select(CanvasMapping.Describe).ToArray());
    }

    public static Task<(string? Error, Guid[] Ids)> DeleteAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<Guid> itemIds, CancellationToken ct = default) =>
        UnderGateAsync(boardId, () => DeleteCoreAsync(db, access, boardId, userId, itemIds, ct), ct);

    private static async Task<(string? Error, Guid[] Ids)> DeleteCoreAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<Guid> itemIds, CancellationToken ct)
    {
        var board = await BoardAsync(db, access, boardId, ct);
        if (board is null) return (ErrorMessages.UnknownBoard, []);
        if (itemIds.Count == 0) return (null, []);

        var items = await db.CanvasItems.Where(i => i.BoardId == boardId && itemIds.Contains(i.Id)).ToListAsync(ct);
        if (items.Count == 0) return (null, []);

        // Hard delete, no tombstone: a late joiner takes a whole snapshot, so there is nothing a
        // tombstone would let it reconcile.
        db.CanvasItems.RemoveRange(items);
        Touch(board, userId);
        // Somebody deleting the same item first is this call's own outcome arriving early, so the
        // ids go back either way.
        await SaveAsync(db, ct);

        return (null, items.Select(i => i.Id).ToArray());
    }

    public static Task<(string? Error, CanvasItemDto[] Items)> BringToFrontAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<Guid> itemIds, CancellationToken ct = default) =>
        UnderGateAsync(boardId, () => BringToFrontCoreAsync(db, access, boardId, userId, itemIds, ct), ct);

    private static async Task<(string? Error, CanvasItemDto[] Items)> BringToFrontCoreAsync(
        CedarDbContext db, ProjectAccess access, Guid boardId, string userId,
        IReadOnlyList<Guid> itemIds, CancellationToken ct)
    {
        var board = await BoardAsync(db, access, boardId, ct);
        if (board is null) return (ErrorMessages.UnknownBoard, []);
        if (itemIds.Count == 0) return (null, []);

        var items = await db.CanvasItems.Where(i => i.BoardId == boardId && itemIds.Contains(i.Id)).ToListAsync(ct);
        if (items.Count == 0) return (null, []);

        var top = await db.CanvasItems.Where(i => i.BoardId == boardId).Select(i => (int?)i.Z).MaxAsync(ct) ?? 0;
        foreach (var item in InRenderOrder(items))
        {
            item.Z = ++top;
            Stamp(item, userId);
        }

        Touch(board, userId);
        if (!await SaveAsync(db, ct))
        {
            var surviving = await db.CanvasItems.AsNoTracking()
                .Where(i => i.BoardId == boardId && itemIds.Contains(i.Id)).ToListAsync(ct);
            return (null, InRenderOrder(surviving).Select(CanvasMapping.Describe).ToArray());
        }

        return (null, InRenderOrder(items).Select(CanvasMapping.Describe).ToArray());
    }

    /// <summary>
    /// The board of this project, in the owner's scope. The project is named as well as the id:
    /// a board id belonging to another project of the same owner would otherwise pass the tenant
    /// filter and be written through access resolved for a different project.
    /// </summary>
    public static Task<CanvasBoard?> BoardAsync(CedarDbContext db, ProjectAccess access, Guid boardId, CancellationToken ct = default) =>
        db.CanvasBoards.FirstOrDefaultAsync(b => b.Id == boardId && b.ProjectId == access.ProjectId, ct);

    /// <summary>The board list sorts by UpdatedAt, so anything written on a board moves the board.</summary>
    public static void Touch(CanvasBoard board, string userId)
    {
        board.UpdatedAt = DateTime.UtcNow;
        board.UpdatedByUserId = userId;
    }

    private static void Stamp(CanvasItem item, string userId)
    {
        item.Version += 1;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedByUserId = userId;
    }

    private static string? Geometry(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return ErrorMessages.CanvasGeometryInvalid;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return ErrorMessages.CanvasGeometryInvalid;
        return null;
    }

    private static (string? Error, string Json) Payload(string kind, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return (ErrorMessages.CanvasPayloadInvalid, "{}");

        var json = payload.GetRawText();
        if (json.Length > Consts.Canvas.PayloadMaxChars)
            return (ErrorMessages.CanvasPayloadTooLarge(Consts.Canvas.PayloadMaxChars), "{}");

        return (CanvasPayload.Validate(kind, json), json);
    }

    /// <summary>
    /// An angle is cyclic, so 540° is a legal way to say 180°. Normalised rather than refused —
    /// a client that accumulates rotation across drags is not doing anything wrong.
    /// </summary>
    private static double Normalize(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0;
        var wrapped = degrees % 360;
        if (wrapped > 180) wrapped -= 360;
        if (wrapped <= -180) wrapped += 360;
        return wrapped;
    }
}
