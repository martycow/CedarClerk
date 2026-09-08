using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CedarClerk.Tests;

// ADR-293 — the invariants the schema does not carry: every project holds one row per module key,
// documents never go off, an unknown key is refused, and the rows leave with the project.
public class ProjectModulesTests
{
    private const string BeforeModules = "20260905095858_AddDraftLastTelegramSentAt";
    private const string Owner = "owner";

    private static CedarDbContext Platform(SqliteConnection c) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(c).Options, TenantProvider.Platform());

    [Fact]
    public void Every_preset_unfolds_into_every_key()
    {
        foreach (var preset in ProjectTypes.All)
        {
            var rows = ProjectModules.ForPreset(preset);
            Assert.NotNull(rows);
            Assert.Equal(ProjectModules.All, rows!.Keys);
            Assert.True(rows[ProjectModules.Documents]);
        }
        Assert.Null(ProjectModules.ForPreset("jam"));
        Assert.Null(ProjectModules.ForPreset(null));
    }

    [Theory]
    [InlineData(ProjectTypes.Blog, ProjectModules.Site, true)]
    [InlineData(ProjectTypes.Blog, ProjectModules.Builds, false)]
    [InlineData(ProjectTypes.Blog, ProjectModules.Calendar, true)]
    [InlineData(ProjectTypes.FullGame, ProjectModules.Dialogues, true)]
    [InlineData(ProjectTypes.FullGame, ProjectModules.Calendar, false)]
    [InlineData(ProjectTypes.FullGame, ProjectModules.Site, false)]
    [InlineData(ProjectTypes.Work, ProjectModules.Canvas, true)]
    [InlineData(ProjectTypes.Work, ProjectModules.Assets, false)]
    [InlineData(ProjectTypes.Product, ProjectModules.Builds, true)]
    [InlineData(ProjectTypes.Product, ProjectModules.Canvas, false)]
    [InlineData(ProjectTypes.Vault, ProjectModules.Posts, false)]
    [InlineData(ProjectTypes.Empty, ProjectModules.Assets, false)]
    public void The_matrix_matches_the_decision(string preset, string key, bool enabled)
    {
        Assert.Equal(enabled, ProjectModules.ForPreset(preset)![key]);
    }

    [Fact]
    public void Documents_cannot_be_switched_off_and_unknown_keys_are_refused()
    {
        Assert.Null(ProjectModules.Refuse(new Dictionary<string, bool> { [ProjectModules.Builds] = false }));
        Assert.Null(ProjectModules.Refuse(new Dictionary<string, bool> { [ProjectModules.Documents] = true }));

        var off = ProjectModules.Refuse(new Dictionary<string, bool> { [ProjectModules.Documents] = false });
        Assert.Equal((ProjectModules.Refusal.RequiredOff, ProjectModules.Documents), off);

        var unknown = ProjectModules.Refuse(new Dictionary<string, bool> { ["wiki"] = true });
        Assert.Equal((ProjectModules.Refusal.UnknownKey, "wiki"), unknown);
    }

    [Fact]
    public void Deleting_a_project_takes_its_module_rows_along()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var db = Platform(connection))
        {
            db.Database.EnsureCreated();
            db.Users.Add(new ApplicationUser { Id = Owner, UserName = "o@x.test", Email = "o@x.test" });
            var project = new Project { OwnerId = Owner, Name = "p", CreatedFromPreset = ProjectTypes.Blog };
            project.Modules.AddRange(ProjectModules.ForPreset(ProjectTypes.Blog)!.Select(m =>
                new ProjectModule { OwnerId = Owner, ProjectId = project.Id, ModuleKey = m.Key, Enabled = m.Value }));
            db.Projects.Add(project);
            db.SaveChanges();
            Assert.Equal(ProjectModules.All.Count, db.ProjectModules.Count());

            db.Projects.Remove(db.Projects.Single());
            db.SaveChanges();
        }
        using var check = Platform(connection);
        Assert.Empty(check.ProjectModules);
    }

    [Fact]
    public void The_migration_backfills_every_existing_project_from_its_preset()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = Platform(connection);
        db.GetService<IMigrator>().Migrate(BeforeModules);

        InsertRow(connection, "AspNetUsers", new() { ["Id"] = $"'{Owner}'" });
        var presets = new[] { "blog", "fullgame", "product", "empty", "jam", "released" };
        foreach (var preset in presets)
            InsertRow(connection, "Projects", new()
            {
                ["Id"] = $"'{Guid.NewGuid().ToString().ToUpperInvariant()}'",
                ["OwnerId"] = $"'{Owner}'",
                ["Name"] = $"'{preset}'",
                ["ProjectType"] = $"'{preset}'",
            });

        db.GetService<IMigrator>().Migrate();

        var rows = db.ProjectModules.AsNoTracking().ToList();
        Assert.Equal(presets.Length * ProjectModules.All.Count, rows.Count);
        Assert.All(rows, r => Assert.Equal(Owner, r.OwnerId));

        var byName = db.Projects.AsNoTracking().Include(p => p.Modules).ToDictionary(p => p.Name);
        foreach (var preset in new[] { "blog", "fullgame", "product", "empty" })
        {
            var expected = ProjectModules.ForPreset(preset)!;
            var actual = byName[preset].Modules.ToDictionary(m => m.ModuleKey, m => m.Enabled);
            Assert.Equal(expected, actual);
        }
        // Retired presets fold into the nearest survivor rather than being left without rows.
        Assert.Equal(ProjectTypes.FullGame, byName["jam"].CreatedFromPreset);
        Assert.True(byName["jam"].Modules.Single(m => m.ModuleKey == ProjectModules.Dialogues).Enabled);
        Assert.Equal(ProjectTypes.Product, byName["released"].CreatedFromPreset);
        Assert.True(byName["released"].Modules.Single(m => m.ModuleKey == ProjectModules.Builds).Enabled);
    }

    /// <summary>
    /// Inserts through the pre-migration schema, where the column is still ProjectType. Every other
    /// NOT NULL column gets a typed blank so the row is legal without naming the whole table.
    /// </summary>
    private static void InsertRow(SqliteConnection connection, string table, Dictionary<string, string> values)
    {
        var columns = new List<(string Name, string Type)>();
        using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                var notNull = reader.GetInt32(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                if (notNull && !hasDefault) columns.Add((reader.GetString(1), reader.GetString(2)));
            }
        }

        foreach (var (name, type) in columns)
            values.TryAdd(name, type == "INTEGER" ? "0" : name.EndsWith("At") ? "'2026-01-01 00:00:00'" : "''");

        using var insert = connection.CreateCommand();
        insert.CommandText =
            $"INSERT INTO \"{table}\" ({string.Join(", ", values.Keys.Select(k => $"\"{k}\""))}) " +
            $"VALUES ({string.Join(", ", values.Values)})";
        insert.ExecuteNonQuery();
    }
}
