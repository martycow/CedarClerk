using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// ADR-320 — one entry, a row per language. What is pinned here: the language rows are written as
// a set, a document is matched against its own language's row only, and the blanks of a row read
// as the entry's own name and description.
public class GlossaryEntriesTests : IDisposable
{
    private const string Owner = "owner-a";
    private const string Other = "owner-b";
    private const string Free = "owner-free";

    private readonly Microsoft.Data.Sqlite.SqliteConnection connection;
    private readonly CedarDbContext db;

    public GlossaryEntriesTests()
    {
        connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "a@x.test", TenantUsername = "a", PlanTier = PlanTiers.Forever });
        db.Users.Add(new ApplicationUser { Id = Other, UserName = "b@x.test", TenantUsername = "b", PlanTier = PlanTiers.Forever });
        db.Users.Add(new ApplicationUser { Id = Free, UserName = "f@x.test", TenantUsername = "f" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static GlossaryEntries.LanguageInput In(string language, string? name = null, string? description = null,
        params string[] spellings) => new(language, name, spellings, description);

    private static GlossaryEntries.EntryInput Input(string name, params GlossaryEntries.LanguageInput[] languages) =>
        new(name, $"about {name}", null, false, null, languages);

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;

    private static string Error(IResult result) =>
        JsonSerializer.SerializeToNode(((IValueHttpResult)result).Value)!["error"]!.GetValue<string>();

    private async Task<GlossaryEntry> CreateAsync(GlossaryEntries.EntryInput input, string owner = Owner)
    {
        var result = await GlossaryEntries.CreateAsync(db, owner, input);
        Assert.Equal(200, Status(result));
        var id = ((GlossaryEntries.EntryDto)((IValueHttpResult)result).Value!).Id;
        db.ChangeTracker.Clear();
        return await db.GlossaryEntries.Include(e => e.Languages).SingleAsync(e => e.Id == id);
    }

    private static string Doc(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    private async Task<string> RenderAsync(string text, string language, Guid? projectId = null, Guid? draftId = null) =>
        CedarToBlogHtmlRenderer.Render(Doc(text), "https://blog.test", language,
            await GlossaryEntries.LoadForAsync(db, Owner, language, projectId, draftId));

    [Fact]
    public async Task An_entry_is_stored_with_a_row_per_language()
    {
        var entry = await CreateAsync(new("  Renderer ", " Draws frames ", "/media/asset_1.png", true, null,
        [
            In("en", "renderer", null, "renderers", " ", "rendering engine"),
            In("ru", " рендерер ", " Рисует кадры ", "рендерера", "рендереру"),
        ]));

        Assert.Equal(("Renderer", "Draws frames", "/media/asset_1.png", true),
            (entry.Name, entry.Description, entry.ImageUrl, entry.IsCaseSensitive));
        Assert.Equal(2, entry.Languages.Count);
        var english = entry.Languages.Single(l => l.Language == "en");
        var russian = entry.Languages.Single(l => l.Language == "ru");
        Assert.Equal(["renderers", "rendering engine"], GlossaryEntries.ParseSpellings(english.SpellingsJson));
        Assert.Equal("", english.LocalizedDescription);
        Assert.Equal(("рендерер", "Рисует кадры"), (russian.LocalizedName, russian.LocalizedDescription));
        Assert.Equal(["рендерера", "рендереру"], GlossaryEntries.ParseSpellings(russian.SpellingsJson));
        Assert.Contains("рендерера", russian.SpellingsJson);
    }

    [Theory]
    [InlineData("", "d", "en", 400)]
    [InlineData("Unity", "", "en", 400)]
    [InlineData("Unity", "d", "xx", 400)]
    [InlineData("Unity", "d", null, 400)]
    public async Task An_entry_needs_a_name_a_description_and_a_known_language(string name, string description, string? language, int status)
    {
        var languages = language is null ? [] : new[] { In(language) };
        var result = await GlossaryEntries.CreateAsync(db, Owner, new(name, description, null, false, null, languages));

        Assert.Equal(status, Status(result));
        Assert.Empty(db.GlossaryEntries);
    }

    [Fact]
    public async Task A_language_cannot_be_listed_twice_and_limits_are_enforced()
    {
        Assert.Equal(400, Status(await GlossaryEntries.CreateAsync(db, Owner, Input("Unity", In("en"), In("en")))));
        Assert.Equal(400, Status(await GlossaryEntries.CreateAsync(db, Owner,
            Input(new string('x', GlossaryEntries.NameMaxLength + 1), In("en")))));
        Assert.Equal(400, Status(await GlossaryEntries.CreateAsync(db, Owner,
            Input("Unity", In("en", new string('x', GlossaryEntries.NameMaxLength + 1))))));
        Assert.Equal(400, Status(await GlossaryEntries.CreateAsync(db, Owner,
            Input("Unity", In("en", null, null, new string('x', 300), new string('y', 300))))));
        Assert.Empty(db.GlossaryEntries);
    }

    [Fact]
    public async Task A_free_plan_cannot_add_a_locked_language_but_keeps_editing_one_it_has()
    {
        var refused = await GlossaryEntries.CreateAsync(db, Free, Input("Unity", In("en"), In("ru")));
        Assert.Equal(403, Status(refused));

        var entry = await CreateAsync(Input("Unity", In("en")), Free);
        db.GlossaryEntryLanguages.Add(new GlossaryEntryLanguage { OwnerId = Free, EntryId = entry.Id, Language = "ru", LocalizedName = "Юнити" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var edited = await GlossaryEntries.UpdateAsync(db, Free, entry.Id, Input("Unity", In("en"), In("ru", "Юнити 3D")));
        Assert.Equal(200, Status(edited));
        Assert.Equal(403, Status(await GlossaryEntries.UpdateAsync(db, Free, entry.Id, Input("Unity", In("en"), In("ru"), In("de")))));
    }

    [Fact]
    public async Task An_update_keeps_the_rows_it_still_names_adds_new_ones_and_drops_the_rest()
    {
        var entry = await CreateAsync(Input("Unity", In("en", "Unity"), In("ru", "Юнити"), In("de", "Unity")));
        var english = entry.Languages.Single(l => l.Language == "en").Id;
        var german = entry.Languages.Single(l => l.Language == "de").Id;
        var draft = new Draft { OwnerId = Owner, Title = "post", CedarJson = Doc("Unity und Unity"), PrimaryLanguage = "de" };
        db.Drafts.Add(draft);
        db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion { OwnerId = Owner, DraftId = draft.Id, GlossaryTermId = english, Language = "en" });
        db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion { OwnerId = Owner, DraftId = draft.Id, GlossaryTermId = german, Language = "de" });
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForDraftAsync(db, Owner, draft.Id);
        Assert.Single(db.GlossaryTermUsages.Where(u => u.GlossaryTermId == german));
        db.ChangeTracker.Clear();

        var result = await GlossaryEntries.UpdateAsync(db, Owner, entry.Id,
            new("Unity Engine", "A game engine", null, true, null, [In("en", "Unity", null, "Unity3D"), In("fr", "Unity")]));

        Assert.Equal(200, Status(result));
        db.ChangeTracker.Clear();
        var saved = await db.GlossaryEntries.Include(e => e.Languages).SingleAsync();
        Assert.Equal(("Unity Engine", "A game engine", true), (saved.Name, saved.Description, saved.IsCaseSensitive));
        Assert.Equal(["en", "fr"], saved.Languages.Select(l => l.Language).Order());
        Assert.Equal(english, saved.Languages.Single(l => l.Language == "en").Id);
        Assert.Equal(["Unity3D"], GlossaryEntries.ParseSpellings(saved.Languages.Single(l => l.Language == "en").SpellingsJson));
        Assert.Equal(english, Assert.Single(db.DraftGlossaryExclusions).GlossaryTermId);
        Assert.Empty(db.GlossaryTermUsages.Where(u => u.GlossaryTermId == german));
    }

    [Fact]
    public async Task Another_owners_entry_is_neither_updated_nor_deleted()
    {
        var entry = await CreateAsync(Input("Unity", In("en")));

        Assert.Equal(404, Status(await GlossaryEntries.UpdateAsync(db, Other, entry.Id, Input("Hijacked", In("en")))));
        Assert.Equal(404, Status(await GlossaryEntries.DeleteAsync(db, Other, entry.Id)));
        Assert.Equal("Unity", (await db.GlossaryEntries.SingleAsync()).Name);
        Assert.Single(db.GlossaryEntryLanguages);
    }

    [Fact]
    public async Task Deleting_an_entry_takes_its_languages_exclusions_and_usage_with_it()
    {
        var entry = await CreateAsync(Input("Unity", In("en"), In("ru", "Юнити")));
        var draft = new Draft { OwnerId = Owner, Title = "post", CedarJson = Doc("Unity"), PrimaryLanguage = "en" };
        db.Drafts.Add(draft);
        db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion
            { OwnerId = Owner, DraftId = draft.Id, GlossaryTermId = entry.Languages[0].Id, Language = entry.Languages[0].Language });
        await db.SaveChangesAsync();
        await GlossaryUsage.SyncForDraftAsync(db, Owner, draft.Id);
        Assert.NotEmpty(db.GlossaryTermUsages);
        db.ChangeTracker.Clear();

        Assert.Equal(204, Status(await GlossaryEntries.DeleteAsync(db, Owner, entry.Id)));

        Assert.Empty(db.GlossaryEntries);
        Assert.Empty(db.GlossaryEntryLanguages);
        Assert.Empty(db.DraftGlossaryExclusions);
        Assert.Empty(db.GlossaryTermUsages);
    }

    [Fact]
    public async Task A_document_is_matched_against_the_row_of_its_own_language_only()
    {
        await CreateAsync(new("Renderer", "Draws frames", null, false, null,
        [
            In("en", "renderer", "Draws the frame", "renderers"),
            In("ru", "рендерер", "Рисует кадр", "рендерера"),
        ]));

        var english = await RenderAsync("Two renderers and a рендерер", "en");
        var russian = await RenderAsync("Два рендерера и renderer", "ru");
        var german = await RenderAsync("Ein renderer", "de");

        Assert.Contains("data-term=\"renderer\" data-desc=\"Draws the frame\">renderers</span>", english);
        Assert.DoesNotContain(">рендерер</span>", english);
        Assert.Contains("data-term=\"рендерер\" data-desc=\"Рисует кадр\">рендерера</span>", russian);
        Assert.DoesNotContain(">renderer</span>", russian);
        Assert.DoesNotContain("glossary-term", german);
    }

    [Fact]
    public async Task A_blank_localized_name_or_description_reads_as_the_entrys_own()
    {
        await CreateAsync(new("Unity", "A game <engine> & \"more\"", "/media/asset_u.png", false, null, [In("en"), In("ru", null, "Игровой движок")]));

        var english = await RenderAsync("Unity here", "en");
        var russian = await RenderAsync("Unity тут", "ru");

        Assert.Contains("data-term=\"Unity\" data-desc=\"A game &lt;engine&gt; &amp; &quot;more&quot;\" data-img=\"/media/asset_u.png\">Unity</span>", english);
        Assert.Contains("data-term=\"Unity\" data-desc=\"Игровой движок\"", russian);
    }

    [Fact]
    public async Task The_case_flag_belongs_to_the_entry_and_covers_names_and_spellings()
    {
        await CreateAsync(new("IT", "The industry", null, true, null, [In("en", "IT", null, "ITs")]));
        await CreateAsync(new("Engine", "A motor", null, false, null, [In("en", "engine")]));

        var html = await RenderAsync("IT is not it, ITs are not its, an ENGINE", "en");

        Assert.Contains(">IT</span>", html);
        Assert.Contains(">ITs</span>", html);
        Assert.DoesNotContain(">it</span>", html);
        Assert.DoesNotContain(">its</span>", html);
        Assert.Contains(">ENGINE</span>", html);
    }

    [Fact]
    public async Task A_project_entry_wins_inside_its_project_and_is_absent_outside()
    {
        var project = Guid.NewGuid();
        await CreateAsync(new("Ferry", "A boat", null, false, null, [In("en", "ferry")]));
        await CreateAsync(new("Ferry", "The Cedar Quest ferry", null, false, project, [In("en", "ferry")]));
        await CreateAsync(new("Harbour", "Where the ferry docks", null, false, project, [In("en", "harbour")]));

        var inside = await RenderAsync("The ferry and the harbour", "en", project);
        var outside = await RenderAsync("The ferry and the harbour", "en");

        Assert.Contains("data-desc=\"The Cedar Quest ferry\"", inside);
        Assert.Contains(">harbour</span>", inside);
        Assert.Contains("data-desc=\"A boat\"", outside);
        Assert.DoesNotContain(">harbour</span>", outside);
    }

    [Fact]
    public async Task A_document_can_leave_out_one_language_row_without_touching_the_others()
    {
        var entry = await CreateAsync(Input("Unity", In("en"), In("ru", "Юнити")));
        var draft = Guid.NewGuid();
        db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion
            { OwnerId = Owner, DraftId = draft, GlossaryTermId = entry.Languages.Single(l => l.Language == "en").Id, Language = "en" });
        await db.SaveChangesAsync();

        Assert.DoesNotContain("glossary-term", await RenderAsync("Unity", "en", draftId: draft));
        Assert.Contains("glossary-term", await RenderAsync("Юнити", "ru", draftId: draft));
        Assert.Contains("glossary-term", await RenderAsync("Unity", "en", draftId: Guid.NewGuid()));
    }

    [Fact]
    public async Task Saving_an_entry_counts_every_language_it_is_used_in()
    {
        db.Drafts.Add(new Draft { OwnerId = Owner, Title = "en", CedarJson = Doc("Unity and unity3d"), PrimaryLanguage = "en" });
        db.Drafts.Add(new Draft { OwnerId = Owner, Title = "ru", CedarJson = Doc("Юнити"), PrimaryLanguage = "ru" });
        await db.SaveChangesAsync();

        var entry = await CreateAsync(Input("Unity", In("en", null, null, "unity3d"), In("ru", "Юнити"), In("de")));

        var usage = await db.GlossaryTermUsages.AsNoTracking().ToListAsync();
        Assert.Equal(2, usage.Single(u => u.GlossaryTermId == entry.Languages.Single(l => l.Language == "en").Id).Occurrences);
        Assert.Equal(1, usage.Single(u => u.GlossaryTermId == entry.Languages.Single(l => l.Language == "ru").Id).Occurrences);
        Assert.Equal(2, usage.Count);
    }

    [Fact]
    public async Task The_list_shape_carries_usage_and_shadow_per_language_row()
    {
        var older = await CreateAsync(Input("Unity", In("en")));
        var newer = await CreateAsync(Input("Unity Engine", In("en", "Unity")));
        var rows = await GlossaryEntries.RowsAsync(db.GlossaryEntryLanguages.Where(l => l.OwnerId == Owner));
        var shadows = GlossaryUsage.ShadowsByTerm(rows.Select(r => r.ToTermRow()).ToList());
        var winner = new[] { older, newer }.Single(e => !shadows.ContainsKey(e.Languages[0].Id));
        var loser = new[] { older, newer }.Single(e => e != winner);

        var dto = GlossaryEntries.ToDto(loser, new Dictionary<Guid, int>(), shadows);

        Assert.Equal(winner.Languages[0].Id, dto.Languages[0].ShadowedByTermId);
        Assert.Equal(0, dto.Languages[0].UsedInDrafts);
        Assert.Null(GlossaryEntries.ToDto(loser).Languages[0].UsedInDrafts);
    }

    [Fact]
    public void A_damaged_spellings_column_reads_as_no_spellings()
    {
        Assert.Empty(GlossaryEntries.ParseSpellings("not json"));
        Assert.Empty(GlossaryEntries.ParseSpellings(null));
        Assert.Equal(["a", "b"], GlossaryEntries.ParseSpellings("""[" a ", "", null, "b"]"""));
    }
}
