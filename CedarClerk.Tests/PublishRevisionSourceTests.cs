using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// T-104 / ADR-269. The Telegram target sends a copy of the document with media paths rewritten to
// Telegram-safe derivatives, and records the *source* as the publish revision. Recording the wire
// copy instead made the next publish diff show every compressed image as changed.
public class PublishRevisionSourceTests
{
    private const string Source =
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]},{"type":"image","attrs":{"src":"/media/photo.png"}}]}""";

    private static readonly Dictionary<string, string> Derivatives = new() { ["photo.png"] = "photo.tg.jpg" };

    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts, TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Draft> SeedDraftAsync(CedarDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1", Email = "owner-1@test.local" });
        var draft = new Draft { Title = "T", CedarJson = Source, OwnerId = "owner-1", PrimaryLanguage = Languages.Russian };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    private static List<string> Kinds(object diff)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(diff));
        return json.RootElement.GetProperty("lines").EnumerateArray()
            .Select(line => line.GetProperty("kind").GetString()!).ToList();
    }

    [Fact]
    public void The_wire_copy_differs_from_the_source_by_every_rewritten_image()
    {
        var wire = CedarPackage.RewriteMediaPaths(Source, Derivatives);

        Assert.Contains("/media/photo.tg.jpg", wire);
        Assert.Contains(Kinds(DraftRevisionService.Diff(wire, Source)), kind => kind != "context");
    }

    [Fact]
    public async Task A_revision_of_the_source_leaves_an_unchanged_document_with_nothing_to_diff()
    {
        using var db = NewDb();
        var draft = await SeedDraftAsync(db);

        await DraftRevisionService.RecordAsync(db, draft.Id, Languages.Russian, "T", Source,
            DraftRevisionService.Kinds.Telegram, "-100", CancellationToken.None);
        await db.SaveChangesAsync();

        var preview = await DraftRevisionService.PreviewAsync(db, draft, Languages.Russian,
            DraftRevisionService.Kinds.Telegram, "-100");

        Assert.NotNull(preview);
        Assert.True(preview!.PublishedBefore);
        Assert.All(Kinds(preview.Diff!), kind => Assert.Equal("context", kind));
        Assert.Equal(Source, Assert.Single(db.DraftRevisions).CedarJson);
    }
}
