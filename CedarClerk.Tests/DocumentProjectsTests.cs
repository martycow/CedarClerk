using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace CedarClerk.Tests;

public class DocumentProjectsTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection connection = new("Data Source=:memory:");
    private readonly CedarDbContext db;
    public DocumentProjectsTests()
    {
        connection.Open();
        db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.AddRange(new ApplicationUser { Id = "a", UserName = "a@example.test" }, new ApplicationUser { Id = "b", UserName = "b@example.test" });
        db.SaveChanges();
    }
    [Fact]
    public async Task Backfill_preserves_documents_and_separates_owners()
    {
        var existing = new Project { OwnerId = "a", Name = "Existing" };
        var bound = new Draft { OwnerId = "a", ProjectId = existing.Id, Title = "Bound" };
        var a = new Draft { OwnerId = "a", Title = "Keep", CedarJson = "{\"type\":\"doc\"}", BlogSlug = "same-url", IsBlogPublished = true };
        var b = new Draft { OwnerId = "b", Title = "Other" };
        db.Projects.Add(existing);
        db.Drafts.AddRange(bound, a, b);
        await db.SaveChangesAsync();
        Assert.Equal(2, await DocumentProjects.BackfillAsync(db));
        Assert.Equal(existing.Id, bound.ProjectId);
        Assert.Equal(DocumentProjects.PersonalId("a"), a.ProjectId);
        Assert.Equal(DocumentProjects.PersonalId("b"), b.ProjectId);
        Assert.NotEqual(a.ProjectId, b.ProjectId);
        Assert.Equal("same-url", a.BlogSlug);
        Assert.True(a.IsBlogPublished);
        Assert.Equal("{\"type\":\"doc\"}", a.CedarJson);
        Assert.Equal(0, await DocumentProjects.BackfillAsync(db));
        Assert.Equal(3, await db.Projects.CountAsync());
        Assert.True(await db.ProjectModules.AnyAsync(m => m.ProjectId == a.ProjectId && m.ModuleKey == ProjectModules.Documents && m.Enabled));
    }
    public void Dispose() { db.Dispose(); connection.Dispose(); }
}
