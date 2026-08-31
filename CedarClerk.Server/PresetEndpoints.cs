using System.Security.Claims;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// T-331/T-355 — the Preset Manager's API. Owner-scoped CRUD over the Presets table. Document
// presets are the only kind wired today; the endpoint validates the config per kind so a project
// or export preset can join without reshaping this.
public static class PresetEndpoints
{
    public record SavePresetRequest(string Kind, string Name, string? BaseType, string? Icon, string[]? Headings);

    private const int MaxName = 60;
    private static readonly HashSet<string> Kinds = ["document"];

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
        if (!Kinds.Contains(req.Kind))
            return Results.BadRequest(new { error = ErrorMessages.UnknownPresetKind });
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > MaxName)
            return Results.BadRequest(new { error = ErrorMessages.PresetNameInvalid });
        return null;
    }

    // For a document preset, the config is validated and normalised through DocumentPresetConfig,
    // so a bad base type or an overlong heading list cannot reach the database.
    private static string BuildConfig(SavePresetRequest req)
    {
        var cfg = DocumentPresetConfig.Parse(new JsonObject
        {
            ["baseType"] = req.BaseType,
            ["icon"] = req.Icon,
            ["headings"] = new JsonArray((req.Headings ?? []).Select(h => JsonValue.Create(h)).ToArray()),
        }.ToJsonString());
        return new JsonObject
        {
            ["baseType"] = cfg.BaseType,
            ["icon"] = cfg.Icon,
            ["headings"] = new JsonArray(cfg.Headings.Select(h => JsonValue.Create(h)).ToArray()),
        }.ToJsonString();
    }
}
