using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Post series (ADR-125) — the same management shape as folders: a small named entity the drafts
// point at, deleting it unassigns rather than touches content. What folders don't have and series
// do: a slug (the blog serves /series/{slug}) and a member order.
public static class SeriesEndpoints
{
    public record UpsertSeriesRequest(string Name, string? Description = null);

    private const int SeriesNameMaxLength = 80;

    public static void MapSeriesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/series").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var series = await db.Series.Where(s => s.OwnerId == uid)
                .OrderBy(s => s.Name)
                .ToListAsync();
            var counts = await db.Drafts.Where(d => d.OwnerId == uid && d.SeriesId != null)
                .GroupBy(d => d.SeriesId)
                .Select(g => new { SeriesId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.SeriesId!.Value, g => g.Count);

            return Results.Ok(series.Select(s => new { s.Id, s.Name, s.Slug, s.Description, Count = counts.GetValueOrDefault(s.Id) }));
        });

        group.MapPost("/", async (UpsertSeriesRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var name = req.Name.Trim();
            if (name.Length == 0 || name.Length > SeriesNameMaxLength)
                return Results.Json(new { error = $"Series name must be 1-{SeriesNameMaxLength} characters" }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var slug = SlugGenerator.Slugify(name);
            if (slug.Length == 0)
                return Results.Json(new { error = ErrorMessages.SeriesNameEmptySlug }, statusCode: StatusCodes.Status400BadRequest);

            // The slug carries the public URL, so a duplicate is refused rather than suffixed —
            // the author picked a name that already means another series.
            if (await db.Series.AnyAsync(s => s.OwnerId == uid && s.Slug == slug))
                return Results.Json(new { error = ErrorMessages.SeriesNameTaken }, statusCode: StatusCodes.Status409Conflict);

            var series = new Series { OwnerId = uid, Name = name, Slug = slug, Description = req.Description?.Trim() };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            return Results.Ok(new { series.Id, series.Name, series.Slug, series.Description });
        });

        // Rename keeps the slug: the URL is a promise already shared in published posts, and a
        // rename must not break every link the series ever earned.
        group.MapPut("/{id:guid}", async (Guid id, UpsertSeriesRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var name = req.Name.Trim();
            if (name.Length == 0 || name.Length > SeriesNameMaxLength)
                return Results.Json(new { error = $"Series name must be 1-{SeriesNameMaxLength} characters" }, statusCode: StatusCodes.Status400BadRequest);

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var series = await db.Series.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (series is null) return Results.NotFound();

            series.Name = name;
            series.Description = req.Description?.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(new { series.Id, series.Name, series.Slug, series.Description });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var series = await db.Series.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
            if (series is null) return Results.NotFound();

            await db.Drafts.Where(d => d.SeriesId == id && d.OwnerId == uid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.SeriesId, d => null)
                    .SetProperty(d => d.SeriesOrder, d => null));

            db.Series.Remove(series);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
