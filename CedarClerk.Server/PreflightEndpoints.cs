using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// The pre-publish checklist (T-238): things worth knowing before a send, never reasons to refuse
/// one. Everything here is a warning — the publish fires regardless — which is also why the
/// dead-link probe is best-effort with a hard time cap. Alt-text and capability warnings come
/// from the existing /api/posts/validate feed; this endpoint owns what needs the stored versions:
/// an empty ticked language and links that no longer answer.
/// </summary>
public static class PreflightEndpoints
{
    public record PreflightRequest(Guid DraftId, string[] Languages);
    public record PreflightLanguage(string Language, bool EmptyVersion, IReadOnlyList<DeadLink> DeadLinks);
    public record PreflightResponse(IReadOnlyList<PreflightLanguage> PerLanguage);

    public static IServiceCollection AddPreflightServices(this IServiceCollection services)
    {
        services.AddHttpClient<LinkCheckService>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("CedarClerk-LinkCheck/1.0");
        }).ConfigurePrimaryHttpMessageHandler(LinkCheckService.CreateHandler);
        return services;
    }

    public static void MapPreflightEndpoints(this WebApplication app)
    {
        app.MapPost("/api/posts/preflight", async (PreflightRequest req, ClaimsPrincipal user,
            CedarDbContext db, LinkCheckService linkCheck, CancellationToken ct) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == req.DraftId && d.OwnerId == uid, ct);
            if (draft is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });

            return Results.Ok(await RunAsync(db, draft, req.Languages, linkCheck, ct));
        }).RequireAuthorization();
    }

    public static async Task<PreflightResponse> RunAsync(CedarDbContext db, Draft draft,
        IEnumerable<string> languages, LinkCheckService linkCheck, CancellationToken ct = default)
    {
        var perLanguage = new List<PreflightLanguage>();
        // One verdict per URL across the whole call: the same link in two translations is one probe.
        var verdicts = new Dictionary<string, DeadLink?>(StringComparer.Ordinal);

        foreach (var language in languages.Distinct())
        {
            var document = await DraftRevisionService.ResolveAsync(db, draft, language, ct);
            if (document is null)
            {
                perLanguage.Add(new PreflightLanguage(language, true, []));
                continue;
            }

            var cedarJson = document.Value.CedarJson;
            var empty = CedarPlainText.Paragraphs(cedarJson).All(string.IsNullOrWhiteSpace);

            var links = CedarLinkScan.CollectLinks(cedarJson).Take(LinkCheckService.MaxLinks).ToList();
            var fresh = links.Where(l => !verdicts.ContainsKey(l)).ToList();
            if (fresh.Count > 0)
            {
                foreach (var url in fresh) verdicts[url] = null;
                foreach (var dead in await linkCheck.CheckAsync(fresh, ct)) verdicts[dead.Url] = dead;
            }

            var deadLinks = links.Select(l => verdicts[l]).OfType<DeadLink>().ToList();
            perLanguage.Add(new PreflightLanguage(language, empty, deadLinks));
        }

        return new PreflightResponse(perLanguage);
    }
}
