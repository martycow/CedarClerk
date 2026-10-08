using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// ADR-238 — keeps <see cref="GlossaryTermUsage"/> in step with the text and with the terms.
///
/// A usage row is a fact about a pair, and both halves move on their own: a document is edited, and
/// a term's spellings are edited. So there are two entry points and they meet in the middle. Both are
/// called *after* the caller has saved its own change and both save their own — the scan reads the
/// stored document, which an unsaved edit is not yet part of.
///
/// The scan runs on plain text rather than on rendered HTML, and ignores
/// <see cref="DraftGlossaryExclusion"/>: an exclusion is a publishing decision, and a writer asking
/// where a term is used means the text, not the render.
/// </summary>
public static class GlossaryUsage
{
    /// <summary>
    /// Rescans one document against every term visible to it. Clause 8 states this as an invariant
    /// rather than a list: <b>any endpoint that changes a draft's stored text calls this before it
    /// returns</b> — a save, a translation write or delete, a primary-language swap, a revision
    /// restore, an AI rewrite, an import, and every path that creates a document with a body.
    /// </summary>
    public static async Task SyncForDraftAsync(CedarDbContext db, string ownerId, Guid draftId)
    {
        var draft = await db.Drafts.AsNoTracking()
            .Where(d => d.Id == draftId && d.OwnerId == ownerId)
            .Select(d => new { d.ProjectId, d.PrimaryLanguage, d.CedarJson })
            .FirstOrDefaultAsync();
        if (draft is null) return;

        var translations = await db.DraftTranslations.AsNoTracking()
            .Where(t => t.DraftId == draftId && t.OwnerId == ownerId)
            .Select(t => new { t.Language, t.CedarJson })
            .ToListAsync();

        var texts = translations.ToDictionary(t => t.Language, t => PlainText(t.CedarJson));
        // Last, so a leftover translation row in the document's own language (ADR-065 calls it a
        // shadow) cannot stand in for the canonical body.
        texts[draft.PrimaryLanguage] = PlainText(draft.CedarJson);

        var terms = (await GlossaryEntries.RowsAsync(db.GlossaryEntryLanguages
                .Where(l => l.OwnerId == ownerId
                            && (l.Entry!.ProjectId == null || l.Entry.ProjectId == draft.ProjectId))))
            .Select(r => r.ToTermRow()).ToList();

        var hits = new List<(Guid TermId, Guid DraftId, int Occurrences)>();
        foreach (var group in terms.GroupBy(t => t.Language))
        {
            if (!texts.TryGetValue(group.Key, out var text)) continue;
            foreach (var (termId, occurrences) in Count(text, group))
                hits.Add((termId, draftId, occurrences));
        }

        var existing = await db.GlossaryTermUsages
            .Where(u => u.OwnerId == ownerId && u.DraftId == draftId).ToListAsync();
        Reconcile(db, ownerId, existing, hits);
        await db.SaveChangesAsync();
    }

    /// <summary>Rescans one term against every document of that owner.</summary>
    public static Task SyncForTermAsync(CedarDbContext db, string ownerId, Guid termId) =>
        SyncForTermsAsync(db, ownerId, [termId]);

