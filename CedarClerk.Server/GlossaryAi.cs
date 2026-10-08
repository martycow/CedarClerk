using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// ADR-320 — the glossary's paid calls. Each answers with a proposal the owner reads in the entry
// form before saving; only the whole-language sweep writes rows itself.
public static class GlossaryAi
{
    // Anthropic refuses an image over 5 MB once base64-encoded, which is 3.75 MB of file.
    public const long ImageMaxBytes = 3_700_000;

    private static readonly HashSet<string> ImageTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    public record TranslateRequest(string? Name, string? Description, string? SourceLanguage, string? TargetLanguage);
    public record DescribeRequest(string? Name, string? Language, string? ImageUrl);
    public record TranslateAllRequest(string? SourceLanguage, string? TargetLanguage);

    public static async Task<IResult> TranslateAsync(CedarDbContext db, string ownerId, ITranslationProvider? provider,
        ProductAnalytics analytics, TranslateRequest req, CancellationToken ct)
    {
        if (req.TargetLanguage is null || !Languages.IsContentLanguage(req.TargetLanguage))
            return Results.BadRequest(new { error = ErrorMessages.UnsupportedLanguage(req.TargetLanguage) });
        if (req.SourceLanguage == req.TargetLanguage)
            return Results.BadRequest(new { error = ErrorMessages.SameSourceAndTarget });
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0) return Results.BadRequest(new { error = ErrorMessages.TermRequired });
        if (name.Length > GlossaryEntries.NameMaxLength)
            return Results.BadRequest(new { error = ErrorMessages.TermTooLong(GlossaryEntries.NameMaxLength) });
        var description = req.Description?.Trim() ?? "";
        if (description.Length > GlossaryEntries.DescriptionMaxLength)
            return Results.BadRequest(new { error = ErrorMessages.DescriptionTooLong(GlossaryEntries.DescriptionMaxLength) });

        if (await RefuseTranslateAsync(db, ownerId, provider, req.TargetLanguage) is { } refusal) return refusal;
        var ai = (IGlossaryAiProvider)provider!;

        if (await SubscriptionPlan.ChargeAiOrRefuseAsync(db, ownerId, CreditPacks.AiSmallCost, analytics, "glossary_term") is { } unpaid)
            return unpaid;

        GlossaryTermTranslation translated;
        try
        {
            translated = (await ai.TranslateTermsAsync([new GlossaryTermSource(name, description)], req.TargetLanguage, ct))[0];
        }
        catch (TranslationException ex)
        {
            return await SubscriptionPlan.RefundAiAndFailAsync(db, ownerId, CreditPacks.AiSmallCost, ex.Message);
        }

        if (!Usable(translated))
            return await SubscriptionPlan.RefundAiAndFailAsync(db, ownerId, CreditPacks.AiSmallCost, ErrorMessages.TranslationUnusable);

