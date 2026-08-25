using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// A public slug is unique inside one blog and means nothing outside it. Global uniqueness was the
// old shape and it is what made one account's name collide with another's.
public class BlogSlugUniquenessTests
{
    private static CedarDbContext Database()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "a", UserName = "a" });
        db.Users.Add(new ApplicationUser { Id = "b", UserName = "b" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public void Two_owners_may_publish_the_same_blog_slug()
    {
        using var db = Database();
        db.Drafts.Add(new Draft { OwnerId = "a", Title = "A", BlogSlug = "hello" });
        db.Drafts.Add(new Draft { OwnerId = "b", Title = "B", BlogSlug = "hello" });

        db.SaveChanges();

        Assert.Equal(2, db.Drafts.Count(d => d.BlogSlug == "hello"));
    }

    [Fact]
    public void One_owner_may_not_publish_a_blog_slug_twice()
    {
        using var db = Database();
        db.Drafts.Add(new Draft { OwnerId = "a", Title = "A", BlogSlug = "hello" });
        db.Drafts.Add(new Draft { OwnerId = "a", Title = "A again", BlogSlug = "hello" });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Unpublished_drafts_do_not_collide()
    {
        using var db = Database();
        db.Drafts.Add(new Draft { OwnerId = "a", Title = "One" });
        db.Drafts.Add(new Draft { OwnerId = "a", Title = "Two" });

        db.SaveChanges();

        Assert.Equal(2, db.Drafts.Count());
    }

    [Fact]
    public void Two_owners_may_publish_the_same_showcase_slug()
    {
        using var db = Database();
        db.Projects.Add(new Project { OwnerId = "a", Name = "A", ShowcaseSlug = "game" });
        db.Projects.Add(new Project { OwnerId = "b", Name = "B", ShowcaseSlug = "game" });

        db.SaveChanges();

        Assert.Equal(2, db.Projects.Count(p => p.ShowcaseSlug == "game"));
    }

    [Fact]
    public void One_owner_may_not_publish_a_showcase_slug_twice()
    {
        using var db = Database();
        db.Projects.Add(new Project { OwnerId = "a", Name = "A", ShowcaseSlug = "game" });
        db.Projects.Add(new Project { OwnerId = "a", Name = "A again", ShowcaseSlug = "game" });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }
}
