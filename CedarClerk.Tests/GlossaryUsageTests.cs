using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// ADR-238 clause 8 — a usage row is a fact about a pair, and both halves move on their own. These
// drive the two triggers separately, because either one working alone would still leave the number
// wrong half the time.
public class GlossaryUsageTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly Microsoft.Data.Sqlite.SqliteConnection connection;
    private readonly CedarDbContext db;

    public GlossaryUsageTests()
    {
        connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = OwnerA, UserName = "a@x.test", TenantUsername = "a" });
        db.Users.Add(new ApplicationUser { Id = OwnerB, UserName = "b@x.test", TenantUsername = "b" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string Doc(params string[] paragraphs) =>
        $$"""{"type":"doc","content":[{{string.Join(",", paragraphs.Select(p =>
            $$"""{"type":"paragraph","content":[{"type":"text","text":"{{p}}"}]}"""))}}]}""";

    private Draft SeedDraft(string ownerId, string text, Guid? projectId = null, string language = Languages.Russian)
    {
        var draft = new Draft
        {
            OwnerId = ownerId, Title = "post", CedarJson = Doc(text),
            ProjectId = projectId, PrimaryLanguage = language,
        };
        db.Drafts.Add(draft);
        db.SaveChanges();
        return draft;
    }

    private GlossaryEntryLanguage SeedTerm(string ownerId, string term, string aliases = "",
        Guid? projectId = null, string language = Languages.Russian, DateTime? createdAt = null)
    {
        var entry = new GlossaryEntry { OwnerId = ownerId, Name = term, Description = "d", ProjectId = projectId };
        var row = new GlossaryEntryLanguage
        {
            OwnerId = ownerId, Entry = entry, Language = language, LocalizedName = term,
            SpellingsJson = GlossaryEntries.SerializeSpellings(aliases.Split(',')),
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        db.GlossaryEntryLanguages.Add(row);
        db.SaveChanges();
        return row;
    }

    private List<GlossaryTermUsage> Rows(string ownerId) =>
        db.GlossaryTermUsages.AsNoTracking().Where(u => u.OwnerId == ownerId).ToList();

    [Fact]
    public async Task Scanning_a_draft_records_the_terms_it_uses()
    {
        var term = SeedTerm(OwnerA, "Unity");
        SeedTerm(OwnerA, "Godot");
        var draft = SeedDraft(OwnerA, "We use Unity and Unity again");

        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);

        // Godot is nowhere in the text, so it has no row at all — a stored zero is not how "used
        // nowhere" is spelled here.
        var row = Assert.Single(Rows(OwnerA));
        Assert.Equal(term.Id, row.GlossaryTermId);
        Assert.Equal(draft.Id, row.DraftId);
        Assert.Equal(2, row.Occurrences);
    }

    [Fact]
    public async Task Writing_a_term_records_it_against_the_drafts_that_already_existed()
    {
        // The half a save-time-only trigger would miss: the documents were written first.
        var first = SeedDraft(OwnerA, "We use Unity here");
        var second = SeedDraft(OwnerA, "Nothing relevant");
        var term = SeedTerm(OwnerA, "Unity");

        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);

        var row = Assert.Single(Rows(OwnerA));
        Assert.Equal(first.Id, row.DraftId);
        Assert.NotEqual(second.Id, row.DraftId);
    }

    [Fact]
    public async Task Editing_the_term_out_of_a_draft_removes_its_row()
    {
        var term = SeedTerm(OwnerA, "Unity");
        var draft = SeedDraft(OwnerA, "We use Unity here");
        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);
        Assert.Single(Rows(OwnerA));

        db.Drafts.Single(d => d.Id == draft.Id).CedarJson = Doc("We use nothing here");
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);

        Assert.Empty(Rows(OwnerA));
    }

    [Fact]
    public async Task Renaming_a_term_moves_the_rows_with_it()
    {
        var term = SeedTerm(OwnerA, "Godot");
        var draft = SeedDraft(OwnerA, "We use Unity here");
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);
        Assert.Empty(Rows(OwnerA));

        db.GlossaryEntryLanguages.Single(t => t.Id == term.Id).LocalizedName = "Unity";
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);

        Assert.Equal(draft.Id, Assert.Single(Rows(OwnerA)).DraftId);
    }

    [Fact]
    public async Task Aliases_count_towards_the_term_that_lists_them()
    {
        var term = SeedTerm(OwnerA, "рендерер", aliases: "рендерера");
        var draft = SeedDraft(OwnerA, "про рендерера и рендерер речь");

        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);

        var row = Assert.Single(Rows(OwnerA));
        Assert.Equal(term.Id, row.GlossaryTermId);
        Assert.Equal(2, row.Occurrences);
    }

    [Fact]
    public async Task A_term_is_counted_against_the_documents_text_in_its_own_language()
    {
        var english = SeedTerm(OwnerA, "Unity", language: Languages.English);
        var draft = SeedDraft(OwnerA, "Про Unity по-русски");
        db.DraftTranslations.Add(new DraftTranslation
        {
            OwnerId = OwnerA, DraftId = draft.Id, Language = Languages.English,
            Title = "post", CedarJson = Doc("We use Unity in English"),
        });
        await db.SaveChangesAsync();

        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);

        // The Russian body also spells "Unity", and would double the count if language were ignored.
        var row = Assert.Single(Rows(OwnerA));
        Assert.Equal(english.Id, row.GlossaryTermId);
        Assert.Equal(1, row.Occurrences);
    }

    [Fact]
    public async Task A_projects_term_is_invisible_to_another_projects_document()
    {
        var project = new Project { OwnerId = OwnerA, Name = "Cedar Quest" };
        var other = new Project { OwnerId = OwnerA, Name = "Something Else" };
        db.Projects.AddRange(project, other);
        db.SaveChanges();

        var term = SeedTerm(OwnerA, "ferry", projectId: project.Id);
        var inside = SeedDraft(OwnerA, "the ferry sails", projectId: project.Id);
        SeedDraft(OwnerA, "the ferry sails", projectId: other.Id);

        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);

        Assert.Equal(inside.Id, Assert.Single(Rows(OwnerA)).DraftId);
    }

    [Fact]
    public async Task One_owners_scan_never_reaches_another_owners_documents()
    {
        var term = SeedTerm(OwnerA, "Unity");
        SeedDraft(OwnerB, "We use Unity here");

        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);

        Assert.Empty(Rows(OwnerA));
        Assert.Empty(Rows(OwnerB));
    }

    [Fact]
    public async Task Rescanning_a_draft_leaves_another_drafts_rows_alone()
    {
        var term = SeedTerm(OwnerA, "Unity");
        var kept = SeedDraft(OwnerA, "We use Unity here");
        var edited = SeedDraft(OwnerA, "Unity again");
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);
        Assert.Equal(2, Rows(OwnerA).Count);

        db.Drafts.Single(d => d.Id == edited.Id).CedarJson = Doc("nothing here now");
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, edited.Id);

        Assert.Equal(kept.Id, Assert.Single(Rows(OwnerA)).DraftId);
    }

    [Fact]
    public async Task The_count_is_documents_not_occurrences()
    {
        var term = SeedTerm(OwnerA, "Unity");
        SeedDraft(OwnerA, "Unity Unity Unity");
        SeedDraft(OwnerA, "Unity once");
        SeedDraft(OwnerA, "nothing");
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);

        var counts = await GlossaryUsage.CountsByTermAsync(db, OwnerA);

        Assert.Equal(2, counts[term.Id]);
    }

    [Fact]
    public async Task A_term_nothing_uses_is_absent_from_the_counts()
    {
        var term = SeedTerm(OwnerA, "Godot");
        SeedDraft(OwnerA, "We use Unity here");
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);

        var counts = await GlossaryUsage.CountsByTermAsync(db, OwnerA);

        Assert.False(counts.ContainsKey(term.Id));
    }

    [Fact]
    public async Task The_counts_of_one_owner_do_not_include_another_owners_rows()
    {
        var mine = SeedTerm(OwnerA, "Unity");
        var theirs = SeedTerm(OwnerB, "Unity");
        SeedDraft(OwnerA, "Unity here");
        SeedDraft(OwnerB, "Unity there");
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, mine.Id);
        await GlossaryUsage.SyncForTermAsync(db, OwnerB, theirs.Id);

        var counts = await GlossaryUsage.CountsByTermAsync(db, OwnerA);

        Assert.Equal(1, counts[mine.Id]);
        Assert.False(counts.ContainsKey(theirs.Id));
    }

    private static GlossaryUsage.TermRow Row(GlossaryEntryLanguage term) =>
        new(term.Id, term.LocalizedName, GlossaryEntries.ParseSpellings(term.SpellingsJson),
            term.Entry!.IsCaseSensitive, term.Language, term.Entry.ProjectId, term.CreatedAt);

    // ADR-238 clause 13 — the note beside the count and the count itself must name the same winner,
    // which is the only reason the tiebreak has to be a total order.
    [Fact]
    public async Task The_shadow_names_the_term_the_scan_actually_credited()
    {
        var project = new Project { OwnerId = OwnerA, Name = "Cedar Quest" };
        db.Projects.Add(project);
        db.SaveChanges();

        var global = SeedTerm(OwnerA, "Unity", createdAt: new DateTime(2026, 1, 1));
        var scoped = SeedTerm(OwnerA, "Unity", projectId: project.Id, createdAt: new DateTime(2026, 6, 1));
        var draft = SeedDraft(OwnerA, "We use Unity here", projectId: project.Id);

        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);
        var shadows = GlossaryUsage.ShadowsByTerm([Row(global), Row(scoped)]);

        var credited = Assert.Single(Rows(OwnerA)).GlossaryTermId;
        Assert.Equal(scoped.Id, credited);
        Assert.Equal(credited, shadows[global.Id]);
        Assert.False(shadows.ContainsKey(scoped.Id));
    }

    // The count is not blanked: the global term really is used everywhere the project term is not.
    [Fact]
    public async Task A_shadowed_global_term_keeps_a_non_zero_count()
    {
        var project = new Project { OwnerId = OwnerA, Name = "Cedar Quest" };
        db.Projects.Add(project);
        db.SaveChanges();

        var global = SeedTerm(OwnerA, "Unity", createdAt: new DateTime(2026, 1, 1));
        var scoped = SeedTerm(OwnerA, "Unity", projectId: project.Id, createdAt: new DateTime(2026, 6, 1));
        var inside = SeedDraft(OwnerA, "Unity in the game", projectId: project.Id);
        var outside = SeedDraft(OwnerA, "Unity in general");

        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, inside.Id);
        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, outside.Id);

        var counts = await GlossaryUsage.CountsByTermAsync(db, OwnerA);
        var shadows = GlossaryUsage.ShadowsByTerm([Row(global), Row(scoped)]);

        Assert.Equal(1, counts[global.Id]);
        Assert.Equal(1, counts[scoped.Id]);
        Assert.True(shadows.ContainsKey(global.Id));
    }

    [Fact]
    public async Task Two_terms_in_one_scope_credit_the_older_one_and_the_newer_says_so()
    {
        var older = SeedTerm(OwnerA, "Unity", createdAt: new DateTime(2026, 1, 1));
        var newer = SeedTerm(OwnerA, "Unity", createdAt: new DateTime(2026, 6, 1));
        var draft = SeedDraft(OwnerA, "We use Unity here");

        await GlossaryUsage.SyncForDraftAsync(db, OwnerA, draft.Id);
        var shadows = GlossaryUsage.ShadowsByTerm([Row(newer), Row(older)]);

        Assert.Equal(older.Id, Assert.Single(Rows(OwnerA)).GlossaryTermId);
        Assert.Equal(older.Id, shadows[newer.Id]);
    }

    [Fact]
    public async Task Deleting_a_draft_takes_its_usage_rows_with_it()
    {
        var term = SeedTerm(OwnerA, "Unity");
        var draft = SeedDraft(OwnerA, "We use Unity here");
        await GlossaryUsage.SyncForTermAsync(db, OwnerA, term.Id);
        Assert.Single(Rows(OwnerA));

        await DraftDeletion.CascadeAsync(db, OwnerA, draft.Id);

        Assert.Empty(Rows(OwnerA));
    }
}

