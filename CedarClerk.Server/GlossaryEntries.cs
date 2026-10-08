using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// ADR-320 — the glossary's own rules: what a valid entry is, how its language rows are written,
// and how an entry reads as the per-language term the scanner and the usage counts work with.
public static class GlossaryEntries
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 1000;
    public const int SpellingsMaxLength = 400;

    public record LanguageInput(string? Language, string? LocalizedName, IReadOnlyList<string>? Spellings, string? LocalizedDescription);

    public record EntryInput(string? Name, string? Description, string? ImageUrl, bool IsCaseSensitive = false,
        Guid? ProjectId = null, IReadOnlyList<LanguageInput>? Languages = null);

    public record LanguageDto(Guid Id, string Language, string LocalizedName, IReadOnlyList<string> Spellings,
        string LocalizedDescription, int? UsedInDrafts, Guid? ShadowedByTermId);

    public record EntryDto(Guid Id, string Name, string Description, string? ImageUrl, bool IsCaseSensitive,
        Guid? ProjectId, DateTime UpdatedAt, IReadOnlyList<LanguageDto> Languages);

    /// <summary>
    /// One entry in one language, with the blanks already filled from the entry: the shape every
    /// reader of the glossary works with. <c>Id</c> is the language row's.
    /// </summary>
    public record Row(Guid Id, Guid EntryId, string Language, string Term, IReadOnlyList<string> Spellings,
        string Description, string? ImageUrl, bool IsCaseSensitive, Guid? ProjectId, DateTime CreatedAt)
    {
        public GlossaryUsage.TermRow ToTermRow() =>
            new(Id, Term, Spellings, IsCaseSensitive, Language, ProjectId, CreatedAt);
    }

    public static IReadOnlyList<string> ParseSpellings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return CleanSpellings(JsonSerializer.Deserialize<List<string?>>(json));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeSpellings(IEnumerable<string?>? spellings) =>
        JsonSerializer.Serialize(CleanSpellings(spellings), SpellingsJsonOptions);

    public static List<string> CleanSpellings(IEnumerable<string?>? spellings) =>
        spellings is null
            ? []
            : spellings.Select(s => s?.Trim() ?? "").Where(s => s.Length > 0).ToList();

    // Cyrillic stays readable in the column instead of becoming \u escapes.
    private static readonly JsonSerializerOptions SpellingsJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<List<Row>> RowsAsync(IQueryable<GlossaryEntryLanguage> languages, CancellationToken ct = default)
    {
        var raw = await languages.AsNoTracking()
            .Select(l => new
            {
                l.Id, l.EntryId, l.Language, l.LocalizedName, l.SpellingsJson, l.LocalizedDescription, l.CreatedAt,
                l.Entry!.Name, l.Entry.Description, l.Entry.ImageUrl, l.Entry.IsCaseSensitive, l.Entry.ProjectId,
            })
            .ToListAsync(ct);
        return raw.Select(r => new Row(
                r.Id, r.EntryId, r.Language,
                string.IsNullOrWhiteSpace(r.LocalizedName) ? r.Name : r.LocalizedName,
                ParseSpellings(r.SpellingsJson),
                string.IsNullOrWhiteSpace(r.LocalizedDescription) ? r.Description : r.LocalizedDescription,
                r.ImageUrl, r.IsCaseSensitive, r.ProjectId, r.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// The terms a page renders with: one owner's rows in the language being shown, global plus
    /// the document's project, minus what this document excludes.
    /// </summary>
    public static async Task<IReadOnlyList<GlossaryMatchTerm>> LoadForAsync(
        CedarDbContext db, string ownerId, string language, Guid? projectId = null, Guid? draftId = null)
    {
        var excluded = draftId is null
            ? []
            : await db.DraftGlossaryExclusions
                .Where(x => x.OwnerId == ownerId && x.DraftId == draftId && x.Language == language)
                .Select(x => x.GlossaryTermId).ToListAsync();
        var rows = await RowsAsync(db.GlossaryEntryLanguages
            .Where(l => l.OwnerId == ownerId && l.Language == language
                        && (l.Entry!.ProjectId == null || l.Entry.ProjectId == projectId)
                        && !excluded.Contains(l.Id)));
        return MatchTerms(rows);
    }

    public static IReadOnlyList<GlossaryMatchTerm> MatchTerms(IEnumerable<Row> rows) => rows
        // Project terms first, so DistinctBy below keeps them over a global term of the same
        // name. Comparison is case-insensitive because the scanner matches that way too.
        .OrderByDescending(r => r.ProjectId.HasValue)
        .DistinctBy(r => r.Term, StringComparer.OrdinalIgnoreCase)
        .Select(r => new GlossaryMatchTerm(r.Term, r.Description, r.ImageUrl, r.Spellings, r.IsCaseSensitive))
        .ToList();

    public static EntryDto ToDto(GlossaryEntry entry, IReadOnlyDictionary<Guid, int>? usedIn = null,
        IReadOnlyDictionary<Guid, Guid>? shadows = null) =>
        new(entry.Id, entry.Name, entry.Description, entry.ImageUrl, entry.IsCaseSensitive, entry.ProjectId, entry.UpdatedAt,
            entry.Languages
                .OrderBy(l => LanguageOrder(l.Language))
                .Select(l => new LanguageDto(
                    l.Id, l.Language, l.LocalizedName, ParseSpellings(l.SpellingsJson), l.LocalizedDescription,
                    usedIn is null ? null : usedIn.GetValueOrDefault(l.Id),
                    shadows is not null && shadows.TryGetValue(l.Id, out var winner) ? winner : null))
                .ToList());

    private static int LanguageOrder(string language)
    {
        for (var i = 0; i < Languages.ContentLanguages.Count; i++)
            if (Languages.ContentLanguages[i] == language) return i;
        return int.MaxValue;
    }

    public static string? Validate(EntryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return ErrorMessages.TermRequired;
        if (input.Name.Trim().Length > NameMaxLength) return ErrorMessages.TermTooLong(NameMaxLength);
        if (string.IsNullOrWhiteSpace(input.Description)) return ErrorMessages.DescriptionRequired;
        if (input.Description.Trim().Length > DescriptionMaxLength) return ErrorMessages.DescriptionTooLong(DescriptionMaxLength);
        if (input.Languages is not { Count: > 0 }) return ErrorMessages.GlossaryLanguageRequired;

        var seen = new HashSet<string>();
        foreach (var language in input.Languages)
        {
            if (language.Language is null || !Languages.IsContentLanguage(language.Language))
                return ErrorMessages.UnsupportedLanguage(language.Language);
            if (!seen.Add(language.Language)) return ErrorMessages.GlossaryLanguageRepeated(language.Language);
            if ((language.LocalizedName?.Trim().Length ?? 0) > NameMaxLength) return ErrorMessages.TermTooLong(NameMaxLength);
            if ((language.LocalizedDescription?.Trim().Length ?? 0) > DescriptionMaxLength)
                return ErrorMessages.DescriptionTooLong(DescriptionMaxLength);
            if (string.Join(",", CleanSpellings(language.Spellings)).Length > SpellingsMaxLength)
                return ErrorMessages.AliasesTooLong(SpellingsMaxLength);
        }
        return null;
    }

    public static async Task<IResult> CreateAsync(CedarDbContext db, string ownerId, EntryInput input)
    {
        if (Validate(input) is { } error) return Results.BadRequest(new { error });
        if (await LockedLanguageAsync(db, ownerId, input.Languages!.Select(l => l.Language!)))
            return Results.Json(new { error = ErrorMessages.LanguageRequiresPro }, statusCode: StatusCodes.Status403Forbidden);

        var entry = new GlossaryEntry { OwnerId = ownerId };
        Apply(entry, input);
        foreach (var language in input.Languages!)
            entry.Languages.Add(NewLanguage(entry, language));
        db.GlossaryEntries.Add(entry);
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForTermsAsync(db, ownerId, entry.Languages.Select(l => l.Id).ToList());
        return Results.Ok(ToDto(entry));
    }

    public static async Task<IResult> UpdateAsync(CedarDbContext db, string ownerId, Guid id, EntryInput input)
    {
        if (Validate(input) is { } error) return Results.BadRequest(new { error });

        var entry = await db.GlossaryEntries.Include(e => e.Languages)
            .FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId);
        if (entry is null) return Results.NotFound();

        // A language the entry already has stays editable on any plan; only a new one is gated.
        var added = input.Languages!.Select(l => l.Language!)
            .Where(code => entry.Languages.All(l => l.Language != code));
        if (await LockedLanguageAsync(db, ownerId, added))
            return Results.Json(new { error = ErrorMessages.LanguageRequiresPro }, statusCode: StatusCodes.Status403Forbidden);

        Apply(entry, input);
        entry.UpdatedAt = DateTime.UtcNow;

        var kept = input.Languages!.Select(l => l.Language!).ToHashSet();
        var removed = entry.Languages.Where(l => !kept.Contains(l.Language)).ToList();
        foreach (var language in input.Languages!)
        {
            var row = entry.Languages.FirstOrDefault(l => l.Language == language.Language);
            if (row is null)
            {
                // Added through the set rather than the navigation: a client-generated key on a
                // tracked parent's collection is otherwise read as an existing row and updated.
                db.GlossaryEntryLanguages.Add(NewLanguage(entry, language));
                continue;
            }
            Apply(row, language);
            row.UpdatedAt = entry.UpdatedAt;
        }
        var removedIds = removed.Select(l => l.Id).ToList();
        db.GlossaryEntryLanguages.RemoveRange(removed);
        await db.SaveChangesAsync();

        if (removedIds.Count > 0)
        {
            await db.DraftGlossaryExclusions
                .Where(x => x.OwnerId == ownerId && removedIds.Contains(x.GlossaryTermId)).ExecuteDeleteAsync();
            await db.GlossaryTermUsages
                .Where(u => u.OwnerId == ownerId && removedIds.Contains(u.GlossaryTermId)).ExecuteDeleteAsync();
        }
        // The name, the spellings, the case flag or the scope may have moved, and each of those
        // changes which documents every language of this entry is used in.
        await GlossaryUsage.SyncForTermsAsync(db, ownerId, entry.Languages.Select(l => l.Id).ToList());
        return Results.Ok(ToDto(entry));
    }

    public static async Task<IResult> DeleteAsync(CedarDbContext db, string ownerId, Guid id)
    {
        var rowIds = await db.GlossaryEntryLanguages
            .Where(l => l.EntryId == id && l.OwnerId == ownerId).Select(l => l.Id).ToListAsync();
        await db.DraftGlossaryExclusions
            .Where(x => x.OwnerId == ownerId && rowIds.Contains(x.GlossaryTermId)).ExecuteDeleteAsync();
        await db.GlossaryTermUsages
            .Where(u => u.OwnerId == ownerId && rowIds.Contains(u.GlossaryTermId)).ExecuteDeleteAsync();
        await db.GlossaryEntryLanguages.Where(l => l.EntryId == id && l.OwnerId == ownerId).ExecuteDeleteAsync();
        var deleted = await db.GlossaryEntries.Where(e => e.Id == id && e.OwnerId == ownerId).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<bool> LockedLanguageAsync(CedarDbContext db, string ownerId, IEnumerable<string> languages)
    {
        var codes = languages.ToList();
        if (codes.Count == 0) return false;
        var tier = await SubscriptionPlan.EffectiveTierAsync(db, ownerId);
        return codes.Any(code => !PlanLimitations.HasContentLanguage(tier, code));
    }

    private static void Apply(GlossaryEntry entry, EntryInput input)
    {
        entry.Name = input.Name!.Trim();
        entry.Description = input.Description!.Trim();
        entry.ImageUrl = NormalizeImage(input.ImageUrl);
        entry.IsCaseSensitive = input.IsCaseSensitive;
        entry.ProjectId = input.ProjectId;
    }

    private static GlossaryEntryLanguage NewLanguage(GlossaryEntry entry, LanguageInput input)
    {
        var row = new GlossaryEntryLanguage { OwnerId = entry.OwnerId, EntryId = entry.Id, Language = input.Language! };
        Apply(row, input);
        return row;
    }

    private static void Apply(GlossaryEntryLanguage row, LanguageInput input)
    {
        row.LocalizedName = input.LocalizedName?.Trim() ?? "";
        row.SpellingsJson = SerializeSpellings(input.Spellings);
        row.LocalizedDescription = input.LocalizedDescription?.Trim() ?? "";
    }

    // Same rule as the avatar endpoint: only a path this server produced. Accepting an arbitrary
    // URL would let a glossary tooltip point the blog's own chrome at someone else's server.
    public static string? NormalizeImage(string? imageUrl)
    {
        var url = imageUrl?.Trim();
        if (string.IsNullOrEmpty(url)) return null;
        return url.StartsWith("/media/", StringComparison.Ordinal) ? url : null;
    }
}
