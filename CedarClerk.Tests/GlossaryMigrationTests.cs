using System.Text.Json;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CedarClerk.Tests;

// ADR-320 — the GlossaryEntries migration runs on the production database at startup, so what it
// does to rows of the old shape is pinned here on a database built at the migration before it.
// Everything is read back with plain SQL: the test must keep describing this one migration after
// later ones have changed the entities.
public class GlossaryMigrationTests : IDisposable
{
    private const string Before = "AddProjectBannerUrl";
    private const string Migration = "GlossaryEntries";

    private readonly SqliteConnection connection;
    private readonly CedarDbContext db;
    private readonly List<OldRow> seeded = [];
    private int clock;

    public GlossaryMigrationTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.GetService<IMigrator>().Migrate(Before);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record OldRow(string Id, string Owner, string Term, string Description, string Aliases,
        string? Image, string Language, bool CaseSensitive, string? Source, string? Project, string CreatedAt);

    private static string NewId() => Guid.NewGuid().ToString().ToUpperInvariant();

    private OldRow Old(string term, string language, string aliases = "", string? source = null,
        string owner = "owner-a", string? image = null, bool caseSensitive = false, string? project = null,
        string? id = null, string? description = null)
    {
        var row = new OldRow(id ?? NewId(), owner, term, description ?? $"about {term}", aliases, image, language,
            caseSensitive, source, project, new DateTime(2026, 8, 1).AddMinutes(clock++).ToString("yyyy-MM-dd HH:mm:ss"));
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "GlossaryTerms" ("Id", "OwnerId", "Term", "Description", "Aliases", "ImageUrl", "Language",
                "IsCaseSensitive", "SourceTermId", "ProjectId", "CreatedAt", "UpdatedAt")
            VALUES ($id, $owner, $term, $description, $aliases, $image, $language, $case, $source, $project, $created, $created)
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$owner", row.Owner);
        command.Parameters.AddWithValue("$term", row.Term);
        command.Parameters.AddWithValue("$description", row.Description);
        command.Parameters.AddWithValue("$aliases", row.Aliases);
        command.Parameters.AddWithValue("$image", (object?)row.Image ?? DBNull.Value);
        command.Parameters.AddWithValue("$language", row.Language);
        command.Parameters.AddWithValue("$case", row.CaseSensitive ? 1 : 0);
        command.Parameters.AddWithValue("$source", (object?)row.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$project", (object?)row.Project ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", row.CreatedAt);
        command.ExecuteNonQuery();
        seeded.Add(row);
        return row;
    }

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private List<Dictionary<string, object?>> Query(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private void Migrate() => db.GetService<IMigrator>().Migrate(Migration);

    private Dictionary<string, object?> Language(OldRow row) =>
        Assert.Single(Query($"""SELECT * FROM "GlossaryEntryLanguages" WHERE "Id" = '{row.Id}'"""));

    private string EntryOf(OldRow row) => (string)Language(row)["EntryId"]!;

    private Dictionary<string, object?> Entry(string id) =>
        Assert.Single(Query($"""SELECT * FROM "GlossaryEntries" WHERE "Id" = '{id}'"""));

    private static string[] Spellings(Dictionary<string, object?> language) =>
        JsonSerializer.Deserialize<string[]>((string)language["SpellingsJson"]!)!;

    private static string[] OldAliases(OldRow row) =>
        row.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // The one statement that must hold for every row, whatever group it fell into.
    private void AssertNothingWasLost()
    {
        Assert.Equal(seeded.Count, Query("""SELECT "Id" FROM "GlossaryEntryLanguages" """).Count);
        foreach (var row in seeded)
        {
            var language = Language(row);
            Assert.Equal(row.Owner, language["OwnerId"]);
            Assert.Equal(row.Language, language["Language"]);
            Assert.Equal(row.Term, language["LocalizedName"]);
            Assert.Equal(row.Description, language["LocalizedDescription"]);
            Assert.Equal(row.CreatedAt, language["CreatedAt"]);
            Assert.Equal(OldAliases(row), Spellings(language));

            var entry = Entry((string)language["EntryId"]!);
            Assert.Equal(row.Owner, entry["OwnerId"]);
            Assert.Equal(row.Image, entry["ImageUrl"]);
            Assert.Equal(row.CaseSensitive ? 1L : 0L, entry["IsCaseSensitive"]);
            Assert.Equal(row.Project, entry["ProjectId"]);
        }
        // No entry without a language, and no language twice in one entry.
        Assert.Empty(Query("""
            SELECT e."Id" FROM "GlossaryEntries" e
            WHERE NOT EXISTS (SELECT 1 FROM "GlossaryEntryLanguages" l WHERE l."EntryId" = e."Id")
            """));
        Assert.Empty(Query("""
            SELECT "EntryId" FROM "GlossaryEntryLanguages" GROUP BY "EntryId", "Language" HAVING COUNT(*) > 1
            """));
    }

    [Fact]
    public void A_hand_written_term_becomes_one_entry_with_one_language()
    {
        var project = NewId();
        var term = Old("рендерер", "ru", aliases: "рендерера, рендереру", image: "/media/asset_1.png",
            caseSensitive: true, project: project);

        Migrate();

        AssertNothingWasLost();
        var entry = Entry(EntryOf(term));
        Assert.Equal(term.Id, entry["Id"]);
        Assert.Equal("рендерер", entry["Name"]);
        Assert.Equal("about рендерер", entry["Description"]);
        Assert.Equal(["рендерера", "рендереру"], Spellings(Language(term)));
        Assert.Single(Query("""SELECT "Id" FROM "GlossaryEntries" """));
    }

    [Fact]
    public void A_source_and_its_translations_become_one_entry()
    {
        var source = Old("renderer", "en", aliases: "renderers");
        var russian = Old("рендерер", "ru", aliases: "рендерера,рендереру", source: source.Id);
        var german = Old("Renderer", "de", source: source.Id);

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(source.Id, EntryOf(source));
        Assert.Equal(source.Id, EntryOf(russian));
        Assert.Equal(source.Id, EntryOf(german));
        var entry = Assert.Single(Query("""SELECT * FROM "GlossaryEntries" """));
        Assert.Equal("renderer", entry["Name"]);
        Assert.Equal("about renderer", entry["Description"]);
    }

    [Fact]
    public void Translations_whose_source_is_gone_stay_together_under_the_oldest()
    {
        var missing = NewId();
        var russian = Old("паром", "ru", source: missing);
        var english = Old("ferry", "en", source: missing);
        var alone = Old("tide", "en", source: NewId());

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(russian.Id, EntryOf(russian));
        Assert.Equal(russian.Id, EntryOf(english));
        Assert.Equal("паром", Entry(russian.Id)["Name"]);
        Assert.Equal(alone.Id, EntryOf(alone));
        Assert.Equal(2, Query("""SELECT "Id" FROM "GlossaryEntries" """).Count);
    }

    [Fact]
    public void A_second_row_in_a_language_the_entry_already_has_keeps_an_entry_of_its_own()
    {
        var source = Old("shader", "en");
        var first = Old("шейдер", "ru", aliases: "шейдера", source: source.Id);
        var second = Old("шейдерная программа", "ru", aliases: "шейдерной программы", source: source.Id);
        var sameAsSource = Old("shader program", "en", source: source.Id);

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(source.Id, EntryOf(first));
        Assert.Equal(second.Id, EntryOf(second));
        Assert.Equal("шейдерная программа", Entry(second.Id)["Name"]);
        Assert.Equal(sameAsSource.Id, EntryOf(sameAsSource));
        Assert.Equal(3, Query("""SELECT "Id" FROM "GlossaryEntries" """).Count);
    }

    [Fact]
    public void A_translation_the_entry_cannot_hold_unchanged_keeps_its_own_flags()
    {
        var project = NewId();
        var source = Old("IT", "en", caseSensitive: true, project: project, image: "/media/asset_it.png");
        // What POST /translate wrote for such a source: the image copied, the scope and the flag not.
        var translated = Old("ИТ", "ru", source: source.Id, image: "/media/asset_it.png");
        var otherImage = Old("IT", "de", source: source.Id, caseSensitive: true, project: project, image: "/media/asset_de.png");
        var noImage = Old("TI", "fr", source: source.Id, caseSensitive: true, project: project);
        var fits = Old("TI", "es", source: source.Id, caseSensitive: true, project: project, image: "/media/asset_it.png");
        var otherOwner = Old("IT", "ja", source: source.Id, owner: "owner-b", caseSensitive: true, project: project,
            image: "/media/asset_it.png");

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(translated.Id, EntryOf(translated));
        Assert.Equal(otherImage.Id, EntryOf(otherImage));
        Assert.Equal(noImage.Id, EntryOf(noImage));
        Assert.Equal(otherOwner.Id, EntryOf(otherOwner));
        Assert.Equal(source.Id, EntryOf(fits));
        Assert.Equal(0L, Entry(translated.Id)["IsCaseSensitive"]);
        Assert.Null(Entry(translated.Id)["ProjectId"]);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData(" , ,, ", new string[0])]
    [InlineData("one", new[] { "one" })]
    [InlineData(" a ,, b,c ,", new[] { "a", "b", "c" })]
    [InlineData("say \"hi\",back\\slash,it's", new[] { "say \"hi\"", "back\\slash", "it's" })]
    [InlineData("юнити,Юнити 3D", new[] { "юнити", "Юнити 3D" })]
    public void A_comma_separated_alias_string_becomes_a_list(string aliases, string[] expected)
    {
        var term = Old("Unity", "ru", aliases: aliases);

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(expected, Spellings(Language(term)));
    }

    [Fact]
    public void A_row_that_names_itself_or_a_loop_as_its_source_still_survives()
    {
        var self = NewId();
        var selfReferencing = Old("loop", "en", source: self, id: self);
        var a = NewId();
        var c = NewId();
        var first = Old("alpha", "en", source: c, id: a);
        var second = Old("альфа", "ru", source: a, id: c);
        var chained = Old("Alpha", "de", source: a);

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(selfReferencing.Id, EntryOf(selfReferencing));
        Assert.Contains(EntryOf(first), new[] { first.Id, second.Id });
        Assert.Contains(EntryOf(chained), new[] { first.Id, second.Id, chained.Id });
    }

    [Fact]
    public void A_chain_of_sources_collapses_into_its_root()
    {
        var root = Old("canvas", "en");
        var middle = Old("холст", "ru", source: root.Id);
        var leaf = Old("Leinwand", "de", source: middle.Id);

        Migrate();

        AssertNothingWasLost();
        Assert.Equal(root.Id, EntryOf(middle));
        Assert.Equal(root.Id, EntryOf(leaf));
    }

    [Fact]
    public void Exclusions_and_usage_rows_still_point_at_the_term_they_were_written_for()
    {
        var source = Old("Unity", "en");
        var russian = Old("Юнити", "ru", source: source.Id);
        var draft = NewId();
        Execute($"""
            INSERT INTO "DraftGlossaryExclusions" ("Id", "OwnerId", "DraftId", "GlossaryTermId", "Language")
            VALUES ('{NewId()}', 'owner-a', '{draft}', '{russian.Id}', 'ru')
            """);
        Execute($"""
            INSERT INTO "GlossaryTermUsages" ("Id", "OwnerId", "GlossaryTermId", "DraftId", "Occurrences", "ScannedAt")
            VALUES ('{NewId()}', 'owner-a', '{russian.Id}', '{draft}', 3, '2026-08-01 00:00:00')
            """);

        Migrate();

        AssertNothingWasLost();
        var excluded = Assert.Single(Query("""
            SELECT l."LocalizedName" FROM "DraftGlossaryExclusions" x
            JOIN "GlossaryEntryLanguages" l ON l."Id" = x."GlossaryTermId" AND l."Language" = x."Language"
            """));
        Assert.Equal("Юнити", excluded["LocalizedName"]);
        var used = Assert.Single(Query("""
            SELECT l."LocalizedName", u."Occurrences" FROM "GlossaryTermUsages" u
            JOIN "GlossaryEntryLanguages" l ON l."Id" = u."GlossaryTermId"
            """));
        Assert.Equal(3L, used["Occurrences"]);
    }

    [Fact]
    public void The_old_table_is_left_as_it_was_and_Down_goes_back_to_it()
    {
        var source = Old("Unity", "en", aliases: "unity3d");
        Old("Юнити", "ru", aliases: "юнити", source: source.Id);
        var before = Query("""SELECT * FROM "GlossaryTerms" ORDER BY "Id" """);

        Migrate();

        Assert.Equal(JsonSerializer.Serialize(before),
            JsonSerializer.Serialize(Query("""SELECT * FROM "GlossaryTerms" ORDER BY "Id" """)));
        Assert.Empty(Query("""SELECT name FROM sqlite_temp_master WHERE name LIKE '_Glossary%'"""));

        db.GetService<IMigrator>().Migrate(Before);

        Assert.Equal(JsonSerializer.Serialize(before),
            JsonSerializer.Serialize(Query("""SELECT * FROM "GlossaryTerms" ORDER BY "Id" """)));
        Assert.Empty(Query("""
            SELECT name FROM sqlite_master WHERE name IN ('GlossaryEntries', 'GlossaryEntryLanguages')
            """));
    }

    [Fact]
    public void A_migrated_glossary_renders_a_post_as_it_did_before()
    {
        var project = NewId();
        var source = Old("renderer", "en", aliases: "renderers", description: "Draws <frames> & more");
        Old("рендерер", "ru", aliases: "рендерера", source: source.Id, description: "Рисует кадры");
        Old("renderer", "en", project: project, description: "The project's own renderer");

        db.GetService<IMigrator>().Migrate();

        var global = GlossaryEntries.LoadForAsync(db, "owner-a", "en").GetAwaiter().GetResult();
        var inProject = GlossaryEntries.LoadForAsync(db, "owner-a", "en", Guid.Parse(project)).GetAwaiter().GetResult();
        var russian = GlossaryEntries.LoadForAsync(db, "owner-a", "ru").GetAwaiter().GetResult();

        var term = Assert.Single(global);
        Assert.Equal(("renderer", "Draws <frames> & more"), (term.Term, term.Description));
        Assert.Equal(["renderers"], term.Aliases);
        Assert.Equal("The project's own renderer", Assert.Single(inProject).Description);
        Assert.Equal(("рендерер", "Рисует кадры"), (Assert.Single(russian).Term, russian[0].Description));
    }
}
