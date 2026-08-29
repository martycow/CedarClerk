using System.Security.Claims;
using System.Security.Cryptography;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Wave 2 item 16 — tracked short links: /l/{code} answers 302 to the stored URL and bumps a
/// counter. The redirect is anonymous and counts every hit, bots included — the UI says so instead
/// of pretending to filter. Clicks are counters and a per-day tally, never a visit log.
/// </summary>
public static class TrackedLinkEndpoints
{
    public record CreateLinkRequest(string Url, Guid? DraftId = null, string? Network = null);

    public const int CodeLength = 8;
    private const int CollisionRetries = 8;
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Cryptographically random base62, the PrivateAccess shape — unguessable enough that
    /// scanning for codes is not a strategy, short enough to live inside a post.</summary>
    public static string NewCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Base62[RandomNumberGenerator.GetInt32(Base62.Length)];
        return new string(chars);
    }

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Reuse-or-create for an identical (owner, url, draft, network) — asking twice for the same
    /// thing must hand back the same code, or every re-open of the export modal would mint a new
    /// link and split the click counts. Collision-retried against the global unique code index;
    /// <paramref name="codeGen"/> is injectable so the retry is testable.
    /// </summary>
    public static async Task<TrackedLink> CreateOrReuseAsync(CedarDbContext db, string ownerId, string url,
        Guid? draftId, string? network, Func<string>? codeGen = null)
    {
        var existing = await db.TrackedLinks.FirstOrDefaultAsync(l =>
            l.OwnerId == ownerId && l.Url == url && l.DraftId == draftId && l.Network == network);
        if (existing is not null) return existing;

        codeGen ??= NewCode;
        for (var attempt = 0; attempt < CollisionRetries; attempt++)
        {
            var code = codeGen();
            // Codes are unique across the installation, so the check has to look past the tenant
            // filter — an existence probe on a random code, nothing readable leaks.
            var taken = await db.TrackedLinks.IgnoreQueryFilters().AnyAsync(l => l.Code == code);
            if (taken) continue;

            var link = new TrackedLink
            {
                OwnerId = ownerId,
                Code = code,
                Url = url,
                DraftId = draftId,
                Network = network,
            };
            db.TrackedLinks.Add(link);
            await db.SaveChangesAsync();
            return link;
        }

        throw new InvalidOperationException("Could not find a free tracked-link code.");
    }

    public static string ShortUrl(IConfiguration cfg, string code)
    {
        var mainHost = cfg[CedarClerk.Core.Consts.General.MainHostCfg] ?? CedarClerk.Core.Consts.URLs.MainHost;
        return $"{mainHost.TrimEnd('/')}/l/{code}";
    }

    /// <summary>
    /// GET /l/{code} — mapped in Program.cs on the app host, before the SPA fallback. Anonymous:
    /// the code names a link across owners, so the lookup opens its own platform scope the way the
    /// preview page does. Unknown code answers a plain 404.
    /// </summary>
    public static async Task HandleRedirectAsync(HttpContext ctx)
    {
        var code = ctx.Request.RouteValues["code"]?.ToString() ?? "";
        if (code.Length is 0 or > CodeLength * 2)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var scope = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        if (!await RecordClickAndRedirectAsync(db, code, ctx.Response))
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    /// <summary>The redirect's whole decision, on a platform-scoped context so a test drives it
    /// without an HttpContext pipeline. True when the code resolved and the response was written.</summary>
    public static async Task<bool> RecordClickAndRedirectAsync(CedarDbContext db, string code, HttpResponse? response = null)
    {
        var link = await db.TrackedLinks.FirstOrDefaultAsync(l => l.Code == code);
        if (link is null) return false;

        var now = DateTime.UtcNow;
        link.ClickCount++;
        link.LastClickAt = now;

        var day = now.Date;
        var daily = await db.TrackedLinkClickDailies
            .FirstOrDefaultAsync(d => d.TrackedLinkId == link.Id && d.Day == day);
        if (daily is null)
        {
            daily = new TrackedLinkClickDaily { TrackedLinkId = link.Id, OwnerId = link.OwnerId, Day = day };
            db.TrackedLinkClickDailies.Add(daily);
        }
        daily.Clicks++;
        await db.SaveChangesAsync();

        response?.Redirect(link.Url); // 302 — the destination may be edited or retired later
        return true;
    }

    public static void MapTrackedLinkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/links").RequireAuthorization();

        group.MapPost("/", async (CreateLinkRequest req, ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var url = req.Url.Trim();
            if (!IsValidUrl(url))
                return Results.BadRequest(new { error = CedarClerk.Localization.ErrorMessages.TrackedLinkUrlInvalid });

            if (req.DraftId is { } draftId && !await db.Drafts.AnyAsync(d => d.Id == draftId && d.OwnerId == uid))
                return Results.NotFound();

            var link = await CreateOrReuseAsync(db, uid, url, req.DraftId, req.Network);
            return Results.Ok(new { code = link.Code, shortUrl = ShortUrl(cfg, link.Code) });
        });

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg, Guid? draftId = null) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var links = await db.TrackedLinks
                .Where(l => l.OwnerId == uid && (draftId == null || l.DraftId == draftId))
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return Results.Ok(links.Select(l => new
            {
                id = l.Id,
                code = l.Code,
                shortUrl = ShortUrl(cfg, l.Code),
                url = l.Url,
                draftId = l.DraftId,
                network = l.Network,
                clickCount = l.ClickCount,
                lastClickAt = l.LastClickAt,
                createdAt = l.CreatedAt,
            }));
        });
    }
}
