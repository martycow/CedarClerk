using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Idea #11 — the owner's glossary. Same treatment as FolderEndpoints/FormPresetEndpoints: a real
// named entity with its own CRUD file, everything scoped by OwnerId.
public static class GlossaryEndpoints
{
    // ProjectId is nullable and optional: absent means a global term, which is what every term
    // written before T-125 is and what the glossary screen still defaults to.
    public record UpsertTermRequest(string Term, string Description, string? Aliases, string? ImageUrl, string? Language, bool IsCaseSensitive = false, Guid? ProjectId = null);
    public record SetDraftTermRequest(bool Excluded);

    private const int TermMaxLength = 80;
    private const int DescriptionMaxLength = 1000;
    private const int AliasesMaxLength = 400;

    public static void MapGlossaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/glossary").RequireAuthorization();

        // ?projectId= narrows to one project's terms *plus* the global ones — the same set a
        // document in that project renders with. ?scope=global asks for the global ones alone.
        // Without either, everything the owner has, which is what the glossary screen shows.
        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db, Guid? projectId, string? scope) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var query = db.GlossaryTerms.Where(t => t.OwnerId == uid);

            if (scope == "global") query = query.Where(t => t.ProjectId == null);
            else if (projectId is { } id) query = query.Where(t => t.ProjectId == null || t.ProjectId == id);

            var terms = await query
                .OrderBy(t => t.Term)
                .Select(t => new { t.Id, t.Term, t.Description, t.Aliases, t.ImageUrl, t.Language, t.IsCaseSensitive, t.SourceTermId, t.ProjectId, t.UpdatedAt })
                .ToListAsync();
            return Results.Ok(terms);
        });

        // T-040 — the Russian forms of a term, proposed rather than applied. Russian inflects, so
        // "рендерер" misses "рендерера"; the author reads the suggestions into the alias field and
        // deletes what is wrong, which is the only safe way to let a suffix rule near morphology.
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
            if (!Languages.IsContentLanguage(language)) return Results.BadRequest(new { error = $"Unsupported language: {language}" });

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
            var terms = await db.GlossaryTerms
                .Where(t => t.OwnerId == uid && t.Language == language
                            && (t.ProjectId == null || t.ProjectId == draft.ProjectId))
                .OrderBy(t => t.Term)
                .Select(t => new { t.Id, t.Term, t.Aliases, t.IsCaseSensitive })
                .ToListAsync();

            return Results.Ok(terms
                .Where(t => t.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Prepend(t.Term).Any(alias => ContainsWholeWord(text, alias, t.IsCaseSensitive)))
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
            var validTerm = await db.GlossaryTerms.AnyAsync(t => t.Id == termId && t.OwnerId == uid
                && t.Language == language && (t.ProjectId == null || t.ProjectId == draft.ProjectId));
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

        group.MapPost("/", async (UpsertTermRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error) return error;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var term = new GlossaryTerm
            {
                OwnerId = uid,
                Term = req.Term.Trim(),
                Description = req.Description.Trim(),
                Aliases = NormalizeAliases(req.Aliases),
                IsCaseSensitive = req.IsCaseSensitive,
                ImageUrl = NormalizeImage(req.ImageUrl),
                Language = ResolveLanguage(req.Language),
                ProjectId = req.ProjectId,
            };
            db.GlossaryTerms.Add(term);
            await db.SaveChangesAsync();
            return Results.Ok(new { term.Id, term.Term, term.Description, term.Aliases, term.ImageUrl, term.Language, term.ProjectId, term.UpdatedAt });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpsertTermRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error) return error;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var term = await db.GlossaryTerms.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid);
            if (term is null) return Results.NotFound();

            term.Term = req.Term.Trim();
            term.Description = req.Description.Trim();
            term.Aliases = NormalizeAliases(req.Aliases);
            term.IsCaseSensitive = req.IsCaseSensitive;
            term.ImageUrl = NormalizeImage(req.ImageUrl);
            term.Language = ResolveLanguage(req.Language);
            // Moving a term between scopes is an edit, not a re-entry — which is half of why the
            // scope is a column on the same row rather than a second entity (ADR-112).
            term.ProjectId = req.ProjectId;
            term.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { term.Id, term.Term, term.Description, term.Aliases, term.ImageUrl, term.Language, term.ProjectId, term.UpdatedAt });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await db.DraftGlossaryExclusions
                .Where(x => x.GlossaryTermId == id && x.OwnerId == uid).ExecuteDeleteAsync();
            var deleted = await db.GlossaryTerms.Where(t => t.Id == id && t.OwnerId == uid).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        // ADR-061 — copy one term into another language by machine translation. Same gates and
        // synchronous shape as the form-preset endpoint (ADR-060); a term is two short strings.
        // Aliases are NOT translated — they cover one language's inflections and would come back
        // as noise in another. The image is copied: a picture is language-neutral.
        group.MapPost("/{id:guid}/translate", async (Guid id, TranslateTermRequest req, ClaimsPrincipal user,
            CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (req.TargetLanguage is null || !Languages.ContentLanguages.Contains(req.TargetLanguage))
                return Results.BadRequest(new { error = $"Unsupported language: {req.TargetLanguage}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var source = await db.GlossaryTerms.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid, ct);
            if (source is null) return Results.NotFound();
            if (source.Language == req.TargetLanguage)
                return Results.BadRequest(new { error = ErrorMessages.TermAlreadyInLanguage });

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = ErrorMessages.AutoTranslateProPlus }, statusCode: StatusCodes.Status403Forbidden);

            ITranslationProvider? provider;
            try
            {
                provider = TranslationProviderFactory.Create(cfg, httpFactory);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is not ITextsTranslationProvider textsProvider)
                return Results.Json(new { error = ErrorMessages.AutoTranslateNoProvider }, statusCode: StatusCodes.Status501NotImplemented);
            // T-013 — asked before the quota is charged; see the same check in DraftEndpoints.
            if (!provider.SupportsTargetLanguage(req.TargetLanguage))
                return Results.Json(new { error = ErrorMessages.LanguageNotSupportedByProvider(req.TargetLanguage, provider.Name) },
                    statusCode: StatusCodes.Status501NotImplemented);

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = ErrorMessages.AiDailyLimitReached(PlanLimitations.AiDailyLimit) }, statusCode: StatusCodes.Status429TooManyRequests);

            IReadOnlyList<string> translated;
            try
            {
                translated = await textsProvider.TranslateTextsAsync(
                    new[] { source.Term, source.Description }, req.TargetLanguage, ct);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }

            var newTerm = translated[0].Trim();
            var newDescription = translated[1].Trim();
            if (newTerm.Length == 0 || newTerm.Length > TermMaxLength || newDescription.Length == 0)
                return Results.Json(new { error = ErrorMessages.TranslationUnusable }, statusCode: StatusCodes.Status502BadGateway);
            if (newDescription.Length > DescriptionMaxLength)
                newDescription = newDescription[..DescriptionMaxLength];

            // Upsert by translated term text so a second press refreshes instead of duplicating
            // (MT is stable enough that the same source yields the same term). ADR-061.
            var existing = await db.GlossaryTerms.FirstOrDefaultAsync(t =>
                t.OwnerId == uid && t.Language == req.TargetLanguage && t.Term.ToLower() == newTerm.ToLower(), ct);
            GlossaryTerm term;
            if (existing is not null)
            {
                existing.Description = newDescription;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.SourceTermId ??= source.SourceTermId ?? source.Id;
                term = existing;
            }
            else
            {
                term = new GlossaryTerm
                {
                    OwnerId = uid,
                    Term = newTerm,
                    Description = newDescription,
                    Aliases = "",
                    ImageUrl = source.ImageUrl,
                    Language = req.TargetLanguage,
                    // The group root, so a chain of translations stays one group rather than a tree.
                    SourceTermId = source.SourceTermId ?? source.Id,
                };
                db.GlossaryTerms.Add(term);
            }
            await db.SaveChangesAsync(CancellationToken.None); // the work is done — don't let a disconnect discard it
            return Results.Ok(new { term.Id, term.Term, term.Description, term.Aliases, term.ImageUrl, term.Language, term.UpdatedAt });
        });
        // ADR-062 — the whole-language sweep: every term of sourceLanguage into targetLanguage
        // for one quota call and one (chunked) provider call, instead of one per term. The
        // frontend still loops per target language, so quota and errors stay per-language.
        group.MapPost("/translate-all", async (TranslateAllRequest req, ClaimsPrincipal user,
            CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (req.SourceLanguage is null || !Languages.ContentLanguages.Contains(req.SourceLanguage))
                return Results.BadRequest(new { error = $"Unsupported language: {req.SourceLanguage}" });
            if (req.TargetLanguage is null || !Languages.ContentLanguages.Contains(req.TargetLanguage))
                return Results.BadRequest(new { error = $"Unsupported language: {req.TargetLanguage}" });
            if (req.SourceLanguage == req.TargetLanguage)
                return Results.BadRequest(new { error = ErrorMessages.SameSourceAndTarget });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var sources = await db.GlossaryTerms
                .Where(t => t.OwnerId == uid && t.Language == req.SourceLanguage)
                .OrderBy(t => t.Term)
                .ToListAsync(ct);
            if (sources.Count == 0)
                return Results.BadRequest(new { error = ErrorMessages.NoTermsInLanguage });

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = ErrorMessages.AutoTranslateProPlus }, statusCode: StatusCodes.Status403Forbidden);

            ITranslationProvider? provider;
            try
            {
                provider = TranslationProviderFactory.Create(cfg, httpFactory);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
            }
            if (provider is not ITextsTranslationProvider textsProvider)
                return Results.Json(new { error = ErrorMessages.AutoTranslateNoProvider }, statusCode: StatusCodes.Status501NotImplemented);
            // T-013 — asked before the quota is charged; see the same check in DraftEndpoints.
            if (!provider.SupportsTargetLanguage(req.TargetLanguage))
                return Results.Json(new { error = ErrorMessages.LanguageNotSupportedByProvider(req.TargetLanguage, provider.Name) },
                    statusCode: StatusCodes.Status501NotImplemented);

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = ErrorMessages.AiDailyLimitReached(PlanLimitations.AiDailyLimit) }, statusCode: StatusCodes.Status429TooManyRequests);

            IReadOnlyList<string> translated;
            try
            {
                translated = await textsProvider.TranslateTextsAsync(
                    sources.SelectMany(t => new[] { t.Term, t.Description }).ToList(), req.TargetLanguage, ct);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }

            // One query for the whole target language; terms created below join the map so two
            // sources translating to the same word update one row instead of duplicating. ADR-062.
            var byLowerTerm = (await db.GlossaryTerms
                    .Where(t => t.OwnerId == uid && t.Language == req.TargetLanguage)
                    .ToListAsync(ct))
                .GroupBy(t => t.Term.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            var upserted = new List<GlossaryTerm>();
            var skipped = 0;
            for (var i = 0; i < sources.Count; i++)
            {
                var newTerm = translated[i * 2].Trim();
                var newDescription = translated[i * 2 + 1].Trim();
                if (newTerm.Length == 0 || newTerm.Length > TermMaxLength || newDescription.Length == 0)
                {
                    skipped++;
                    continue;
                }
                if (newDescription.Length > DescriptionMaxLength)
                    newDescription = newDescription[..DescriptionMaxLength];

                if (byLowerTerm.TryGetValue(newTerm.ToLowerInvariant(), out var existing))
                {
                    existing.Description = newDescription;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.SourceTermId ??= sources[i].SourceTermId ?? sources[i].Id;
                    upserted.Add(existing);
                }
                else
                {
                    var term = new GlossaryTerm
                    {
                        OwnerId = uid,
                        Term = newTerm,
                        Description = newDescription,
                        Aliases = "",
                        ImageUrl = sources[i].ImageUrl,
                        Language = req.TargetLanguage,
                        SourceTermId = sources[i].SourceTermId ?? sources[i].Id,
                    };
                    db.GlossaryTerms.Add(term);
                    byLowerTerm[newTerm.ToLowerInvariant()] = term;
                    upserted.Add(term);
                }
            }
            await db.SaveChangesAsync(CancellationToken.None); // the work is done — don't let a disconnect discard it
            return Results.Ok(new
            {
                terms = upserted.Select(t => new { t.Id, t.Term, t.Description, t.Aliases, t.ImageUrl, t.Language, t.IsCaseSensitive, t.SourceTermId, t.UpdatedAt }),
                skipped,
            });
        });
    }

    public record TranslateTermRequest(string? TargetLanguage);
    public record TranslateAllRequest(string? SourceLanguage, string? TargetLanguage);

    /// <summary>
    /// The glossary a blog page renders with: one owner's terms in the language being shown.
    /// Empty is the normal case for an owner who has never defined one, and costs one indexed
    /// read per page.
    ///
    /// T-125 (ADR-112) — <paramref name="projectId"/> is the document's project. Global terms plus
    /// that project's, and **the project's wins** where both define the same word: a narrower
    /// scope is a more precise definition, which is the reason to write one.
    /// </summary>
    internal static async Task<IReadOnlyList<GlossaryEntry>> LoadForAsync(
        CedarDbContext db, string ownerId, string language, Guid? projectId = null, Guid? draftId = null)
    {
        var excluded = draftId is null
            ? []
            : await db.DraftGlossaryExclusions
                .Where(x => x.OwnerId == ownerId && x.DraftId == draftId && x.Language == language)
                .Select(x => x.GlossaryTermId).ToListAsync();
        var rows = await db.GlossaryTerms
            .Where(t => t.OwnerId == ownerId && t.Language == language
                        && (t.ProjectId == null || t.ProjectId == projectId) && !excluded.Contains(t.Id))
            .Select(t => new { t.Term, t.Description, t.Aliases, t.ImageUrl, t.IsCaseSensitive, t.ProjectId })
            .ToListAsync();

        return rows
            // Project terms first, so DistinctBy below keeps them over a global term of the same
            // name. Comparison is case-insensitive because the scanner matches that way too.
            .OrderByDescending(r => r.ProjectId.HasValue)
            .DistinctBy(r => r.Term, StringComparer.OrdinalIgnoreCase)
            .Select(r => new GlossaryEntry(
                r.Term,
                r.Description,
                r.ImageUrl,
                r.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                r.IsCaseSensitive))
            .ToList();
    }

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

    private static IResult? Validate(UpsertTermRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Term))
            return Results.BadRequest(new { error = ErrorMessages.TermRequired });
        if (req.Term.Trim().Length > TermMaxLength)
            return Results.BadRequest(new { error = $"Term is too long ({TermMaxLength} characters maximum)" });
        if (string.IsNullOrWhiteSpace(req.Description))
            return Results.BadRequest(new { error = ErrorMessages.DescriptionRequired });
        if (req.Description.Trim().Length > DescriptionMaxLength)
            return Results.BadRequest(new { error = $"Description is too long ({DescriptionMaxLength} characters maximum)" });
        if (req.Aliases is { Length: > AliasesMaxLength })
            return Results.BadRequest(new { error = $"Aliases are too long ({AliasesMaxLength} characters maximum)" });
        return null;
    }

    private static string NormalizeAliases(string? aliases) =>
        aliases is null
            ? ""
            : string.Join(",", aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // Same rule as the avatar endpoint: only a path this server produced. Accepting an arbitrary
    // URL would let a glossary tooltip point the blog's own chrome at someone else's server.
    private static string? NormalizeImage(string? imageUrl)
    {
        var url = imageUrl?.Trim();
        if (string.IsNullOrEmpty(url)) return null;
        return url.StartsWith("/media/", StringComparison.Ordinal) ? url : null;
    }

    private static string ResolveLanguage(string? lang) =>
        lang is not null && Languages.ContentLanguages.Contains(lang) ? lang : Languages.Russian;
}
