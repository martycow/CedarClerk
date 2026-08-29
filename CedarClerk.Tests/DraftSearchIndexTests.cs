using CedarClerk.Server;
using CedarClerk.Server.Search;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Wave 1 item 2 — the FTS5 index end to end: the interceptor keeps rows in step with ordinary
// saves, the triggers survive ExecuteDeleteAsync, and both search surfaces stay inside their
// owner. The table is outside the EF model, so EnsureCreated() does not know it — every writer
// ensures the schema itself, which these tests exercise implicitly by never creating it by hand.
public class DraftSearchIndexTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DraftSearchIndexTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1" });
        db.Users.Add(new ApplicationUser { Id = "o2", UserName = "o2" });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private CedarDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CedarDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new DraftSearchInterceptor())
            .Options, TenantProvider.Platform());

    private static string Doc(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    [Fact]
    public async Task Saving_a_draft_makes_its_body_searchable()
    {
        await using var db = NewContext();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Devlog", CedarJson = Doc("the ferry crosses at dawn") });
        await db.SaveChangesAsync();

        var hits = await new DraftSearchIndex(db).SearchDraftsAsync("o1", "ferry");
        Assert.Single(hits);
        Assert.Equal("Devlog", hits[0].Title);
        Assert.Contains("ferry", hits[0].Snippet);
    }

    [Fact]
    public async Task Updating_a_draft_replaces_its_row_instead_of_adding_one()
    {
        await using var db = NewContext();
        var draft = new Draft { OwnerId = "o1", Title = "Devlog", CedarJson = Doc("about lighthouses") };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();

        draft.CedarJson = Doc("about windmills");
        await db.SaveChangesAsync();

        var index = new DraftSearchIndex(db);
        Assert.Empty(await index.SearchDraftsAsync("o1", "lighthouses"));
        Assert.Single(await index.SearchDraftsAsync("o1", "windmills"));
    }

    [Fact]
    public async Task A_translation_is_its_own_searchable_row()
    {
        await using var db = NewContext();
        var draft = new Draft { OwnerId = "o1", Title = "Пост", CedarJson = Doc("маяк на берегу") };
        db.Drafts.Add(draft);
        db.DraftTranslations.Add(new DraftTranslation
        {
            OwnerId = "o1", DraftId = draft.Id, Language = "en",
            Title = "The post", CedarJson = Doc("a lighthouse on the shore"),
        });
        await db.SaveChangesAsync();

        var hits = await new DraftSearchIndex(db).SearchDraftsAsync("o1", "lighthouse");
        Assert.Single(hits);
        Assert.Equal(draft.Id, hits[0].Id);
    }

    [Fact]
    public async Task ExecuteDelete_removes_rows_through_the_trigger()
    {
        await using var db = NewContext();
        var draft = new Draft { OwnerId = "o1", Title = "Devlog", CedarJson = Doc("about volcanoes") };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();

        // Bypasses the interceptor entirely — only the migration's trigger can clean up here.
        await db.Drafts.Where(d => d.Id == draft.Id).ExecuteDeleteAsync();

        Assert.Empty(await new DraftSearchIndex(db).SearchDraftsAsync("o1", "volcanoes"));
    }

    [Fact]
    public async Task Draft_search_never_crosses_owners()
    {
        await using var db = NewContext();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Mine", CedarJson = Doc("secret meadow plans") });
        db.Drafts.Add(new Draft { OwnerId = "o2", Title = "Theirs", CedarJson = Doc("secret meadow plans") });
        await db.SaveChangesAsync();

        var hits = await new DraftSearchIndex(db).SearchDraftsAsync("o1", "meadow");
        Assert.Single(hits);
        Assert.Equal("Mine", hits[0].Title);
    }

    [Fact]
    public async Task Published_search_only_sees_public_published_posts_of_the_owner()
    {
        await using var db = NewContext();
        db.Drafts.Add(new Draft
        {
            OwnerId = "o1", Title = "Public", CedarJson = Doc("harbor story"),
            IsBlogPublished = true, BlogSlug = "public", BlogPublishedAt = DateTime.UtcNow,
        });
        db.Drafts.Add(new Draft
        {
            OwnerId = "o1", Title = "Private", CedarJson = Doc("harbor story"),
            IsBlogPublished = true, BlogSlug = "private", IsPrivate = true, IsListedWhilePrivate = true,
        });
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Unpublished", CedarJson = Doc("harbor story") });
        db.Drafts.Add(new Draft
        {
            OwnerId = "o2", Title = "Other tenant", CedarJson = Doc("harbor story"),
            IsBlogPublished = true, BlogSlug = "other",
        });
        await db.SaveChangesAsync();

        var hits = await new DraftSearchIndex(db).SearchPublishedAsync("o1", "harbor", null);
        Assert.Single(hits);
        Assert.Equal("public", hits[0].Slug);
    }

    [Fact]
    public async Task Published_search_prefers_the_page_language_and_shows_one_row_per_post()
    {
        await using var db = NewContext();
        var draft = new Draft
        {
            OwnerId = "o1", Title = "Пост про гавань", CedarJson = Doc("гавань и корабли harbor"),
            IsBlogPublished = true, BlogSlug = "harbor-post", BlogPublishedAt = DateTime.UtcNow,
        };
        db.Drafts.Add(draft);
        db.DraftTranslations.Add(new DraftTranslation
        {
            OwnerId = "o1", DraftId = draft.Id, Language = "en",
            Title = "Harbor post", CedarJson = Doc("the harbor and its ships"),
        });
        await db.SaveChangesAsync();

        var hits = await new DraftSearchIndex(db).SearchPublishedAsync("o1", "harbor", "en");
        Assert.Single(hits);
        Assert.Equal("en", hits[0].Language);
        Assert.Equal("Harbor post", hits[0].Title);
    }

    [Fact]
    public async Task Reindex_rebuilds_and_remove_clears()
    {
        await using var db = NewContext();
        var draft = new Draft { OwnerId = "o1", Title = "Devlog", CedarJson = Doc("about glaciers") };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();

        var index = new DraftSearchIndex(db);
        await index.RemoveDraftAsync(draft.Id);
        Assert.Empty(await index.SearchDraftsAsync("o1", "glaciers"));

        await index.ReindexDraftAsync(draft.Id);
        Assert.Single(await index.SearchDraftsAsync("o1", "glaciers"));
    }

    [Theory]
    [InlineData("ferry AND \"dock\" OR (x:y)*", "\"ferry\" \"AND\" \"dock\" \"OR\" \"x\" \"y\"*")]
    [InlineData("hello", "\"hello\"*")]
    [InlineData("()*:^-", "")]
    [InlineData("", "")]
    public void Query_sanitization_disarms_fts_syntax(string raw, string expected) =>
        Assert.Equal(expected, DraftSearchIndex.SanitizeQuery(raw));

    [Fact]
    public async Task Hostile_queries_return_results_or_nothing_but_never_throw()
    {
        await using var db = NewContext();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Devlog", CedarJson = Doc("plain text") });
        await db.SaveChangesAsync();

        var index = new DraftSearchIndex(db);
        foreach (var query in new[] { "NEAR(a b)", "\"unbalanced", "col:val", "-", "* * *", "a OR b" })
            _ = await index.SearchDraftsAsync("o1", query);
    }
}
