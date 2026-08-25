using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CedarClerk.Tests;

// ADR-065. The publish guard and the revision log are the two things standing between an edit and
// silently overwriting a live post, so both are pinned here rather than trusted to review.
public class DraftRevisionServiceTests
{
    [Fact]
    public void Diff_carries_readable_added_removed_and_context_lines()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            DraftRevisionService.Diff(Doc("kept", "removed"), Doc("kept", "added"))));
        var lines = json.RootElement.GetProperty("lines").EnumerateArray().ToList();

        Assert.Contains(lines, line => line.GetProperty("kind").GetString() == "context"
            && line.GetProperty("text").GetString() == "kept");
        Assert.Contains(lines, line => line.GetProperty("kind").GetString() == "removed"
            && line.GetProperty("text").GetString() == "removed");
        Assert.Contains(lines, line => line.GetProperty("kind").GetString() == "added"
            && line.GetProperty("text").GetString() == "added");
    }

    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts, TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    private static string Doc(params string[] paragraphs) =>
        "{\"type\":\"doc\",\"content\":[" +
        string.Join(",", paragraphs.Select(p => $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{p}\"}}]}}")) +
        "]}";

    private static async Task<Draft> SeedDraftAsync(CedarDbContext db, string cedarJson, string primary = Languages.Russian)
    {
        // Draft.OwnerId is a real FK to AspNetUsers, so the owner has to exist first.
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1", Email = "owner-1@test.local" });
        var draft = new Draft { Title = "T", CedarJson = cedarJson, OwnerId = "owner-1", PrimaryLanguage = primary };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    [Fact]
    public async Task Record_skips_a_save_that_repeats_the_previous_content()
    {
        using var db = NewDb();
        var id = (await SeedDraftAsync(db, Doc("seed"))).Id;

        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("a"));
        await db.SaveChangesAsync();
        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("a"));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.DraftRevisions.CountAsync());
    }

    [Fact]
    public async Task Record_keeps_a_save_whose_title_alone_changed()
    {
        using var db = NewDb();
        var id = (await SeedDraftAsync(db, Doc("seed"))).Id;

        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("a"));
        await db.SaveChangesAsync();
        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "renamed", Doc("a"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DraftRevisions.CountAsync());
    }

    [Fact]
    public async Task Record_does_not_confuse_two_languages_of_one_draft()
    {
        using var db = NewDb();
        var id = (await SeedDraftAsync(db, Doc("seed"))).Id;

        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("a"));
        await db.SaveChangesAsync();
        await DraftRevisionService.RecordAsync(db, id, Languages.English, "T", Doc("a"));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.DraftRevisions.CountAsync());
    }

    // The autosave fires on every pause in typing; without a ceiling a few weeks of writing is a
    // few hundred megabytes of near-identical documents on the server.
    [Fact]
    public async Task Save_revisions_are_pruned_but_published_ones_are_kept()
    {
        using var db = NewDb();
        var id = (await SeedDraftAsync(db, Doc("seed"))).Id;

        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("published"),
            DraftRevisionService.Kinds.Telegram, "@chan");
        await db.SaveChangesAsync();

        for (var i = 0; i < 60; i++)
        {
            await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("edit" + i));
            await db.SaveChangesAsync();
        }

        Assert.Equal(50, await db.DraftRevisions.CountAsync(r => r.Kind == DraftRevisionService.Kinds.Save));
        Assert.Equal(1, await db.DraftRevisions.CountAsync(r => r.Kind == DraftRevisionService.Kinds.Telegram));
        // The newest edits are the ones that survive.
        Assert.True(await db.DraftRevisions.AnyAsync(r => r.CedarJson == Doc("edit59")));
        Assert.False(await db.DraftRevisions.AnyAsync(r => r.CedarJson == Doc("edit0")));
    }

    // T-016 — a restore marker is the one point in the history an author will look for later, so
    // it must not be swept away by the edit-history ceiling like an ordinary autosave.
    [Fact]
    public async Task Restore_markers_survive_the_save_pruning()
    {
        using var db = NewDb();
        var id = (await SeedDraftAsync(db, Doc("seed"))).Id;

        await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("rewound here"),
            DraftRevisionService.Kinds.Restore);
        await db.SaveChangesAsync();

        for (var i = 0; i < 60; i++)
        {
            await DraftRevisionService.RecordAsync(db, id, Languages.Russian, "T", Doc("edit" + i));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await db.DraftRevisions.CountAsync(r => r.Kind == DraftRevisionService.Kinds.Restore));
    }

    [Fact]
    public async Task A_first_publication_needs_no_confirmation()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db, Doc("a"));

        Assert.True(await DraftRevisionService.ConfirmationSatisfiedAsync(
            db, draft, Languages.Russian, DraftRevisionService.Kinds.Telegram, "@chan", confirmedFingerprint: null));
    }

    [Fact]
    public async Task Overwriting_a_live_post_requires_the_fingerprint_that_was_shown()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db, Doc("a"));
        await DraftRevisionService.RecordAsync(db, draft.Id, Languages.Russian, draft.Title, draft.CedarJson,
            DraftRevisionService.Kinds.Telegram, "@chan");
        await db.SaveChangesAsync();

        // The document moved on after the preview was taken.
        draft.CedarJson = Doc("a", "b");
        await db.SaveChangesAsync();

        var stalePreviewFingerprint = DraftRevisionService.Fingerprint(draft.Title, Doc("a"));
        Assert.False(await DraftRevisionService.ConfirmationSatisfiedAsync(
            db, draft, Languages.Russian, DraftRevisionService.Kinds.Telegram, "@chan", stalePreviewFingerprint));

        var currentFingerprint = DraftRevisionService.Fingerprint(draft.Title, draft.CedarJson);
        Assert.True(await DraftRevisionService.ConfirmationSatisfiedAsync(
            db, draft, Languages.Russian, DraftRevisionService.Kinds.Telegram, "@chan", currentFingerprint));
    }

    [Fact]
    public async Task Confirming_one_destination_does_not_confirm_another()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db, Doc("a"));
        foreach (var chat in new[] { "@one", "@two" })
        {
            await DraftRevisionService.RecordAsync(db, draft.Id, Languages.Russian, draft.Title, draft.CedarJson,
                DraftRevisionService.Kinds.Telegram, chat);
            await db.SaveChangesAsync();
        }
        draft.CedarJson = Doc("a", "b");
        await db.SaveChangesAsync();

        var wrong = DraftRevisionService.Fingerprint(draft.Title, Doc("a"));
        Assert.False(await DraftRevisionService.ConfirmationSatisfiedAsync(
            db, draft, Languages.Russian, DraftRevisionService.Kinds.Telegram, "@two", wrong));
    }

    [Fact]
    public async Task Preview_reports_the_blocks_an_update_would_change()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db, Doc("a"));
        await DraftRevisionService.RecordAsync(db, draft.Id, Languages.Russian, draft.Title, draft.CedarJson,
            DraftRevisionService.Kinds.Blog);
        await db.SaveChangesAsync();

        draft.CedarJson = Doc("a", "b", "c");
        await db.SaveChangesAsync();

        var preview = await DraftRevisionService.PreviewAsync(db, draft, Languages.Russian, DraftRevisionService.Kinds.Blog, null);

        Assert.NotNull(preview);
        Assert.True(preview!.PublishedBefore);
        Assert.Equal(Languages.Russian, preview.Language);
        Assert.NotNull(preview.Diff);
    }

    [Fact]
    public async Task Preview_of_a_never_published_target_carries_no_diff()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db, Doc("a"));

        var preview = await DraftRevisionService.PreviewAsync(db, draft, Languages.Russian, DraftRevisionService.Kinds.Blog, null);

        Assert.NotNull(preview);
        Assert.False(preview!.PublishedBefore);
        Assert.Null(preview.Diff);
    }

    // The whole point of PrimaryLanguage: the canonical slot is not "the Russian one".
    [Fact]
    public async Task Resolve_reads_the_draft_slot_for_its_own_primary_language()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db, Doc("english body"), Languages.English);
        db.DraftTranslations.Add(new DraftTranslation
        {
            DraftId = draft.Id, OwnerId = draft.OwnerId, Language = Languages.Russian, Title = "Ru", CedarJson = Doc("russian body"),
        });
        await db.SaveChangesAsync();

        var primary = await DraftRevisionService.ResolveAsync(db, draft, Languages.English);
        var translated = await DraftRevisionService.ResolveAsync(db, draft, Languages.Russian);
        var missing = await DraftRevisionService.ResolveAsync(db, draft, Languages.Japanese);

        Assert.Equal(Doc("english body"), primary!.Value.CedarJson);
        Assert.Equal(Doc("russian body"), translated!.Value.CedarJson);
        Assert.Null(missing);
    }

    [Fact]
    public void Fingerprint_separates_title_from_body()
    {
        // "ab" + "" and "a" + "b" must not hash alike, or a rename could pass as an unchanged post.
        Assert.NotEqual(DraftRevisionService.Fingerprint("ab", ""), DraftRevisionService.Fingerprint("a", "b"));
    }

    [Fact]
    public void BlockCount_counts_top_level_blocks_and_survives_junk()
    {
        Assert.Equal(3, DraftRevisionService.BlockCount(Doc("a", "b", "c")));
        Assert.Equal(0, DraftRevisionService.BlockCount("not json at all"));
    }
}
