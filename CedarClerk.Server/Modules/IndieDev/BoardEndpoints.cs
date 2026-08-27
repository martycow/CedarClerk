using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Modules.IndieDev;

// Reference boards, T-301 (ADR-217/218). The first part of the module a non-owner can reach: every
// route resolves membership through ProjectAccessResolver and then works inside the project
// owner's tenant scope, so a member's row lands stamped with the owner's id like everything else.
//
// Items are deliberately absent here — they are written over the hub and only read from it or from
// the snapshot below, which exists so a board is never hostage to a socket.
public static class CanvasEndpoints
{
    public record SaveBoardRequest(string? Name, string? Background);

    public static void MapCanvasEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/api/projects/{projectId:guid}/canvas").RequireAuthorization();

        boards.MapGet("/", async (Guid projectId, ClaimsPrincipal user, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, UserId(user), ct);
            if (access is null) return Results.NotFound();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var list = await db.CanvasBoards
                .Where(b => b.ProjectId == projectId)
                .OrderByDescending(b => b.UpdatedAt)
                .ToListAsync(ct);

            var counts = await db.CanvasItems
                .Where(i => i.ProjectId == projectId)
                .GroupBy(i => i.BoardId)
                .Select(g => new { BoardId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.BoardId, g => g.Count, ct);

            return Results.Ok(list.Select(b =>
                CanvasMapping.Describe(b, counts.GetValueOrDefault(b.Id), access.CanWrite)));
        });

        boards.MapPost("/", async (Guid projectId, SaveBoardRequest req, ClaimsPrincipal user,
            IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var access = await ProjectAccessResolver.ResolveAsync(scopes, projectId, UserId(user), ct);
            if (access is null) return Results.NotFound();
            if (!access.CanWrite) return Refused();

            var name = (req.Name ?? "").Trim();
            if (name.Length is 0 || name.Length > Consts.Canvas.BoardNameMax)
                return Bad(ErrorMessages.BoardNameLength(Consts.Canvas.BoardNameMax));

            var background = string.IsNullOrWhiteSpace(req.Background) ? CanvasBackgrounds.Grid : req.Background.Trim();
            if (!CanvasBackgrounds.IsKnown(background)) return Bad(ErrorMessages.UnknownCanvasBackground(background));

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            if (await db.CanvasBoards.CountAsync(b => b.ProjectId == projectId, ct) >= Consts.Canvas.BoardsPerProject)
                return Bad(ErrorMessages.BoardLimitReached(Consts.Canvas.BoardsPerProject));

            var board = new CanvasBoard
            {
                OwnerId = access.OwnerId,
                ProjectId = projectId,
                Name = name,
                Background = background,
                CreatedByUserId = UserId(user),
                UpdatedByUserId = UserId(user),
            };
            db.CanvasBoards.Add(board);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/canvas/{board.Id}", CanvasMapping.Describe(board, 0, true));
        });

        var single = app.MapGroup("/api/canvas/{boardId:guid}").RequireAuthorization();

