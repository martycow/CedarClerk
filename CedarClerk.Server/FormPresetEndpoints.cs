using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Reusable registration-form definitions (N12). Same treatment as FolderEndpoints: a real named
// entity with its own CRUD file. The stored blob is the client-authored form JSON — the server
// bounds its size and never dictates its shape, exactly like Draft.RegistrationFormJson (ADR-042).
public static class FormPresetEndpoints
{
    // FI4.1 — Language is optional on the wire so an older client (or a hand-rolled call) still
    // creates a primary-language preset rather than failing.
    public record UpsertPresetRequest(string Name, string? FormJson, string? Language = null);

    private const int PresetNameMaxLength = 60;

    // Anything unrecognised means the primary language rather than an error: the language only
    // decides which readers get this form, and rejecting the write would lose the form itself.
    private static string ResolveLanguage(string? lang) =>
        lang is not null && Languages.ContentLanguages.Contains(lang) ? lang : Languages.Primary;

    public static void MapFormPresetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/form-presets").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var presets = await db.FormPresets.Where(p => p.OwnerId == uid)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.FormJson, p.Language, p.CreatedAt })
                .ToListAsync();
            return Results.Ok(presets);
        });

        group.MapPost("/", async (UpsertPresetRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            if (Validate(req) is { } error)
                return error;

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var preset = new FormPreset
            {
                OwnerId = uid,
                Name = req.Name.Trim(),
                FormJson = req.FormJson!,
                Language = ResolveLanguage(req.Language),
            };
            db.FormPresets.Add(preset);
            await db.SaveChangesAsync();
            return Results.Ok(new { preset.Id, preset.Name, preset.FormJson, preset.Language, preset.CreatedAt });
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
            preset.Language = ResolveLanguage(req.Language);
            await db.SaveChangesAsync();
            return Results.Ok(new { preset.Id, preset.Name, preset.FormJson, preset.Language, preset.CreatedAt });
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await db.FormPresets.Where(p => p.Id == id && p.OwnerId == uid).ExecuteDeleteAsync();
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        // ADR-060 — fill one language of a preset by machine-translating its first language.
        // Same gates as post auto-translate (Pro Plus + the daily AI quota); runs synchronously
        // rather than as an AiJob — a form is a handful of short strings, one chunk, seconds.
        group.MapPost("/{id:guid}/translate", async (Guid id, TranslatePresetRequest req, ClaimsPrincipal user,
            CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (req.TargetLanguage is null || !Languages.ContentLanguages.Contains(req.TargetLanguage))
                return Results.BadRequest(new { error = $"Unsupported language: {req.TargetLanguage}" });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var preset = await db.FormPresets.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid, ct);
            if (preset is null) return Results.NotFound();

            var tier = await SubscriptionPlan.EffectiveTierAsync(db, uid);
            if (!PlanLimitations.HasAiFeatures(tier))
                return Results.Json(new { error = "Auto-translate is a Pro Plus feature. Upgrade to use it." }, statusCode: StatusCodes.Status403Forbidden);

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
                return Results.Json(new { error = "Auto-translate is not available with the configured provider" }, statusCode: StatusCodes.Status501NotImplemented);

            var upgraded = RegistrationFormTexts.UpgradeToV2(preset.FormJson, preset.Language);
            var sourceLang = RegistrationFormSet.LanguagesWithForm(upgraded, null).FirstOrDefault() ?? Languages.Primary;
            if (sourceLang == req.TargetLanguage)
                return Results.BadRequest(new { error = "The form is already written in this language" });

            var texts = RegistrationFormTexts.ExtractTexts(upgraded, sourceLang);
            if (texts.All(string.IsNullOrWhiteSpace))
                return Results.BadRequest(new { error = "The form has no text to translate yet" });

            if (!await SubscriptionPlan.TryConsumeAiCallAsync(db, uid))
                return Results.Json(new { error = $"Daily AI limit ({PlanLimitations.AiDailyLimit} calls) reached — resets at midnight UTC." }, statusCode: StatusCodes.Status429TooManyRequests);

            IReadOnlyList<string> translated;
            try
            {
                translated = await textsProvider.TranslateTextsAsync(texts, req.TargetLanguage, ct);
            }
            catch (TranslationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }

            preset.FormJson = RegistrationFormTexts.ReplaceTexts(upgraded, req.TargetLanguage, translated);
            await db.SaveChangesAsync(CancellationToken.None); // the work is done — don't let a disconnect discard it
            return Results.Ok(new { preset.Id, preset.Name, preset.FormJson, preset.Language, preset.CreatedAt });
        });
    }

    public record TranslatePresetRequest(string? TargetLanguage);

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
