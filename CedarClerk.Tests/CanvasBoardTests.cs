using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-301 / ADR-218 — the item write path, exercised against the owner's scope the way the hub runs
// it, without a socket.
public class CanvasBoardTests
{
    [Fact]
    public async Task Items_come_back_in_z_then_created_then_id_order()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        await using (var db = fx.As(owner))
        {
            var when = new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);
            // Two items share a Z, which concurrency really can produce: the further keys are what
            // make everyone draw them in the same order rather than each client in its own.
            db.CanvasItems.AddRange(
                new CanvasItem { OwnerId = owner, ProjectId = project, BoardId = board, Z = 2, CreatedAt = when },
                new CanvasItem { OwnerId = owner, ProjectId = project, BoardId = board, Z = 1, CreatedAt = when.AddMinutes(5) },
                new CanvasItem { OwnerId = owner, ProjectId = project, BoardId = board, Z = 1, CreatedAt = when });
            await db.SaveChangesAsync();
        }

        await using var read = fx.As(owner);
        var items = await CanvasWrites.ItemsAsync(read, board);

        Assert.Equal([1, 1, 2], items.Select(i => i.Z));
        Assert.True(items[0].CreatedAt <= items[1].CreatedAt);
    }

    [Fact]
    public async Task A_new_item_takes_the_next_z_above_everything_on_the_board()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        await using var db = fx.As(owner);
        var (first, _) = await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(Guid.NewGuid())]);
        Assert.Null(first);
        var (second, added) = await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(Guid.NewGuid())]);
        Assert.Null(second);

        var items = await CanvasWrites.ItemsAsync(db, board);
        Assert.Equal([1, 2], items.Select(i => i.Z));
        Assert.Equal(2, added.Single().Z);
    }

    [Fact]
    public async Task Bring_to_front_moves_only_the_named_items()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        var bottom = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var top = Guid.NewGuid();

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, board, owner,
            [CanvasFixture.Note(bottom), CanvasFixture.Note(middle), CanvasFixture.Note(top)]);

        var (error, _) = await CanvasWrites.BringToFrontAsync(db, access, board, owner, [bottom]);
        Assert.Null(error);

        var order = (await CanvasWrites.ItemsAsync(db, board)).Select(i => i.Id).ToArray();
        Assert.Equal([middle, top, bottom], order);
    }

    [Fact]
    public async Task Deleting_a_board_deletes_its_items_and_nothing_elses()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var doomed = fx.Board(owner, project, "Doomed");
        var kept = fx.Board(owner, project, "Kept");
        var access = await fx.AccessAsync(project, owner);

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, doomed, owner, [CanvasFixture.Note(Guid.NewGuid())]);
        await CanvasWrites.AddAsync(db, access, kept, owner, [CanvasFixture.Note(Guid.NewGuid())]);

        var items = db.CanvasItems.Where(i => i.BoardId == doomed).ToList();
        db.CanvasItems.RemoveRange(items);
        db.CanvasBoards.Remove(db.CanvasBoards.Single(b => b.Id == doomed));
        await db.SaveChangesAsync();

        Assert.Empty(await CanvasWrites.ItemsAsync(db, doomed));
        Assert.Single(await CanvasWrites.ItemsAsync(db, kept));
    }

    [Fact]
    public async Task Board_updated_at_moves_when_an_item_is_written()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        await using var db = fx.As(owner);
        var row = db.CanvasBoards.Single(b => b.Id == board);
        row.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        row.UpdatedByUserId = "";
        await db.SaveChangesAsync();

        await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(Guid.NewGuid())]);

        // The board list sorts by this, so an item written on a board has to move the board.
        Assert.True(row.UpdatedAt.Year > 2020);
        Assert.Equal(owner, row.UpdatedByUserId);
    }

    [Fact]
    public async Task Items_are_capped_per_board()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        await using var db = fx.As(owner);
        db.CanvasItems.AddRange(Enumerable.Range(0, Consts.Canvas.ItemsPerBoard)
            .Select(i => new CanvasItem { OwnerId = owner, ProjectId = project, BoardId = board, Z = i }));
        await db.SaveChangesAsync();

        var (error, _) = await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(Guid.NewGuid())]);
        Assert.Equal(ErrorMessages.CanvasItemLimitReached(Consts.Canvas.ItemsPerBoard), error);
    }

    [Fact]
    public async Task One_call_may_not_carry_more_than_the_batch_cap()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        var batch = Enumerable.Range(0, CanvasWrites.MaxItemsPerCall + 1)
            .Select(_ => CanvasFixture.Note(Guid.NewGuid())).ToArray();

        await using var db = fx.As(owner);
        var (error, _) = await CanvasWrites.AddAsync(db, access, board, owner, batch);
        Assert.NotNull(error);
        Assert.Empty(await CanvasWrites.ItemsAsync(db, board));
    }

    [Fact]
    public async Task A_board_of_another_project_is_refused_even_inside_the_same_owner()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var shared = fx.Project(owner, "Shared");
        var other = fx.Project(owner, "Other");
        var elsewhere = fx.Board(owner, other);
        var access = await fx.AccessAsync(shared, owner);

        // The tenant filter would pass this board — both projects are the same owner's. What stops
        // it is that access was resolved for one project and the board belongs to another.
        await using var db = fx.As(owner);
        var (error, _) = await CanvasWrites.AddAsync(db, access, elsewhere, owner, [CanvasFixture.Note(Guid.NewGuid())]);
        Assert.Equal(ErrorMessages.UnknownBoard, error);
    }

    [Fact]
    public async Task An_item_needs_a_positive_width_and_height()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);

        var flat = new CanvasItemInput(Guid.NewGuid(), CanvasItemKinds.Note, 0, 0, 0, 100, 0, "",
            CanvasFixture.Payload("""{"text":"a"}"""));

        await using var db = fx.As(owner);
        var (error, _) = await CanvasWrites.AddAsync(db, access, board, owner, [flat]);
        Assert.Equal(ErrorMessages.CanvasGeometryInvalid, error);
    }
}
