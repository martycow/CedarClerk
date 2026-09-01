using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// T-331/T-355 — the Preset Manager's API. Owner-scoped CRUD over the Presets table, three kinds
// over one row shape: a document preset (a starting point for a new document), a project preset
// (the New-project dialog's built-ins made editable) and an export preset (a saved set of
// destinations). The kind decides which config record reads and rewrites ConfigJson, so nothing
// stored can be read through the wrong shape.
public static class PresetEndpoints
{
    // Config arrives as free-form JSON and leaves normalised: every kind's record drops what it
    // does not know, so an unvalidated value cannot reach the database through a field this
    // record forgot to name.
    public record SavePresetRequest(string Kind, string Name, JsonElement? Config);

    private const int MaxName = 60;
    private const int MaxPerKind = 50;

    public static void MapPresetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/presets").RequireAuthorization();

        group.MapGet("/", async (string? kind, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var query = db.Presets.Where(p => p.OwnerId == uid);
            if (kind is { Length: > 0 }) query = query.Where(p => p.Kind == kind);
            var presets = await query
                .OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt)
                .Select(p => new { p.Id, p.Kind, p.Name, p.ConfigJson, p.SortOrder })
                .ToListAsync();
            return Results.Ok(presets);
        });

        group.MapPost("/", async (SavePresetRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error) return error;
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var count = await db.Presets.CountAsync(p => p.OwnerId == uid && p.Kind == req.Kind);
            if (count >= MaxPerKind)
                return Results.BadRequest(new { error = ErrorMessages.PresetLimitReached(MaxPerKind) });

            var preset = new Preset
            {
                OwnerId = uid,
                Kind = req.Kind,
                Name = req.Name.Trim(),
                ConfigJson = BuildConfig(req),
                SortOrder = count,
            };
            db.Presets.Add(preset);
            await db.SaveChangesAsync();
            return Results.Ok(new { preset.Id, preset.Kind, preset.Name, preset.ConfigJson, preset.SortOrder });
        });

        group.MapPut("/{id:guid}", async (Guid id, SavePresetRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error) return error;
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var preset = await db.Presets.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid);
            if (preset is null) return Results.NotFound();
            // The kind is fixed at creation: it decides how ConfigJson is read, and a row whose
            // kind moved under a config written for the old one is a row nothing can read.
            if (preset.Kind != req.Kind)
                return Results.BadRequest(new { error = ErrorMessages.PresetKindImmutable });

            preset.Name = req.Name.Trim();
            preset.ConfigJson = BuildConfig(req);
            await db.SaveChangesAsync();
            return Results.Ok(new { preset.Id, preset.Kind, preset.Name, preset.ConfigJson, preset.SortOrder });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.Presets.Where(p => p.Id == id && p.OwnerId == uid).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });
    }

    private static IResult? Validate(SavePresetRequest req)
    {
        if (!PresetKinds.IsKnown(req.Kind))
            return Results.BadRequest(new { error = ErrorMessages.UnknownPresetKind });
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > MaxName)
            return Results.BadRequest(new { error = ErrorMessages.PresetNameInvalid });
        return null;
    }

    /// <summary>
    /// The config as its kind's record understands it, re-serialised from that record rather than
    /// from the request — so a bad base type, an unknown destination or an overlong heading list
    /// is dropped on the way in, not tolerated on the way out.
    /// </summary>
    private static string BuildConfig(SavePresetRequest req)
    {
        var raw = req.Config?.ValueKind is JsonValueKind.Object ? req.Config.Value.GetRawText() : null;
        return req.Kind switch
        {
            PresetKinds.Project => ProjectPresetConfig.Parse(raw).ToJson(),
            PresetKinds.Export => ExportPresetConfig.Parse(raw).ToJson(),
            _ => DocumentPresetConfig.Parse(raw).ToJson(),
        };
    }
}
