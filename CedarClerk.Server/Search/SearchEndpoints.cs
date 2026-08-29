using System.Security.Claims;

namespace CedarClerk.Server.Search;

/// <summary>
/// Slugs the blog host answers for itself, so no post may ever claim them. The refusal lives in
/// BlogEndpoints.GenerateUniqueSlugAsync; the list lives here so route and refusal share one fact.
/// </summary>
public static class BlogReservedSlugs
{
    public static readonly string[] All = ["search", "subscribe", "sitemap.xml"];
}

// Wave 1 item 2 — the app-side document search behind the Ctrl+K overlay. Owner-scoped: the
// index is shared across tenants, the query names the caller and nothing else.
public static class SearchEndpoints
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;
    private const int QueryMaxLength = 200;

    public static void MapSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/search").RequireAuthorization();

        group.MapGet("/drafts", async (string? q, int? limit, ClaimsPrincipal user, IDraftSearchIndex index, CancellationToken ct) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var query = (q ?? "").Trim();
            if (query.Length == 0) return Results.Ok(Array.Empty<DraftSearchHit>());
            if (query.Length > QueryMaxLength) query = query[..QueryMaxLength];

            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            return Results.Ok(await index.SearchDraftsAsync(uid, query, take, ct));
        });
    }
}
