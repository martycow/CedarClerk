using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Wave 2 item 10 — CRUD for the weekly queue slots <see cref="FillQueueSlotsJob"/> fills. Times
/// are stored and served UTC-only; the client renders and edits in the browser timezone, and the
/// DST drift that implies (a slot firing an hour off local after a switch) is accepted and said in
/// the UI rather than hidden behind a per-user timezone this wave does not have.
/// </summary>
public static class QueueSlotEndpoints
{
    public record SaveSlotRequest(Guid TargetId, string Name = "", string Category = "",
        int DayOfWeek = 0, int TimeUtcMinutes = 0, bool IsActive = true);

    public static void MapQueueSlotEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/queue/slots").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var slots = await db.QueueSlots.Where(s => s.OwnerId == uid)
                .OrderBy(s => s.DayOfWeek).ThenBy(s => s.TimeUtcMinutes)
                .ToListAsync();
            var targetNames = await db.PublishTargets.Where(t => t.OwnerId == uid)
                .ToDictionaryAsync(t => t.Id, t => new { t.DisplayName, t.Network });
            return Results.Ok(slots.Select(s => new
            {
                id = s.Id,
                targetId = s.TargetId,
                targetName = targetNames.TryGetValue(s.TargetId, out var t) ? t.DisplayName : null,
                network = targetNames.TryGetValue(s.TargetId, out var n) ? n.Network : null,
                name = s.Name,
                category = s.Category,
                dayOfWeek = s.DayOfWeek,
                timeUtcMinutes = s.TimeUtcMinutes,
                isActive = s.IsActive,
            }));
        });

        group.MapPost("/", async (SaveSlotRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (Invalid(req) is { } error) return error;

            var target = await db.PublishTargets.FirstOrDefaultAsync(t => t.Id == req.TargetId && t.OwnerId == uid && t.IsActive);
            if (target is null) return Results.NotFound();
            // ADR-299 — LinkedIn's API terms forbid automated posting; a slot there cannot exist.
            if (target.Network == PublishNetworks.LinkedIn)
                return Results.Json(new { error = ErrorMessages.LinkedInNoScheduling }, statusCode: StatusCodes.Status422UnprocessableEntity);

            var slot = new QueueSlot
            {
                OwnerId = uid,
                TargetId = req.TargetId,
                Name = req.Name.Trim(),
                Category = req.Category.Trim(),
                DayOfWeek = req.DayOfWeek,
                TimeUtcMinutes = req.TimeUtcMinutes,
                IsActive = req.IsActive,
            };
            db.QueueSlots.Add(slot);
            await db.SaveChangesAsync();
            return Results.Ok(new { id = slot.Id });
        });

        group.MapPut("/{id:guid}", async (Guid id, SaveSlotRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (Invalid(req) is { } error) return error;

            var slot = await db.QueueSlots.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (slot is null) return Results.NotFound();

            var target = await db.PublishTargets.FirstOrDefaultAsync(t => t.Id == req.TargetId && t.OwnerId == uid && t.IsActive);
            if (target is null) return Results.NotFound();
            // ADR-299 — LinkedIn's API terms forbid automated posting; a slot there cannot exist.
            if (target.Network == PublishNetworks.LinkedIn)
                return Results.Json(new { error = ErrorMessages.LinkedInNoScheduling }, statusCode: StatusCodes.Status422UnprocessableEntity);

            slot.TargetId = req.TargetId;
            slot.Name = req.Name.Trim();
            slot.Category = req.Category.Trim();
            slot.DayOfWeek = req.DayOfWeek;
            slot.TimeUtcMinutes = req.TimeUtcMinutes;
            slot.IsActive = req.IsActive;
            await db.SaveChangesAsync();
            return Results.Ok(new { id = slot.Id });
        });

        // Deleting a slot never touches the ScheduledPost rows it already filled: those are real
        // scheduled sends the owner can still see and cancel one by one on the calendar.
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.QueueSlots
                .Where(s => s.Id == id && s.OwnerId == uid)
                .ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }

    private static IResult? Invalid(SaveSlotRequest req)
    {
        if (req.DayOfWeek is < 0 or > 6)
            return Results.BadRequest(new { error = ErrorMessages.QueueSlotDayInvalid });
        if (req.TimeUtcMinutes is < 0 or > 1439)
            return Results.BadRequest(new { error = ErrorMessages.QueueSlotTimeInvalid });
        return null;
    }
}
