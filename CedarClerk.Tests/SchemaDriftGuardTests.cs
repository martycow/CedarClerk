using CedarClerk.Server;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// Turns .claude/rules/ef-migrations.md's "migrate immediately after any Entities.cs change" from
// a rule someone has to remember into a failing test.
//
// The failure mode it guards against is specific and has bitten this project: a schema change
// with no matching migration doesn't break the build, it breaks the *next startup* — and on
// SQLite that surfaces as "no such column" on every authorized request, because Identity's
// security-stamp check touches AspNetUsers constantly.
//
// Verified to actually fail: adding a property to an entity without running `dotnet ef migrations
// add` turns this red. A guard that can't fail would be worse than no guard.
public class SchemaDriftGuardTests
{
    [Fact]
    public void Model_has_no_changes_that_lack_a_migration()
    {
        // Never opened — HasPendingModelChanges compares the model against the compiled-in
        // snapshot, so no real database (and no connection) is involved.
        var opts = new DbContextOptionsBuilder<CedarDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        using var db = new CedarDbContext(opts);

        Assert.False(db.Database.HasPendingModelChanges(),
            "Entities.cs changed without a matching migration. Run: dotnet ef migrations add <Name> --project CedarClerk.Server");
    }
}
