using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// The whole migration chain applied to a scratch database — the check ef-migrations.md's collapse
// procedure does by hand, kept green continuously. SchemaDriftGuardTests proves the model matches
// the snapshot; this proves the chain can actually BUILD that schema from nothing, which the
// 27.07.2026 drift incident showed is a separate fact (two applied migrations were missing from
// the repo while the snapshot carried their changes).
public class MigrationChainTests
{
    [Fact]
    public void The_full_chain_applies_to_a_scratch_database()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        using var db = new CedarDbContext(options, TenantProvider.Platform());

        db.Database.Migrate();

        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.Contains(db.Database.GetAppliedMigrations(), m => m.EndsWith("_AddWave2Rhythm"));

        // The migrated schema really carries the Wave 2 tables — a smoke write per new table.
        db.Users.Add(new ApplicationUser { Id = "t", UserName = "t@x.test", Email = "t@x.test" });
        db.QueueSlots.Add(new QueueSlot { OwnerId = "t", TargetId = Guid.NewGuid() });
        db.ChannelInviteLinks.Add(new ChannelInviteLink { OwnerId = "t", ChannelId = Guid.NewGuid(), InviteLink = "https://t.me/+x" });
        db.ChannelMemberDailies.Add(new ChannelMemberDaily { OwnerId = "t", ChannelId = Guid.NewGuid(), Day = DateTime.UtcNow.Date });
        var link = new TrackedLink { OwnerId = "t", Code = "SMOKE001", Url = "https://x.test" };
        db.TrackedLinks.Add(link);
        db.TrackedLinkClickDailies.Add(new TrackedLinkClickDaily { OwnerId = "t", TrackedLinkId = link.Id, Day = DateTime.UtcNow.Date });
        db.SaveChanges();
    }
}