        single.MapGet("/", async (Guid boardId, ClaimsPrincipal user, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            var snapshot = await SnapshotAsync(scopes, boardId, UserId(user), ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        });

        // The escape hatch: the same snapshot, as a file. No second format — a board that can be
        // read back into this one is worth more than a prettier export nothing reads.
        single.MapGet("/export", async (Guid boardId, ClaimsPrincipal user, IServiceScopeFactory scopes,
            HttpContext ctx, CancellationToken ct) =>
        {
            var snapshot = await SnapshotAsync(scopes, boardId, UserId(user), ct);
            if (snapshot is null) return Results.NotFound();

            // The board's own name would have to be sanitised into a filename, and a wrong guess
            // there is a broken download; the id is already unique and always safe.
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"canvas-{boardId}.json\"";
            return Results.Ok(snapshot);
        });

        single.MapPut("/", async (Guid boardId, SaveBoardRequest req, ClaimsPrincipal user,
            IServiceScopeFactory scopes, IHubContext<CanvasHub> hub, CancellationToken ct) =>
        {
            var access = await ProjectAccessResolver.ResolveBoardAsync(scopes, boardId, UserId(user), ct);
            if (access is null) return Results.NotFound();
            if (!access.CanWrite) return Refused();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var board = await CanvasWrites.BoardAsync(db, access, boardId, ct);
            if (board is null) return Results.NotFound();

            if (req.Name is not null)
            {
                var name = req.Name.Trim();
                if (name.Length is 0 || name.Length > Consts.Canvas.BoardNameMax)
                    return Bad(ErrorMessages.BoardNameLength(Consts.Canvas.BoardNameMax));
                board.Name = name;
            }

            if (req.Background is not null)
            {
                var background = req.Background.Trim();
                if (!CanvasBackgrounds.IsKnown(background)) return Bad(ErrorMessages.UnknownCanvasBackground(background));
                board.Background = background;
            }

            board.Version += 1;
            CanvasWrites.Touch(board, UserId(user));
            await db.SaveChangesAsync(ct);

            var count = await db.CanvasItems.CountAsync(i => i.BoardId == boardId, ct);
            var summary = CanvasMapping.Describe(board, count, true);
            await CanvasHub.BoardChangedAsync(hub, summary);

            return Results.Ok(summary);
        });

        single.MapDelete("/", async (Guid boardId, ClaimsPrincipal user, IServiceScopeFactory scopes,
            IHubContext<CanvasHub> hub, CancellationToken ct) =>
        {
            var access = await ProjectAccessResolver.ResolveBoardAsync(scopes, boardId, UserId(user), ct);
            if (access is null) return Results.NotFound();
            if (!access.CanWrite) return Refused();

            using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

            var board = await CanvasWrites.BoardAsync(db, access, boardId, ct);
            if (board is null) return Results.NotFound();

            // A board is a surface, not a record of what happened: it goes, and its items go with
            // it. Everything else in the module archives instead — a project keeps its history.
            await db.CanvasItems.Where(i => i.BoardId == boardId).ExecuteDeleteAsync(ct);
            db.CanvasBoards.Remove(board);
            await db.SaveChangesAsync(ct);

            await CanvasHub.BoardDeletedAsync(hub, boardId);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Everything a board page needs in one read, shaped exactly like the hub's <c>Join</c> answer
    /// so the two reconcile against one another. <c>projectName</c> is in it because a member
    /// cannot call <c>GET /api/projects/{id}</c> and the rail has nowhere else to read the name.
    /// </summary>
    public static async Task<CanvasSnapshot?> SnapshotAsync(
        IServiceScopeFactory scopes, Guid boardId, string userId, CancellationToken ct)
    {
        var access = await ProjectAccessResolver.ResolveBoardAsync(scopes, boardId, userId, ct);
        if (access is null) return null;

        using var scope = ProjectAccessResolver.OpenOwnerScope(scopes, access);
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var board = await CanvasWrites.BoardAsync(db, access, boardId, ct);
        if (board is null) return null;

        var items = await CanvasWrites.ItemsAsync(db, boardId, ct);
        var projectName = await db.Projects.Where(p => p.Id == access.ProjectId)
            .Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";

        return new CanvasSnapshot(
            CanvasMapping.Describe(board, items.Count, access.CanWrite), projectName,
            items.Select(CanvasMapping.Describe).ToArray(), access.WireRole, access.CanWrite, []);
    }

    internal static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    internal static IResult Bad(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// 403, not the module's usual 404: a viewer is already looking at the board, so hiding a
    /// refused write behind "no such board" would deny the thing on their screen.
    /// </summary>
    internal static IResult Refused() =>
        Results.Json(new { error = ErrorMessages.NoWriteAccessToProject }, statusCode: StatusCodes.Status403Forbidden);
}
