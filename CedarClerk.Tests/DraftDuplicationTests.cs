using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

public class DraftDuplicationTests
{
    private const string Owner = "owner-1";

    private static CedarDbContext Database()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "o@x.test", Email = "o@x.test" });
        db.Users.Add(new ApplicationUser { Id = "stranger", UserName = "s@x.test", Email = "s@x.test" });
        db.SaveChanges();
        return db;
    }

    private static Draft Seed(CedarDbContext db)
    {
        var projectId = Guid.NewGuid();
        var parent = new Draft { OwnerId = Owner, Title = "Parent", ProjectId = projectId };
        var source = new Draft
        {
            OwnerId = Owner, Title = "Combat", CedarJson = """{"type":"doc","content":[]}""",
            PrimaryLanguage = Languages.English, DocumentType = DocumentTypes.Design, ProjectId = projectId,
            Tags = "gdd,combat", FolderId = Guid.NewGuid(), ParentDraftId = parent.Id, SiblingOrder = 4,
            SeriesId = Guid.NewGuid(), SeriesOrder = 2, IsPrivate = true,
            BlogSlug = "combat", IsBlogPublished = true, BlogPublishedAt = DateTime.UtcNow, ViewCount = 9,
            LastTelegramMessageId = 7, IsTemplate = true, IsArchived = true, IsEvergreen = true,
            EvergreenSendCount = 3, PreviewToken = "token",
        };
        db.Drafts.AddRange(parent, source);
        db.DraftTranslations.Add(new DraftTranslation
            { OwnerId = Owner, DraftId = source.Id, Language = Languages.German, Title = "Kampf", CedarJson = "{}" });
        db.DraftTargetTexts.Add(new DraftTargetText
            { OwnerId = Owner, DraftId = source.Id, Network = "x", Language = Languages.English, Text = "short" });
        db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion
            { OwnerId = Owner, DraftId = source.Id, GlossaryTermId = Guid.NewGuid(), Language = Languages.English });
        db.DraftRevisions.Add(new DraftRevision { OwnerId = Owner, DraftId = source.Id, Title = "old", CedarJson = "{}" });
        db.Comments.Add(new Comment { OwnerId = Owner, DraftId = source.Id, Text = "c" });
        db.SaveChanges();
        return source;
    }

    [Fact]
    public async Task A_copy_carries_the_text_its_languages_and_its_filing()
    {
        using var db = Database();
        var source = Seed(db);

        var copy = await DraftDuplication.DuplicateAsync(db, Owner, source.Id, "Combat (copy)");

        Assert.NotNull(copy);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Combat (copy)", copy.Title);
        Assert.Equal(source.CedarJson, copy.CedarJson);
        Assert.Equal(Languages.English, copy.PrimaryLanguage);
        Assert.Equal(DocumentTypes.Design, copy.DocumentType);
        Assert.Equal(source.ProjectId, copy.ProjectId);
        Assert.Equal(source.Tags, copy.Tags);
        Assert.Equal(source.FolderId, copy.FolderId);
        Assert.Equal(source.ParentDraftId, copy.ParentDraftId);
        Assert.Equal(5, copy.SiblingOrder);
        Assert.True(copy.IsPrivate);

        var translation = Assert.Single(db.DraftTranslations.Where(t => t.DraftId == copy.Id));
        Assert.Equal((Languages.German, "Kampf", Owner), (translation.Language, translation.Title, translation.OwnerId));
        Assert.Single(db.DraftTargetTexts.Where(x => x.DraftId == copy.Id));
        Assert.Single(db.DraftGlossaryExclusions.Where(x => x.DraftId == copy.Id));
        Assert.Equal(2, db.DraftRevisions.Count(r => r.DraftId == copy.Id && r.OwnerId == Owner));
    }

    [Fact]
    public async Task A_copy_carries_nothing_that_happened_to_the_source()
    {
        using var db = Database();
        var source = Seed(db);

        var copy = (await DraftDuplication.DuplicateAsync(db, Owner, source.Id))!;

        Assert.Equal("Combat", copy.Title);
        Assert.Null(copy.BlogSlug);
        Assert.False(copy.IsBlogPublished);
        Assert.Null(copy.BlogPublishedAt);
        Assert.Equal(0, copy.ViewCount);
        Assert.Null(copy.LastTelegramMessageId);
        Assert.Null(copy.PreviewToken);
        Assert.Null(copy.SeriesId);
        Assert.Null(copy.SeriesOrder);
        Assert.False(copy.IsTemplate);
        Assert.False(copy.IsArchived);
        Assert.False(copy.IsEvergreen);
        Assert.Equal(0, copy.EvergreenSendCount);
        Assert.Empty(db.Comments.Where(c => c.DraftId == copy.Id));
        Assert.Single(db.Comments.Where(c => c.DraftId == source.Id));
        Assert.True(db.Drafts.Single(d => d.Id == source.Id).IsBlogPublished);
    }

    [Fact]
    public async Task Another_account_cannot_copy_the_document()
    {
        using var db = Database();
        var source = Seed(db);
        var before = db.Drafts.Count();

        Assert.Null(await DraftDuplication.DuplicateAsync(db, "stranger", source.Id));
        Assert.Equal(before, db.Drafts.Count());
    }

    [Fact]
    public void A_new_document_is_born_in_the_asked_language_and_type()
    {
        var projectId = Guid.NewGuid();
        var asked = DraftDuplication.NewDraft(Owner, projectId,
            new DraftEndpoints.SaveDraftRequest("T", "{}", ProjectId: projectId, Language: Languages.English, DocumentType: DocumentTypes.Script));
        Assert.Equal((Languages.English, DocumentTypes.Script), (asked.PrimaryLanguage, asked.DocumentType));

        var silent = DraftDuplication.NewDraft(Owner, projectId, new DraftEndpoints.SaveDraftRequest("T", "{}", ProjectId: projectId));
        Assert.Equal((Languages.Russian, DocumentTypes.Post), (silent.PrimaryLanguage, silent.DocumentType));
    }
}
