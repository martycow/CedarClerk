using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Tests;

// T-301 / ADR-217 — the tests that justify the design. Every row carries the project owner's id,
// and the member's own tenant scope is blind to the very boards they are allowed to edit: that is
// not a bug to work around, it is the reason canvas work opens the owner's scope explicitly.
public class CanvasTenancyTests
{
    [Fact]
    public async Task Canvas_rows_written_by_a_member_carry_the_project_owner_id()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);
        var board = fx.Board(owner, project);
        var access = await fx.AccessAsync(project, mate);
        var id = Guid.NewGuid();

        // The member's work runs inside the owner's scope, which is what stamps the row.
        await using (var db = fx.As(access.OwnerId))
            await CanvasWrites.AddAsync(db, access, board, mate, [CanvasFixture.Note(id)]);

        await using var check = fx.Platform();
        var item = check.CanvasItems.Single(i => i.Id == id);
        Assert.Equal(owner, item.OwnerId);
        Assert.Equal(project, item.ProjectId);
        // Who made it is still recorded — it is simply not who owns it.
        Assert.Equal(mate, item.CreatedByUserId);
    }

    [Fact]
    public async Task A_member_tenant_scope_cannot_see_the_boards_it_may_edit()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);
        var board = fx.Board(owner, project);

        await using (var asMate = fx.As(mate))
        {
            Assert.Empty(asMate.CanvasBoards);
            Assert.Empty(asMate.Projects);
            // Not even their own membership row: it belongs to the owner's tenant by design.
            Assert.Empty(asMate.ProjectMembers);
        }

        await using var asOwner = fx.As(owner);
        Assert.Single(asOwner.CanvasBoards.Where(b => b.Id == board));
    }

    [Fact]
    public async Task A_membership_row_belongs_to_the_owner_not_the_invitee()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var project = fx.Project(owner);
        var membership = fx.Member(owner, project, "mate@local.test", ProjectRoles.Editor, mate);

        await using var db = fx.Platform();
        var row = db.ProjectMembers.Single(m => m.Id == membership);
        Assert.Equal(owner, row.OwnerId);
        Assert.Equal(mate, row.MemberUserId);
        Assert.NotEqual(row.OwnerId, row.MemberUserId);
    }

    [Fact]
    public async Task A_picture_on_a_board_is_readable_by_the_project_and_by_nobody_else()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var stranger = fx.User("stranger");
        var project = fx.Project(owner);
        fx.Member(owner, project, "mate@local.test", ProjectRoles.Viewer, mate);
        var board = fx.Board(owner, project);
        var asset = Guid.NewGuid();

        var access = await fx.AccessAsync(project, owner);
        await using (var db = fx.As(owner))
            await CanvasWrites.AddAsync(db, access, board, owner, [Image(asset)]);

        await using var platform = fx.Platform();

        // Nothing else in the app claims a library upload, so this lookup is the only thing between
        // a member and a broken picture on a board they are allowed to open.
        var projects = await CanvasMediaIndex.ProjectsOfAssetAsync(platform, owner, asset);
        Assert.Equal([project], projects);

        Assert.NotNull(await ProjectAccessResolver.ResolveAsync(platform, project, mate));
        Assert.Null(await ProjectAccessResolver.ResolveAsync(platform, project, stranger));
        Assert.Empty(await CanvasMediaIndex.ProjectsOfAssetAsync(platform, owner, Guid.NewGuid()));
    }

    private static CanvasItemInput Image(Guid asset) =>
        new(Guid.NewGuid(), CanvasItemKinds.Image, 0, 0, 460, 460, 0, "",
            CanvasFixture.Payload($$"""{"url":"/media/asset_{{asset:D}}.png","naturalWidth":800,"naturalHeight":800,"alt":""}"""));

    [Fact]
    public async Task Deleting_an_account_removes_both_its_memberships_and_the_memberships_of_its_projects()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var mate = fx.User("mate");
        var stranger = fx.User("stranger");

        var mine = fx.Project(owner, "Mine");
        var theirs = fx.Project(stranger, "Theirs");
        var board = fx.Board(owner, mine);

        // Somebody is on my project, and I am on somebody else's.
        fx.Member(owner, mine, "mate@local.test", ProjectRoles.Editor, mate);
        fx.Member(stranger, theirs, "owner@local.test", ProjectRoles.Viewer, owner);

        await using (var db = fx.Platform())
        {
            db.CanvasItems.Add(new CanvasItem { OwnerId = owner, ProjectId = mine, BoardId = board });
            await db.SaveChangesAsync();
            await AccountDeletion.DeleteAsync(db, owner, mediaDir: null, new TenantOwnerCache.ForHosts());
        }

        await using var check = fx.Platform();
        Assert.Empty(check.CanvasBoards.Where(b => b.OwnerId == owner));
        Assert.Empty(check.CanvasItems.Where(i => i.OwnerId == owner));
        Assert.Empty(check.ProjectMembers.Where(m => m.OwnerId == owner));
        // The one that is easy to miss: without it a deleted account keeps granting access to
        // somebody else's project through a user id nothing answers for.
        Assert.Empty(check.ProjectMembers.Where(m => m.MemberUserId == owner));
    }
}
