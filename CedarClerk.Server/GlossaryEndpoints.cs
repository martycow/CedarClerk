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
    public record UpsertTermRequest(string Term, string Description, string? Aliases, string? ImageUrl, string? Language);

    private const int TermMaxLength = 80;
    private const int DescriptionMaxLength = 1000;
    private const int AliasesMaxLength = 400;

    public static void MapGlossaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/glossary").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var terms = await db.GlossaryTerms.Where(t => t.OwnerId == uid)
                .OrderBy(t => t.Term)
                .Select(t => new { t.Id, t.Term, t.Description, t.Aliases, t.ImageUrl, t.Language, t.UpdatedAt })
                .ToListAsync();
            return Results.Ok(terms);
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
                ImageUrl = NormalizeImage(req.ImageUrl),
                Language = ResolveLanguage(req.Language),
            };
            db.GlossaryTerms.Add(term);
            await db.SaveChangesAsync();
            return Results.Ok(new { term.Id, term.Term, term.Description, term.Aliases, term.ImageUrl, term.Language, term.UpdatedAt });
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
            term.ImageUrl = NormalizeImage(req.ImageUrl);
            term.Language = ResolveLanguage(req.Language);
            term.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { term.Id, term.Term, term.Description, term.Aliases, term.ImageUrl, term.Language, term.UpdatedAt });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
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
                return Results.BadRequest(new { error = "The term is already in this language" });

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
                return Results.Json(new { error = "The translation came back unusable — try again" }, statusCode: StatusCodes.Status502BadGateway);
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
                };
                db.GlossaryTerms.Add(term);
            }
            await db.SaveChangesAsync(CancellationToken.None); // the work is done — don't let a disconnect discard it
            return Results.Ok(new { term.Id, term.Term, term.Description, term.Aliases, term.ImageUrl, term.Language, term.UpdatedAt });
        });
    }

    public record TranslateTermRequest(string? TargetLanguage);

    /// <summary>
    /// The glossary a blog page renders with: one owner's terms in the language being shown.
    /// Empty is the normal case for an owner who has never defined one, and costs one indexed
    /// read per page.
    /// </summary>
    internal static async Task<IReadOnlyList<GlossaryEntry>> LoadForAsync(CedarDbContext db, string ownerId, string language)
    {
        var rows = await db.GlossaryTerms
            .Where(t => t.OwnerId == ownerId && t.Language == language)
            .Select(t => new { t.Term, t.Description, t.Aliases, t.ImageUrl })
            .ToListAsync();

        return rows.Select(r => new GlossaryEntry(
            r.Term,
            r.Description,
            r.ImageUrl,
            r.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))).ToList();
    }

    private static IResult? Validate(UpsertTermRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Term))
            return Results.BadRequest(new { error = "A term is required" });
        if (req.Term.Trim().Length > TermMaxLength)
            return Results.BadRequest(new { error = $"Term is too long ({TermMaxLength} characters maximum)" });
        if (string.IsNullOrWhiteSpace(req.Description))
            return Results.BadRequest(new { error = "A description is required" });
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
        lang is not null && Languages.ContentLanguages.Contains(lang) ? lang : Languages.Primary;
}
