using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// T-120 (ADR-102/103). Two rules carry real consequences and neither is visible from a signature:
// what an untyped, pre-module draft is allowed to do, and whether a project can be emptied.
public class DocumentTypesTests
{
    // Drafts carry a real FK to AspNetUsers (they have an Owner navigation property), so the owners
    // have to exist before a draft can be saved — unlike CreditEntry, whose tests get away with a
    // bare owner id.
    private static CedarDbContext NewDb(params string[] owners)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts, TenantProvider.Platform());
        db.Database.EnsureCreated();
        foreach (var owner in owners)
            db.Users.Add(new ApplicationUser { Id = owner, UserName = owner, Email = owner + "@example.com" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public void A_new_draft_is_a_post()
    {
        // The whole argument for a column instead of a data migration: the default IS the truth
        // about every row written before types existed.
        Assert.Equal(DocumentTypes.Post, new Draft().DocumentType);
    }

    [Theory]
    [InlineData("post")]
    [InlineData("design")]
    [InlineData("script")]
    [InlineData("plot")]
    [InlineData("changelog")]
    [InlineData("note")]
    public void Known_types_are_known(string type) => Assert.True(DocumentTypes.IsKnown(type));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Post")]      // types are compared exactly; a capitalised one is a client bug
    [InlineData("gdd")]
    public void Anything_else_is_rejected(string? type) => Assert.False(DocumentTypes.IsKnown(type));

    [Fact]
    public void Working_material_does_not_publish_but_posts_and_changelogs_do()
    {
        Assert.True(DocumentTypes.IsPublishable(DocumentTypes.Post));
        Assert.True(DocumentTypes.IsPublishable(DocumentTypes.Changelog));
        Assert.False(DocumentTypes.IsPublishable(DocumentTypes.Design));
        Assert.False(DocumentTypes.IsPublishable(DocumentTypes.Script));
        Assert.False(DocumentTypes.IsPublishable(DocumentTypes.Plot));
        Assert.False(DocumentTypes.IsPublishable(DocumentTypes.Note));
    }

    [Fact]
    public void A_null_type_still_publishes()
    {
        // The regression this pins is the one the first generated migration would have shipped:
        // it backfilled existing drafts with "" rather than "post", and an unpublishable "" would
        // have made every post ever written refuse to publish on the first deploy. Null takes the
        // same lenient branch for the same reason — an absent type means "written before types".
        Assert.True(DocumentTypes.IsPublishable(null));
        Assert.False(DocumentTypes.IsPublishable(""));
    }

    [Theory]
    [InlineData(ProjectTypes.FullGame, DocumentTypes.Design)]
    [InlineData(ProjectTypes.Jam, DocumentTypes.Design)]
    [InlineData(ProjectTypes.Prototype, DocumentTypes.Note)]
    [InlineData(ProjectTypes.Released, DocumentTypes.Changelog)]
    [InlineData(ProjectTypes.Blog, DocumentTypes.Post)]
    public void Each_project_type_starts_with_its_own_document(string projectType, string expected)
    {
        Assert.Equal(expected, ProjectTypes.StarterDocumentType(projectType));
    }

    [Fact]
    public void An_unknown_project_type_still_produces_a_document()
    {
        // ADR-103 — a project cannot exist without one, so this branch must never return nothing.
        Assert.Equal(DocumentTypes.Post, ProjectTypes.StarterDocumentType(null));
        Assert.Equal(DocumentTypes.Post, ProjectTypes.StarterDocumentType("mmo"));
        Assert.True(DocumentTypes.IsKnown(ProjectTypes.StarterDocumentType("anything")));
    }

    [Fact]
    public void Every_project_type_starts_with_a_real_document_type()
    {
        // Guards the pairing itself: a typo in StarterDocumentType would otherwise only surface as
        // a 400 at the moment someone creates a project.
        Assert.All(ProjectTypes.All, t => Assert.True(DocumentTypes.IsKnown(ProjectTypes.StarterDocumentType(t))));
    }

    [Fact]
    public async Task The_last_document_of_a_project_is_recognised()
    {
        using var db = NewDb("u1");
        var projectId = Guid.NewGuid();
        var only = new Draft { OwnerId = "u1", ProjectId = projectId, Title = "only" };
        db.Drafts.Add(only);
        await db.SaveChangesAsync();

        Assert.True(await ProjectEndpoints.IsLastDocumentOfProjectAsync(db, only.Id, projectId, "u1"));

        var second = new Draft { OwnerId = "u1", ProjectId = projectId, Title = "second" };
        db.Drafts.Add(second);
        await db.SaveChangesAsync();

        Assert.False(await ProjectEndpoints.IsLastDocumentOfProjectAsync(db, only.Id, projectId, "u1"));
    }

    [Fact]
    public async Task A_document_outside_a_project_is_never_the_last_one()
    {
        using var db = NewDb("u1");
        var loose = new Draft { OwnerId = "u1", Title = "unfiled" };
        db.Drafts.Add(loose);
        await db.SaveChangesAsync();

        // Every draft written before the module is this case, so a false positive here would refuse
        // to delete anything in the app.
        Assert.False(await ProjectEndpoints.IsLastDocumentOfProjectAsync(db, loose.Id, null, "u1"));
    }

    [Fact]
    public async Task Someone_elses_document_does_not_keep_a_project_alive()
    {
        using var db = NewDb("u1", "u2");
        var projectId = Guid.NewGuid();
        var mine = new Draft { OwnerId = "u1", ProjectId = projectId, Title = "mine" };
        // Not a real state — ownership is enforced everywhere — but the count must be owner-scoped
        // regardless, or one tenant's rows would decide another tenant's refusals.
        db.Drafts.AddRange(mine, new Draft { OwnerId = "u2", ProjectId = projectId, Title = "theirs" });
        await db.SaveChangesAsync();

        Assert.True(await ProjectEndpoints.IsLastDocumentOfProjectAsync(db, mine.Id, projectId, "u1"));
    }
}
