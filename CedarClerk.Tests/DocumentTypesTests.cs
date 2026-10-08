using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// T-120 (ADR-102). What an untyped, pre-module draft is allowed to do is not visible from a signature.
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
    [InlineData("regular")]
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
        Assert.True(DocumentTypes.IsPublishable(DocumentTypes.Regular));
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
    [InlineData(ProjectTypes.Product, DocumentTypes.Changelog)]
    [InlineData(ProjectTypes.Blog, DocumentTypes.Post)]
    [InlineData(ProjectTypes.Work, DocumentTypes.Note)]
    [InlineData(ProjectTypes.Vault, DocumentTypes.Note)]
    [InlineData(ProjectTypes.Empty, DocumentTypes.Note)]
    public void Each_project_type_starts_with_its_own_document(string projectType, string expected)
    {
        Assert.Equal(expected, ProjectTypes.StarterDocumentType(projectType));
    }

    [Fact]
    public void An_unknown_project_type_still_produces_a_document()
    {
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
}
