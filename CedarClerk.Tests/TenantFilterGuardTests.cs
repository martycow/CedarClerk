using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CedarClerk.Tests;

// ApplyTenantFilters is a hand-written list, and a new owned entity that never reaches it is a
// silent cross-tenant leak — nothing fails, the rows are simply visible to everybody. This turns
// that from something to remember into something that goes red.
//
// Verified to actually fail: removing Folder's line from ApplyTenantFilters turns
// Every_entity_with_an_OwnerId_has_a_tenant_filter red and names Folder; removing the early
// `if (tenant.IsPlatform) return;` turns The_platform_model_carries_no_filters_at_all red.
//
// EF 9 obsoletes GetQueryFilter in favour of GetDeclaredQueryFilters. The project is pinned to
// 8.0.*; that is the line to change on the upgrade.
public class TenantFilterGuardTests
{
    // Tables that belong to the platform rather than to any one account. Each needs a reason,
    // because "it has no filter" is exactly what a leak looks like too.
    private static readonly Dictionary<string, string> Unowned = new()
    {
        ["ApplicationUser"] = "Identity reads AspNetUsers on every authorized request, and sign-in happens before the tenant is known.",
        ["InviteCode"] = "One invite list for the whole platform; a code is redeemed before its holder has an account.",
        ["WaitlistEntry"] = "Signups from the landing page, taken before any account exists.",
        ["LandingSettings"] = "One row, and it is the platform's own front door — no account owns the landing page.",
        ["DiscoverySettings"] = "One row controls the shared public Discovery page; individual consent remains on each account.",
        ["AdminAuditEntry"] = "One audit log, written by admins about other accounts.",
        ["BotKnownChat"] = "One shared bot; membership is discovered from Telegram updates, not from a request.",
        ["BotKnownChatAdmin"] = "Hangs off BotKnownChat, same reason.",
    };

    private static CedarDbContext Model(TenantProvider tenant) =>
        // Never opened: the model is built at design time, exactly as SchemaDriftGuardTests does.
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite("Data Source=:memory:").Options, tenant);

    // The ClrType term matches StampOwners' own check. Keep them identical, or the guard and the
    // runtime refusal can disagree about which rows are owned.
    private static bool IsOwned(IEntityType entity) =>
        entity.FindProperty("OwnerId")?.ClrType == typeof(string);

    private static bool IsFrameworkTable(IEntityType entity) =>
        entity.ClrType.Namespace == "Microsoft.AspNetCore.Identity";

    [Fact]
    public void Every_entity_with_an_OwnerId_has_a_tenant_filter()
    {
        using var db = Model(TenantProvider.For("t"));

        var missing = db.Model.GetEntityTypes()
            .Where(IsOwned)
            .Where(e => e.GetQueryFilter() is null)
            .Select(e => e.DisplayName())
            .OrderBy(name => name)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"Owned but unfiltered: {string.Join(", ", missing)}. For each one add " +
            "builder.Entity<X>().HasQueryFilter(e => e.OwnerId == TenantId) to CedarDbContext.ApplyTenantFilters.");
    }

    // A filter copied onto the wrong column passes the count above and leaks anyway.
    [Fact]
    public void Every_tenant_filter_compares_OwnerId()
    {
        using var db = Model(TenantProvider.For("t"));

        // A missing filter is the test above's finding, not this one's.
        foreach (var entity in db.Model.GetEntityTypes().Where(IsOwned).Where(e => e.GetQueryFilter() is not null))
            Assert.Contains("OwnerId", entity.GetQueryFilter()!.ToString());
    }

    // The other half of the same guard: an entity with no OwnerId at all is either a deliberate
    // platform table or an owned one whose column was forgotten, and the two look identical.
    [Fact]
    public void Every_unfiltered_entity_is_a_named_platform_table()
    {
        using var db = Model(TenantProvider.For("t"));

        var unaccounted = db.Model.GetEntityTypes()
            .Where(e => e.GetQueryFilter() is null && !IsFrameworkTable(e))
            .Select(e => e.DisplayName())
            .Where(name => !Unowned.ContainsKey(name))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(unaccounted.Length == 0,
            $"Neither owned nor declared platform-wide: {string.Join(", ", unaccounted)}. Give the entity an " +
            "OwnerId and a filter, or add it to TenantFilterGuardTests.Unowned with the reason it belongs to everybody.");
    }

    [Fact]
    public void The_allow_list_names_only_entities_that_exist()
    {
        using var db = Model(TenantProvider.For("t"));
        var known = db.Model.GetEntityTypes().Select(e => e.DisplayName()).ToHashSet();

        Assert.Empty(Unowned.Keys.Where(name => !known.Contains(name)));
        Assert.DoesNotContain(Unowned, entry => string.IsNullOrWhiteSpace(entry.Value));
    }

    // Two compiled models, not one parameterised model (TenantModelCacheKeyFactory). If this ever
    // regresses, every owner index stops being used and nothing else would notice.
    [Fact]
    public void The_platform_model_carries_no_filters_at_all()
    {
        using var db = Model(TenantProvider.Platform());

        var filtered = db.Model.GetEntityTypes()
            .Where(e => e.GetQueryFilter() is not null)
            .Select(e => e.DisplayName())
            .OrderBy(name => name)
            .ToArray();

        Assert.True(filtered.Length == 0,
            $"A platform context must see everything, but these carry a filter: {string.Join(", ", filtered)}.");
    }

    // Guards StampOwners: a row with no owner would be written and then be invisible to the
    // account that created it, which reads as data loss rather than as a bug.
    [Fact]
    public void An_owned_row_saved_with_no_tenant_is_refused()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;

        using (var platform = new CedarDbContext(options, TenantProvider.Platform()))
            platform.Database.EnsureCreated();

        using var db = new CedarDbContext(options, new TenantProvider());
        db.Drafts.Add(new Draft { Title = "nobody's" });

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("Draft", error.Message);
    }
}
