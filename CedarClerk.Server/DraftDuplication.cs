using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public static class DraftDuplication
{
    public static Draft NewDraft(string ownerId, Guid projectId, DraftEndpoints.SaveDraftRequest req)
    {
        var draft = new Draft { Title = req.Title, CedarJson = req.CedarJson, OwnerId = ownerId, ProjectId = projectId };
        if (req.Language is not null) draft.PrimaryLanguage = req.Language;
        if (req.DocumentType is not null) draft.DocumentType = req.DocumentType;
        return draft;
    }

    /// <summary>
    /// A copy carries what the author wrote and where it is filed; it never carries what happened
    /// to the source — its blog address, sends, counters, schedule, readers' reactions, history —
    /// nor the flags that make a row act on its own (template, evergreen, archived).
    /// </summary>
    public static async Task<Draft?> DuplicateAsync(CedarDbContext db, string ownerId, Guid sourceId, string? title = null)
    {
        var source = await db.Drafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == sourceId && d.OwnerId == ownerId);
        if (source is null) return null;

        var copy = new Draft
        {
            OwnerId = ownerId,
            Title = string.IsNullOrWhiteSpace(title) ? source.Title : title.Trim(),
            CedarJson = source.CedarJson,
            PrimaryLanguage = source.PrimaryLanguage,
            DocumentType = source.DocumentType,
            ProjectId = source.ProjectId ?? await DocumentProjects.PersonalAsync(db, ownerId),
            Tags = source.Tags,
            FolderId = source.FolderId,
            ParentDraftId = source.ParentDraftId,
            SiblingOrder = (await db.Drafts
                .Where(d => d.OwnerId == ownerId && d.ParentDraftId == source.ParentDraftId)
                .MaxAsync(d => (int?)d.SiblingOrder) ?? 0) + 1,
            ArticleTitle = source.ArticleTitle,
            LocationText = source.LocationText,
            CtaButtonsJson = source.CtaButtonsJson,
            // A private source must not yield a copy that would publish openly.
            IsPrivate = source.IsPrivate,
            IsListedWhilePrivate = source.IsListedWhilePrivate,
            DisableCopy = source.DisableCopy,
            DisableReactions = source.DisableReactions,
            DisableComments = source.DisableComments,
            WatermarkText = source.WatermarkText,
            RegistrationFormJson = source.RegistrationFormJson,
            RegistrationFormTranslationsJson = source.RegistrationFormTranslationsJson,
        };
        db.Drafts.Add(copy);
        await DraftRevisionService.RecordAsync(db, copy.Id, copy.PrimaryLanguage, copy.Title, copy.CedarJson);

        var translations = await db.DraftTranslations.AsNoTracking().Where(t => t.DraftId == sourceId).ToListAsync();
        foreach (var t in translations)
        {
            db.DraftTranslations.Add(new DraftTranslation
            {
                OwnerId = ownerId, DraftId = copy.Id, Language = t.Language, Title = t.Title, CedarJson = t.CedarJson,
                SourceSnapshotJson = t.SourceSnapshotJson, SourceLanguage = t.SourceLanguage,
            });
            await DraftRevisionService.RecordAsync(db, copy.Id, t.Language, t.Title, t.CedarJson);
        }

        var exclusions = await db.DraftGlossaryExclusions.AsNoTracking()
            .Where(x => x.DraftId == sourceId && x.OwnerId == ownerId).ToListAsync();
        foreach (var x in exclusions)
            db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion
                { OwnerId = ownerId, DraftId = copy.Id, GlossaryTermId = x.GlossaryTermId, Language = x.Language });

        var targetTexts = await db.DraftTargetTexts.AsNoTracking().Where(x => x.DraftId == sourceId).ToListAsync();
        foreach (var x in targetTexts)
            db.DraftTargetTexts.Add(new DraftTargetText
                { OwnerId = ownerId, DraftId = copy.Id, Network = x.Network, Language = x.Language, Text = x.Text });

        await DraftEndpoints.SyncDocumentLinksAsync(db, ownerId, copy.Id, copy.CedarJson);
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForDraftAsync(db, ownerId, copy.Id);
        return copy;
    }
}
