using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server.Analytics;
using CedarClerk.Localization;
using CedarClerk.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// The owner's glossary over HTTP. The rules live in GlossaryEntries and GlossaryAi so they can be
// driven without a request.
public static class GlossaryEndpoints
{
    // ProjectId is nullable and optional: absent means a global term, which is what every term
    // written before T-125 is and what the glossary screen still defaults to.
    public record SetDraftTermRequest(bool Excluded);

    public static void MapGlossaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/glossary").RequireAuthorization();

        // ?projectId= narrows to one project's entries *plus* the global ones — the same set a
        // document in that project renders with. ?scope=global asks for the global ones alone.
        // Without either, everything the owner has, which is what the glossary screen shows.
        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db, Guid? projectId, string? scope) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var query = db.GlossaryEntries.AsNoTracking().Include(e => e.Languages).Where(e => e.OwnerId == uid);

            if (scope == "global") query = query.Where(e => e.ProjectId == null);
            else if (projectId is { } id) query = query.Where(e => e.ProjectId == null || e.ProjectId == id);

            var entries = await query.OrderBy(e => e.Name).ToListAsync();
            // ADR-238 — one grouped read for the whole glossary, merged here; a term nothing uses
            // has no rows and reads as zero.
            var usedIn = await GlossaryUsage.CountsByTermAsync(db, uid);
            // Computed from the rows already in hand, so a zero next to a term another term
            // outspells says which one won instead of reading as "this word is nowhere".
            var rows = await GlossaryEntries.RowsAsync(db.GlossaryEntryLanguages.Where(l => l.OwnerId == uid));
            var shadows = GlossaryUsage.ShadowsByTerm(rows.Select(r => r.ToTermRow()).ToList());
            return Results.Ok(entries.Select(e => GlossaryEntries.ToDto(e, usedIn, shadows)));
        });

        // The Russian forms of a term, proposed rather than applied. Russian inflects, so
        // "рендерер" misses "рендерера"; the author reads the suggestions into the spellings field
        // and deletes what is wrong, which is the only safe way to let a suffix rule near morphology.
        group.MapPost("/suggest-forms", (SuggestFormsRequest req) =>
        {
            var language = req.Language ?? Languages.Russian;
            var forms = language == Languages.Russian
                ? RussianDeclensions.Suggest(req.Term ?? "")
                : [];
            return Results.Ok(new { forms });
        });

        group.MapGet("/for-draft/{draftId:guid}/{language}", async (
            Guid draftId, string language, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!Languages.IsContentLanguage(language)) return Results.BadRequest(new { error = ErrorMessages.UnsupportedLanguage(language) });

            var draft = await db.Drafts.Where(d => d.Id == draftId && d.OwnerId == uid)
                .Select(d => new { d.PrimaryLanguage, d.CedarJson, d.ProjectId }).FirstOrDefaultAsync();
            if (draft is null) return Results.NotFound();

            var cedarJson = draft.CedarJson;
            if (language != draft.PrimaryLanguage)
                cedarJson = await db.DraftTranslations.Where(t => t.DraftId == draftId && t.Language == language)
                    .Select(t => t.CedarJson).FirstOrDefaultAsync() ?? "";

            var text = string.Join('\n', CedarPlainText.Paragraphs(cedarJson));
            var excluded = await db.DraftGlossaryExclusions
                .Where(x => x.DraftId == draftId && x.OwnerId == uid && x.Language == language)
                .Select(x => x.GlossaryTermId).ToListAsync();
            var excludedSet = excluded.ToHashSet();
            var terms = await DraftRowsAsync(db, uid, language, draft.ProjectId);

            return Results.Ok(terms
                .OrderBy(t => t.Term, StringComparer.Ordinal)
                .Where(t => t.Spellings.Prepend(t.Term).Any(spelling => ContainsWholeWord(text, spelling, t.IsCaseSensitive)))
                .Select(t => new { t.Id, t.Term, Excluded = excludedSet.Contains(t.Id) }));
        });

        group.MapPut("/for-draft/{draftId:guid}/{language}/{termId:guid}", async (
            Guid draftId, string language, Guid termId, SetDraftTermRequest req,
            ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.Where(d => d.Id == draftId && d.OwnerId == uid)
                .Select(d => new { d.ProjectId }).FirstOrDefaultAsync();
            if (draft is null) return Results.NotFound();
            var validTerm = await db.GlossaryEntryLanguages.AnyAsync(l => l.Id == termId && l.OwnerId == uid
                && l.Language == language && (l.Entry!.ProjectId == null || l.Entry.ProjectId == draft.ProjectId));
            if (!validTerm) return Results.NotFound();

            var row = await db.DraftGlossaryExclusions.FirstOrDefaultAsync(x => x.DraftId == draftId
                && x.GlossaryTermId == termId && x.Language == language);
            if (req.Excluded && row is null)
                db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion
                    { OwnerId = uid, DraftId = draftId, GlossaryTermId = termId, Language = language });
            else if (!req.Excluded && row is not null)
                db.DraftGlossaryExclusions.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/", (GlossaryEntries.EntryInput req, ClaimsPrincipal user, CedarDbContext db) =>
            GlossaryEntries.CreateAsync(db, user.FindFirstValue(ClaimTypes.NameIdentifier)!, req));

        group.MapPut("/{id:guid}", (Guid id, GlossaryEntries.EntryInput req, ClaimsPrincipal user, CedarDbContext db) =>
            GlossaryEntries.UpdateAsync(db, user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, req));

        group.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
            GlossaryEntries.DeleteAsync(db, user.FindFirstValue(ClaimTypes.NameIdentifier)!, id));

        group.MapPost("/ai/translate", async (GlossaryAi.TranslateRequest req, ClaimsPrincipal user, CedarDbContext db,
            IConfiguration cfg, IHttpClientFactory httpFactory, ProductAnalytics analytics, CancellationToken ct) =>
        {
            if (!TryCreateProvider(cfg, httpFactory, out var provider, out var failure)) return failure!;
            return await GlossaryAi.TranslateAsync(db, user.FindFirstValue(ClaimTypes.NameIdentifier)!, provider, analytics, req, ct);
        });

        group.MapPost("/ai/describe", async (GlossaryAi.DescribeRequest req, ClaimsPrincipal user, CedarDbContext db,
            IConfiguration cfg, IHttpClientFactory httpFactory, ProductAnalytics analytics, MediaPaths media, CancellationToken ct) =>
        {
            if (!TryCreateProvider(cfg, httpFactory, out var provider, out var failure)) return failure!;
            return await GlossaryAi.DescribeAsync(db, user.FindFirstValue(ClaimTypes.NameIdentifier)!, provider, analytics, media, req, ct);
        });

        group.MapPost("/translate-all", async (GlossaryAi.TranslateAllRequest req, ClaimsPrincipal user, CedarDbContext db,
            IConfiguration cfg, IHttpClientFactory httpFactory, ProductAnalytics analytics, CancellationToken ct) =>
        {
            if (!TryCreateProvider(cfg, httpFactory, out var provider, out var failure)) return failure!;
            return await GlossaryAi.TranslateAllAsync(db, user.FindFirstValue(ClaimTypes.NameIdentifier)!, provider, analytics, req, ct);
        });
    }

    private static bool TryCreateProvider(IConfiguration cfg, IHttpClientFactory httpFactory,
        out ITranslationProvider? provider, out IResult? failure)
    {
        failure = null;
        provider = null;
        try
        {
            provider = TranslationProviderFactory.Create(cfg, httpFactory);
            return true;
        }
        catch (TranslationException ex)
        {
            failure = Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            return false;
        }
    }

    private static Task<List<GlossaryEntries.Row>> DraftRowsAsync(CedarDbContext db, string ownerId, string language, Guid? projectId) =>
        GlossaryEntries.RowsAsync(db.GlossaryEntryLanguages
            .Where(l => l.OwnerId == ownerId && l.Language == language
                        && (l.Entry!.ProjectId == null || l.Entry.ProjectId == projectId)));

    private static bool ContainsWholeWord(string text, string candidate, bool caseSensitive)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var from = 0;
        while ((from = text.IndexOf(candidate, from, comparison)) >= 0)
        {
            var before = from == 0 || !IsWordChar(text[from - 1]);
            var afterAt = from + candidate.Length;
            var after = afterAt >= text.Length || !IsWordChar(text[afterAt]);
            if (before && after) return true;
            from++;
        }
        return false;
    }

    private static bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_';

    public record SuggestFormsRequest(string Term, string? Language);
}