// ADR-238 clause 13 — shadowing is a property of the glossary alone, so these need no database.
public class GlossaryShadowTests
{
    private static GlossaryUsage.TermRow Term(string term, string aliases = "", Guid? projectId = null,
        bool caseSensitive = false, string language = Languages.Russian, int createdDay = 1, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), term, GlossaryEntries.CleanSpellings(aliases.Split(',')), caseSensitive, language, projectId,
            new DateTime(2026, 1, createdDay));

    [Fact]
    public void Nothing_shadows_a_term_nobody_else_spells()
    {
        var a = Term("Unity");
        var b = Term("Godot");

        Assert.Empty(GlossaryUsage.ShadowsByTerm([a, b]));
    }

    [Fact]
    public void An_alias_of_one_term_shadows_another_terms_name()
    {
        var winner = Term("рендерер", aliases: "рендерера", createdDay: 1);
        var loser = Term("рендерера", createdDay: 2);

        Assert.Equal(winner.Id, GlossaryUsage.ShadowsByTerm([winner, loser])[loser.Id]);
    }

    [Fact]
    public void Terms_in_different_languages_never_meet()
    {
        var ru = Term("Unity", language: Languages.Russian, createdDay: 1);
        var en = Term("Unity", language: Languages.English, createdDay: 2);

        Assert.Empty(GlossaryUsage.ShadowsByTerm([ru, en]));
    }

    // Neither document ever sees both, so neither can take the other's position.
    [Fact]
    public void Terms_scoped_to_different_projects_never_meet()
    {
        var first = Term("ferry", projectId: Guid.NewGuid(), createdDay: 1);
        var second = Term("ferry", projectId: Guid.NewGuid(), createdDay: 2);

        Assert.Empty(GlossaryUsage.ShadowsByTerm([first, second]));
    }

    [Fact]
    public void A_project_term_shadows_a_global_one_whatever_their_ages()
    {
        var global = Term("ferry", createdDay: 1);
        var scoped = Term("ferry", projectId: Guid.NewGuid(), createdDay: 9);

        var shadows = GlossaryUsage.ShadowsByTerm([global, scoped]);

        Assert.Equal(scoped.Id, shadows[global.Id]);
        Assert.False(shadows.ContainsKey(scoped.Id));
    }

    [Fact]
    public void Two_case_insensitive_spellings_of_one_word_collide()
    {
        var first = Term("Unity", createdDay: 1);
        var second = Term("UNITY", createdDay: 2);

        Assert.Equal(first.Id, GlossaryUsage.ShadowsByTerm([first, second])[second.Id]);
    }

    // The pair the first wording missed. A case-insensitive term reaches the other's spelling on
    // its own, so the text "unity" is matched by both and one of them loses the scan there — which
    // is the zero the field exists to explain.
    [Fact]
    public void One_case_insensitive_term_is_enough_for_two_spellings_to_meet()
    {
        var insensitive = Term("Unity", createdDay: 1);
        var sensitive = Term("unity", caseSensitive: true, createdDay: 2);

        Assert.Equal(insensitive.Id, GlossaryUsage.ShadowsByTerm([insensitive, sensitive])[sensitive.Id]);
    }

    // The pair the flag exists for: no single string matches both, so they never compete.
    [Fact]
    public void Two_case_sensitive_terms_spelled_differently_never_meet()
    {
        var industry = Term("IT", caseSensitive: true, createdDay: 1);
        var pronoun = Term("it", caseSensitive: true, createdDay: 2);

        Assert.Empty(GlossaryUsage.ShadowsByTerm([industry, pronoun]));
    }

    [Fact]
    public void An_exact_spelling_still_collides_when_one_term_is_case_sensitive()
    {
        var sensitive = Term("IT", caseSensitive: true, createdDay: 1);
        var insensitive = Term("IT", createdDay: 2);

        Assert.Equal(sensitive.Id, GlossaryUsage.ShadowsByTerm([sensitive, insensitive])[insensitive.Id]);
    }

    // Same scope and the same instant: without the id step the winner would depend on the order the
    // rows came back in, and the field could disagree with the count it sits next to.
    [Fact]
    public void Terms_of_the_same_age_break_the_tie_the_same_way_whatever_order_they_arrive_in()
    {
        var low = Term("Unity", id: new Guid("00000000-0000-0000-0000-000000000001"), createdDay: 3);
        var high = Term("Unity", id: new Guid("00000000-0000-0000-0000-000000000002"), createdDay: 3);

        var forwards = GlossaryUsage.ShadowsByTerm([low, high]);
        var backwards = GlossaryUsage.ShadowsByTerm([high, low]);

        Assert.Equal(forwards, backwards);
        Assert.Equal(low.Id, GlossaryUsage.InPriorityOrder([high, low])[0].Id);
    }

    [Fact]
    public void The_named_winner_is_the_highest_priority_of_several()
    {
        var project = Guid.NewGuid();
        var scoped = Term("Unity", projectId: project, createdDay: 5);
        var oldGlobal = Term("Unity", createdDay: 1);
        var newGlobal = Term("Unity", createdDay: 2);

        var shadows = GlossaryUsage.ShadowsByTerm([oldGlobal, newGlobal, scoped]);

        Assert.Equal(scoped.Id, shadows[oldGlobal.Id]);
        Assert.Equal(scoped.Id, shadows[newGlobal.Id]);
    }

    [Fact]
    public void Blank_aliases_do_not_make_every_term_collide()
    {
        var a = Term("Unity", aliases: " , ,  ", createdDay: 1);
        var b = Term("Godot", aliases: ",,", createdDay: 2);

        Assert.Empty(GlossaryUsage.ShadowsByTerm([a, b]));
    }
}