    /// <summary>
    /// The same for several terms at once. The whole-language translation sweep writes a glossary's
    /// worth of terms in one request, and running the per-term pass in a loop would re-read every
    /// document body once per term.
    /// </summary>
    public static async Task SyncForTermsAsync(CedarDbContext db, string ownerId, IReadOnlyList<Guid> termIds)
    {
        if (termIds.Count == 0) return;

        var terms = (await GlossaryEntries.RowsAsync(db.GlossaryEntryLanguages
                .Where(l => l.OwnerId == ownerId && termIds.Contains(l.Id))))
            .Select(r => r.ToTermRow()).ToList();
        if (terms.Count == 0) return;

        var drafts = await db.Drafts.AsNoTracking()
            .Where(d => d.OwnerId == ownerId)
            .Select(d => new { d.Id, d.ProjectId, d.PrimaryLanguage, d.CedarJson })
            .ToListAsync();

        var languages = terms.Select(t => t.Language).Distinct().ToList();
        var translations = await db.DraftTranslations.AsNoTracking()
            .Where(t => t.OwnerId == ownerId && languages.Contains(t.Language))
            .Select(t => new { t.DraftId, t.Language, t.CedarJson })
            .ToListAsync();
        var translated = translations.ToDictionary(t => (t.DraftId, t.Language), t => t.CedarJson);

        var hits = new List<(Guid TermId, Guid DraftId, int Occurrences)>();
        foreach (var draft in drafts)
        {
            // Same scoping as the render: a document sees global terms plus its own project's.
            var visible = terms.Where(t => t.ProjectId is null || t.ProjectId == draft.ProjectId).ToList();
            foreach (var group in visible.GroupBy(t => t.Language))
            {
                var cedarJson = group.Key == draft.PrimaryLanguage
                    ? draft.CedarJson
                    : translated.GetValueOrDefault((draft.Id, group.Key));
                if (cedarJson is null) continue;

                foreach (var (termId, occurrences) in Count(PlainText(cedarJson), group))
                    hits.Add((termId, draft.Id, occurrences));
            }
        }

        var existing = await db.GlossaryTermUsages
            .Where(u => u.OwnerId == ownerId && termIds.Contains(u.GlossaryTermId)).ToListAsync();
        Reconcile(db, ownerId, existing, hits);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// How many documents each term is used in, for one owner — the number
    /// <c>GET /api/glossary</c> carries as <c>usedInDrafts</c>. A term with no rows is absent from
    /// the result and reads as zero.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> CountsByTermAsync(CedarDbContext db, string ownerId)
    {
        var rows = await db.GlossaryTermUsages
            .Where(u => u.OwnerId == ownerId)
            .GroupBy(u => u.GlossaryTermId)
            .Select(g => new { TermId = g.Key, Drafts = g.Select(u => u.DraftId).Distinct().Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.TermId, r => r.Drafts);
    }

    public record TermRow(Guid Id, string Term, IReadOnlyList<string> Spellings, bool IsCaseSensitive, string Language,
        Guid? ProjectId, DateTime CreatedAt);

    /// <summary>
    /// Which of two terms spelled the same wins the position. Project scope first, because the
    /// narrower definition is the one the blog marks with (ADR-112); then <c>CreatedAt</c>, then
    /// <c>Id</c>, so the order is total. Without that last step the scan and
    /// <see cref="ShadowsByTerm"/> could name different winners on different runs, and the note
    /// beside the count would contradict the count.
    /// </summary>
    public static List<TermRow> InPriorityOrder(IEnumerable<TermRow> terms) =>
        terms.OrderByDescending(t => t.ProjectId.HasValue)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();

    /// <summary>
    /// ADR-238 clause 13 — for each term that another term outspells, the winner's id. Read off the
    /// glossary alone: two terms whose spellings intersect collide wherever both are visible, which
    /// no document has to be opened to know.
    /// </summary>
    public static Dictionary<Guid, Guid> ShadowsByTerm(IReadOnlyList<TermRow> terms)
    {
        var shadows = new Dictionary<Guid, Guid>();
        foreach (var language in terms.GroupBy(t => t.Language))
        {
            var ordered = InPriorityOrder(language);
            for (var loser = 1; loser < ordered.Count; loser++)
                for (var winner = 0; winner < loser; winner++)
                {
                    if (!CanMeet(ordered[winner], ordered[loser])) continue;
                    if (!SharesSpelling(ordered[winner], ordered[loser])) continue;
                    shadows[ordered[loser].Id] = ordered[winner].Id;
                    break;
                }
        }
        return shadows;
    }

    // Two terms scoped to different projects never appear in one document, so neither can take the
    // other's position. A global term meets everything, which is the case clause 13 is about.
    private static bool CanMeet(TermRow a, TermRow b) =>
        a.ProjectId is null || b.ProjectId is null || a.ProjectId == b.ProjectId;

    // The question is whether any text exists that both terms would match, so only a pair that
    // both refuse a different casing needs an exact comparison. One case-insensitive term is enough
    // to reach the other's spelling; "IT" against "it" with both flags set matches no single string.
    private static bool SharesSpelling(TermRow a, TermRow b)
    {
        var comparer = a.IsCaseSensitive && b.IsCaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var theirs = new HashSet<string>(Spellings(b), comparer);
        return Spellings(a).Any(theirs.Contains);
    }

    private static IEnumerable<string> Spellings(TermRow row) =>
        row.Spellings.Select(s => s.Trim())
            .Prepend(row.Term.Trim())
            .Where(s => s.Length > 0);

    private static IEnumerable<(Guid TermId, int Occurrences)> Count(string text, IEnumerable<TermRow> terms)
    {
        var rows = InPriorityOrder(terms);
        var counts = GlossaryScanner.CountHits(text, rows.Select(Entry).ToList());
        for (var i = 0; i < rows.Count; i++)
            if (counts[i] > 0) yield return (rows[i].Id, counts[i]);
    }

    private static GlossaryMatchTerm Entry(TermRow row) =>
        new(row.Term, "", null, row.Spellings, row.IsCaseSensitive);

    private static string PlainText(string cedarJson) =>
        string.Join('\n', CedarPlainText.Paragraphs(cedarJson));

    private static void Reconcile(CedarDbContext db, string ownerId, List<GlossaryTermUsage> existing,
        List<(Guid TermId, Guid DraftId, int Occurrences)> hits)
    {
        var now = DateTime.UtcNow;
        var found = new HashSet<(Guid, Guid)>();
        foreach (var (termId, draftId, occurrences) in hits)
        {
            found.Add((termId, draftId));
            var row = existing.FirstOrDefault(u => u.GlossaryTermId == termId && u.DraftId == draftId);
            if (row is null)
            {
                db.GlossaryTermUsages.Add(new GlossaryTermUsage
                {
                    OwnerId = ownerId,
                    GlossaryTermId = termId,
                    DraftId = draftId,
                    Occurrences = occurrences,
                    ScannedAt = now,
                });
                continue;
            }
            row.Occurrences = occurrences;
            row.ScannedAt = now;
        }

        // A pair that no longer matches loses its row rather than keeping a zero — no row is how
        // "nowhere" is spelled here.
        db.GlossaryTermUsages.RemoveRange(existing.Where(u => !found.Contains((u.GlossaryTermId, u.DraftId))));
    }
}
