using System.Security.Claims;
using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Reusable registration-form definitions (N12). Same treatment as FolderEndpoints: a real named
// entity with its own CRUD file. The stored blob is the client-authored form JSON — the server
// bounds its size and never dictates its shape, exactly like Draft.RegistrationFormJson (ADR-042).
public static class FormPresetEndpoints
{
    public record UpsertPresetRequest(string Name, string? FormJson);

    private const int PresetNameMaxLength = 60;

    public static void MapFormPresetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/form-presets").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var presets = await db.FormPresets.Where(p => p.OwnerId == uid)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.FormJson, p.CreatedAt })
                .ToListAsync();
            return Results.Ok(presets);
        });

        group.MapPost("/", async (UpsertPresetRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error)
                return error;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var preset = new FormPreset { OwnerId = uid, Name = req.Name.Trim(), FormJson = req.FormJson! };
            db.FormPresets.Add(preset);
            await db.SaveChangesAsync();
            return Results.Ok(new { preset.Id, preset.Name, preset.FormJson, preset.CreatedAt });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertPresetRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error)
                return error;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var preset = await db.FormPresets.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (preset is null) return Results.NotFound();

            preset.Name = req.Name.Trim();
            preset.FormJson = req.FormJson!;
            await db.SaveChangesAsync();
            return Results.Ok(new { preset.Id, preset.Name, preset.FormJson, preset.CreatedAt });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.FormPresets.Where(p => p.Id == id && p.OwnerId == uid).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }

    private static IResult? Validate(UpsertPresetRequest req)
    {
        var name = req.Name.Trim();
        if (name.Length == 0 || name.Length > PresetNameMaxLength)
            return Results.Json(new { error = $"Preset name must be 1-{PresetNameMaxLength} characters" }, statusCode: StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(req.FormJson))
            return Results.Json(new { error = "Preset has no form" }, statusCode: StatusCodes.Status400BadRequest);

        if (req.FormJson.Length > Consts.RegistrationForm.FormJsonMaxChars)
            return Results.Json(new { error = "Form is too large" }, statusCode: StatusCodes.Status400BadRequest);

        return null;
    }
}
