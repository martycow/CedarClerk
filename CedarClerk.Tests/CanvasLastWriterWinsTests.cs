using CedarClerk.Core;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-301 / ADR-218 — the conflict rule, stated as tests because it is the one place where doing the
// obvious thing (rejecting a stale write) would be wrong. Version is a counter the client reads,
// not a lock the server enforces.
public class CanvasLastWriterWinsTests
{
    private static CanvasItemPatch MoveTo(Guid id, double x, double y) =>
        new(id, x, y, null, null, null, null, null);

    [Fact]
    public async Task Every_accepted_write_increments_the_version()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var id = Guid.NewGuid();

        await using var db = fx.As(owner);
        var (_, born) = await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(id)]);
        Assert.Equal(1, born.Single().Version);

        var (_, once) = await CanvasWrites.UpdateAsync(db, access, board, owner, [MoveTo(id, 10, 10)]);
        Assert.Equal(2, once.Single().Version);

        var (_, twice) = await CanvasWrites.UpdateAsync(db, access, board, owner, [MoveTo(id, 20, 20)]);
        Assert.Equal(3, twice.Single().Version);
    }

    [Fact]
    public async Task The_server_accepts_a_write_whose_base_version_is_stale()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var mateAccess = await fx.AccessAsync(project, mate);
        var id = Guid.NewGuid();

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(id)]);
        await CanvasWrites.UpdateAsync(db, access, board, owner, [MoveTo(id, 100, 100)]);

        // The second writer's client still believes in version 1. Last writer wins: the write lands
        // and the counter keeps climbing. Two people dragging one note is rare and self-correcting;
        // a refusal here is a rollback the client could do nothing useful with.
        var (error, changed) = await CanvasWrites.UpdateAsync(db, mateAccess, board, mate, [MoveTo(id, 7, 7)]);

        Assert.Null(error);
        Assert.Equal(7, changed.Single().X);
        Assert.Equal(3, changed.Single().Version);
        Assert.Equal(mate, changed.Single().UpdatedBy);
    }

    [Fact]
    public async Task Two_overlapping_writes_take_two_versions_and_the_newer_echo_is_the_stored_row()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var id = Guid.NewGuid();

        await using (var seed = fx.As(owner))
            await CanvasWrites.AddAsync(seed, access, board, owner, [CanvasFixture.Note(id, 0, 0, "old")]);

        // Two clients, two scopes, one item — a drag ending and a note being typed. Read the
        // version, add one, save: monotonic only while the whole sequence belongs to one writer.
        // Overlapping, both read 1 and both write 2, and the two echoes carry one number over two
        // different states, neither of which is the row.
        await using var moving = fx.As(owner);
        await using var typing = fx.As(owner);

        var answers = await Task.WhenAll(
            CanvasWrites.UpdateAsync(moving, access, board, owner, [MoveTo(id, 500, 0)]),
            CanvasWrites.UpdateAsync(typing, access, board, owner, [Retype(id, "new")]));

        var echoes = answers.Select(a => a.Items.Single()).OrderBy(i => i.Version).ToArray();
        Assert.Equal([2, 3], echoes.Select(i => i.Version));

        await using var check = fx.Platform();
        var row = check.CanvasItems.Single(i => i.Id == id);
        Assert.Equal(500, row.X);
        Assert.Equal(row.X, echoes[1].X);
        Assert.Equal("new", echoes[1].Payload.GetProperty("text").GetString());
    }

    private static CanvasItemPatch Retype(Guid id, string text) =>
        new(id, null, null, null, null, null, null,
            CanvasFixture.Payload($$"""{"text":"{{text}}","align":"left"}"""));

    [Fact]
    public async Task A_second_add_with_the_same_id_returns_the_existing_item_rather_than_duplicating()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var id = Guid.NewGuid();

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(id, 5, 5, "first")]);
        var (error, echoed) = await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(id, 900, 900, "again")]);

        // A retried send after a reconnect must not double the sticky note, and must not move it either.
        Assert.Null(error);
        Assert.Equal(5, echoed.Single().X);
        Assert.Single(await CanvasWrites.ItemsAsync(db, board));
    }

    [Fact]
    public async Task A_patch_leaves_untouched_fields_alone()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var id = Guid.NewGuid();

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(id, 5, 6, "keep me")]);

        var (error, changed) = await CanvasWrites.UpdateAsync(db, access, board, owner, [MoveTo(id, 50, 60)]);
        Assert.Null(error);

        var item = changed.Single();
        Assert.Equal(50, item.X);
        Assert.Equal(60, item.Y);
        Assert.Equal(220, item.Width);
        Assert.Equal(180, item.Height);
        Assert.Equal("keep me", item.Payload.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_patch_for_an_item_that_is_already_gone_is_skipped_not_refused()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var alive = Guid.NewGuid();

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(alive)]);

        // A concurrent delete is the newer fact; the client hears about it from itemsDeleted, not
        // from a refusal it would have to undo.
        var (error, changed) = await CanvasWrites.UpdateAsync(db, access, board, owner,
            [MoveTo(alive, 1, 1), MoveTo(Guid.NewGuid(), 2, 2)]);

        Assert.Null(error);
        Assert.Single(changed);
    }

    [Fact]
    public async Task Rotation_is_normalised_rather_than_refused()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, owner);
        var id = Guid.NewGuid();

        await using var db = fx.As(owner);
        await CanvasWrites.AddAsync(db, access, board, owner, [CanvasFixture.Note(id)]);

        // An angle is cyclic, so 540 is a legal way to say 180 — a client accumulating rotation
        // across drags is not doing anything wrong.
        var (error, changed) = await CanvasWrites.UpdateAsync(db, access, board, owner,
            [new CanvasItemPatch(id, null, null, null, null, 540, null, null)]);

        Assert.Null(error);
        Assert.Equal(180, changed.Single().Rotation);
    }
}