        return Results.Ok(new
        {
            language = req.TargetLanguage,
            localizedName = translated.Name.Trim(),
            spellings = FitSpellings(translated),
            localizedDescription = Clip(translated.Description),
        });
    }

    public static async Task<IResult> DescribeAsync(CedarDbContext db, string ownerId, ITranslationProvider? provider,
        ProductAnalytics analytics, MediaPaths media, DescribeRequest req, CancellationToken ct)
    {
        if (req.Language is null || !Languages.IsContentLanguage(req.Language))
            return Results.BadRequest(new { error = ErrorMessages.UnsupportedLanguage(req.Language) });
        var name = req.Name?.Trim() ?? "";
        var imageUrl = GlossaryEntries.NormalizeImage(req.ImageUrl);
        if (name.Length == 0 && imageUrl is null) return Results.BadRequest(new { error = ErrorMessages.TermRequired });
        if (name.Length > GlossaryEntries.NameMaxLength)
            return Results.BadRequest(new { error = ErrorMessages.TermTooLong(GlossaryEntries.NameMaxLength) });

        var tier = await SubscriptionPlan.EffectiveTierAsync(db, ownerId);
        if (!PlanLimitations.HasAiFeatures(tier))
            return Results.Json(new { error = ErrorMessages.AiDescribeProPlus }, statusCode: StatusCodes.Status403Forbidden);
        if (provider is not IGlossaryAiProvider ai)
            return Results.Json(new { error = ErrorMessages.AiDescribeNoProvider }, statusCode: StatusCodes.Status501NotImplemented);

        AiImage? image = null;
        if (imageUrl is not null)
        {
            image = await LoadImageAsync(db, ownerId, media, imageUrl, ct);
            if (image is null) return Results.BadRequest(new { error = ErrorMessages.GlossaryImageUnusable });
        }

        var cost = image is null ? CreditPacks.AiSmallCost : CreditPacks.AiImageDescribeCost;
        var kind = image is null ? "glossary_describe" : "glossary_describe_image";
        if (await SubscriptionPlan.ChargeAiOrRefuseAsync(db, ownerId, cost, analytics, kind) is { } unpaid)
            return unpaid;

        string description;
        try
        {
            description = (await ai.DescribeTermAsync(name, req.Language, image, ct)).Trim();
        }
        catch (TranslationException ex)
        {
            return await SubscriptionPlan.RefundAiAndFailAsync(db, ownerId, cost, ex.Message);
        }

        if (description.Length == 0)
            return await SubscriptionPlan.RefundAiAndFailAsync(db, ownerId, cost, ErrorMessages.AiDescriptionUnusable);

        return Results.Ok(new { description = Clip(description), credits = cost });
    }

    /// <summary>
    /// ADR-062's sweep on the ADR-320 shape: every entry that has <c>SourceLanguage</c> and lacks
    /// <c>TargetLanguage</c> gains that language, for one charge and one provider call. An entry
    /// that already has the target language is left exactly as its owner wrote it.
    /// </summary>
    public static async Task<IResult> TranslateAllAsync(CedarDbContext db, string ownerId, ITranslationProvider? provider,
        ProductAnalytics analytics, TranslateAllRequest req, CancellationToken ct)
    {
        if (req.SourceLanguage is null || !Languages.IsContentLanguage(req.SourceLanguage))
            return Results.BadRequest(new { error = ErrorMessages.UnsupportedLanguage(req.SourceLanguage) });
        if (req.TargetLanguage is null || !Languages.IsContentLanguage(req.TargetLanguage))
            return Results.BadRequest(new { error = ErrorMessages.UnsupportedLanguage(req.TargetLanguage) });
        if (req.SourceLanguage == req.TargetLanguage)
            return Results.BadRequest(new { error = ErrorMessages.SameSourceAndTarget });

        var sources = (await GlossaryEntries.RowsAsync(db.GlossaryEntryLanguages
                .Where(l => l.OwnerId == ownerId && l.Language == req.SourceLanguage), ct))
            .OrderBy(r => r.Term, StringComparer.Ordinal).ToList();
        if (sources.Count == 0)
            return Results.BadRequest(new { error = ErrorMessages.NoTermsInLanguage });

        var covered = await db.GlossaryEntryLanguages
            .Where(l => l.OwnerId == ownerId && l.Language == req.TargetLanguage)
            .Select(l => l.EntryId).ToListAsync(ct);
        sources = sources.Where(r => !covered.Contains(r.EntryId)).ToList();
        if (sources.Count == 0) return Results.Ok(new { added = 0, skipped = 0 });

        if (await RefuseTranslateAsync(db, ownerId, provider, req.TargetLanguage) is { } refusal) return refusal;
        var ai = (IGlossaryAiProvider)provider!;

        if (await SubscriptionPlan.ChargeAiOrRefuseAsync(db, ownerId, CreditPacks.AiSmallCost, analytics, "glossary_bulk") is { } unpaid)
            return unpaid;

        IReadOnlyList<GlossaryTermTranslation> translated;
        try
        {
            translated = await ai.TranslateTermsAsync(
                sources.Select(r => new GlossaryTermSource(r.Term, r.Description)).ToList(), req.TargetLanguage, ct);
        }
        catch (TranslationException ex)
        {
            // Nothing was written, so the credit goes back. A run that translates some entries and
            // skips others is a partial success and keeps its charge.
            return await SubscriptionPlan.RefundAiAndFailAsync(db, ownerId, CreditPacks.AiSmallCost, ex.Message);
        }

        var added = new List<GlossaryEntryLanguage>();
        for (var i = 0; i < sources.Count && i < translated.Count; i++)
        {
            if (!Usable(translated[i]) || translated[i].Description.Trim().Length == 0) continue;
            var row = new GlossaryEntryLanguage
            {
                OwnerId = ownerId,
                EntryId = sources[i].EntryId,
                Language = req.TargetLanguage,
                LocalizedName = translated[i].Name.Trim(),
                SpellingsJson = GlossaryEntries.SerializeSpellings(FitSpellings(translated[i])),
                LocalizedDescription = Clip(translated[i].Description),
            };
            db.GlossaryEntryLanguages.Add(row);
            added.Add(row);
        }
        await db.SaveChangesAsync(CancellationToken.None); // the work is done — don't let a disconnect discard it
        await GlossaryUsage.SyncForTermsAsync(db, ownerId, added.Select(l => l.Id).ToList());
        return Results.Ok(new { added = added.Count, skipped = sources.Count - added.Count });
    }

    private static async Task<IResult?> RefuseTranslateAsync(CedarDbContext db, string ownerId,
        ITranslationProvider? provider, string targetLanguage)
    {
        var tier = await SubscriptionPlan.EffectiveTierAsync(db, ownerId);
        if (!PlanLimitations.HasAiFeatures(tier))
            return Results.Json(new { error = ErrorMessages.AutoTranslateProPlus }, statusCode: StatusCodes.Status403Forbidden);
        if (provider is not IGlossaryAiProvider)
            return Results.Json(new { error = ErrorMessages.AutoTranslateNoProvider }, statusCode: StatusCodes.Status501NotImplemented);
        // Asked before the charge, so an unsupported language costs a clear error rather than a call.
        if (!provider.SupportsTargetLanguage(targetLanguage))
            return Results.Json(new { error = ErrorMessages.LanguageNotSupportedByProvider(targetLanguage, provider.Name) },
                statusCode: StatusCodes.Status501NotImplemented);
        return null;
    }

    private static bool Usable(GlossaryTermTranslation translated) =>
        translated.Name.Trim().Length is > 0 and <= GlossaryEntries.NameMaxLength;

    private static string Clip(string description)
    {
        var text = description.Trim();
        return text.Length > GlossaryEntries.DescriptionMaxLength ? text[..GlossaryEntries.DescriptionMaxLength] : text;
    }

    // The name is matched anyway, and a list past the stored limit would make the form unsaveable.
    private static List<string> FitSpellings(GlossaryTermTranslation translated)
    {
        var name = translated.Name.Trim();
        var kept = new List<string>();
        var length = 0;
        foreach (var spelling in GlossaryEntries.CleanSpellings(translated.Spellings)
                     .Where(s => !string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var next = length + spelling.Length + (kept.Count > 0 ? 1 : 0);
            if (next > GlossaryEntries.SpellingsMaxLength) break;
            kept.Add(spelling);
            length = next;
        }
        return kept;
    }

    // Only a file this owner uploaded is ever sent to the provider.
    private static async Task<AiImage?> LoadImageAsync(CedarDbContext db, string ownerId, MediaPaths media,
        string imageUrl, CancellationToken ct)
    {
        var fileName = imageUrl["/media/".Length..];
        var asset = await db.Assets.AsNoTracking()
            .Where(a => a.OwnerId == ownerId && a.LocalPath == fileName)
            .Select(a => new { a.LocalPath, a.TelegramLocalPath, a.ContentType })
            .FirstOrDefaultAsync(ct);
        if (asset is null || !ImageTypes.Contains(asset.ContentType)) return null;

        // The derivative exists exactly when the original is too large to send as it is.
        var (name, type) = asset.TelegramLocalPath is { Length: > 0 } small
            ? (small, "image/jpeg")
            : (asset.LocalPath, asset.ContentType);
        var path = Path.Combine(media.Dir, Path.GetFileName(name));
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || file.Length > ImageMaxBytes) return null;
        return new AiImage(type, await File.ReadAllBytesAsync(path, ct));
    }
}
