using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

public class UsernameStorageTests
{
    private static CedarDbContext Database()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    private static ApplicationUser User(string email, string? username = null) =>
        new() { UserName = email, Email = email, TenantUsername = username };

    [Fact]
    public void Two_accounts_cannot_hold_the_same_name()
    {
        using var db = Database();
        db.Users.Add(User("a@example.test", "marty"));
        db.SaveChanges();

        db.Users.Add(User("b@example.test", "marty"));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    // Every account that predates the column has no name, and SQLite would read a plain unique
    // index over them as one long list of duplicates.
    [Fact]
    public void Any_number_of_accounts_may_have_no_name()
    {
        using var db = Database();
        db.Users.Add(User("a@example.test"));
        db.Users.Add(User("b@example.test"));
        db.Users.Add(User("c@example.test"));

        db.SaveChanges();

        Assert.Equal(3, db.Users.Count());
    }
}
