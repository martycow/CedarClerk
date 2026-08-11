using CedarClerk.Core;
using CedarClerk.Server;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

// ADR-092. The wallet moves real money's worth of credits, so the two properties that make it
// safe to wire into webhooks and the publish queue — idempotency by (Reason, Ref) and refusal to
// overdraw — are pinned here.
public class CreditWalletTests
{
    private static CedarDbContext NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var opts = new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options;
        var db = new CedarDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Balance_is_the_sum_of_the_ledger()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 10, CreditReasons.Purchase, "s1");
        await CreditWallet.GrantAsync(db, "u1", 50, CreditReasons.Purchase, "s2");
        Assert.True(await CreditWallet.TryChargeAsync(db, "u1", 1, CreditReasons.XPost, "job1"));

        Assert.Equal(59, await CreditWallet.BalanceAsync(db, "u1"));
        Assert.Equal(0, await CreditWallet.BalanceAsync(db, "someone-else"));
    }

    [Fact]
    public async Task A_repeated_grant_with_the_same_ref_adds_nothing()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 10, CreditReasons.Purchase, "session-1");
        await CreditWallet.GrantAsync(db, "u1", 10, CreditReasons.Purchase, "session-1"); // webhook retry

        Assert.Equal(10, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task A_repeated_charge_with_the_same_ref_succeeds_without_a_second_deduction()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 10, CreditReasons.Purchase, "s1");
        Assert.True(await CreditWallet.TryChargeAsync(db, "u1", 1, CreditReasons.XPost, "job-1"));
        Assert.True(await CreditWallet.TryChargeAsync(db, "u1", 1, CreditReasons.XPost, "job-1")); // queue retry

        Assert.Equal(9, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task A_charge_beyond_the_balance_is_refused_and_changes_nothing()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 2, CreditReasons.Purchase, "s1");

        Assert.False(await CreditWallet.TryChargeAsync(db, "u1", 3, CreditReasons.XPost, "job-1"));
        Assert.Equal(2, await CreditWallet.BalanceAsync(db, "u1"));

        // The refused charge left the wallet intact, so an affordable one still goes through.
        Assert.True(await CreditWallet.TryChargeAsync(db, "u1", 1, CreditReasons.XPost, "job-2"));
        Assert.Equal(1, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task An_empty_wallet_refuses_the_first_charge()
    {
        using var db = NewDb();
        Assert.False(await CreditWallet.TryChargeAsync(db, "u1", 1, CreditReasons.XPost, "job-1"));
        Assert.Equal(0, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public void Packs_have_unique_ids_and_a_bulk_discount_that_never_inverts()
    {
        Assert.Equal(CreditPacks.All.Count, CreditPacks.All.Select(p => p.Id).Distinct().Count());
        var perCredit = CreditPacks.All.OrderBy(p => p.Credits)
            .Select(p => (double)p.PriceUsdCents / p.Credits).ToList();
        for (var i = 1; i < perCredit.Count; i++)
            Assert.True(perCredit[i] <= perCredit[i - 1], "a bigger pack must not cost more per credit");
        Assert.Null(CreditPacks.Find("nope"));
        Assert.Null(CreditPacks.Find(null));
    }

    // Admin credit adjustment (11.08.2026, Marty's request). The wallet moves money's worth, so the
    // boundary that matters is the one where a correction would take a balance somewhere the rest
    // of the app cannot read.
    [Fact]
    public async Task An_admin_can_top_a_balance_up()
    {
        using var db = NewDb();
        Assert.True(await CreditWallet.TryAdjustAsync(db, "u1", 10, CreditReasons.AdminGrant, "a"));
        Assert.Equal(10, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task Granting_the_same_amount_twice_on_purpose_gives_twice_as_much()
    {
        // Unlike a purchase, an admin decision is its own event: two grants of ten are twenty. The
        // endpoint passes a fresh reference each time, which is what makes that true.
        using var db = NewDb();
        await CreditWallet.TryAdjustAsync(db, "u1", 10, CreditReasons.AdminGrant, Guid.NewGuid().ToString());
        await CreditWallet.TryAdjustAsync(db, "u1", 10, CreditReasons.AdminGrant, Guid.NewGuid().ToString());
        Assert.Equal(20, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task A_correction_can_take_credits_back()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 50, CreditReasons.Purchase, "s1");
        Assert.True(await CreditWallet.TryAdjustAsync(db, "u1", -20, CreditReasons.AdminGrant, "fix"));
        Assert.Equal(30, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task Taking_back_more_than_there_is_refuses_rather_than_going_negative()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 5, CreditReasons.Purchase, "s1");

        Assert.False(await CreditWallet.TryAdjustAsync(db, "u1", -6, CreditReasons.AdminGrant, "oops"));
        // And nothing was written: a refused correction must not leave a row behind.
        Assert.Equal(5, await CreditWallet.BalanceAsync(db, "u1"));
        Assert.Equal(1, await db.CreditEntries.CountAsync());
    }

    [Fact]
    public async Task Taking_a_balance_to_exactly_zero_is_allowed()
    {
        using var db = NewDb();
        await CreditWallet.GrantAsync(db, "u1", 5, CreditReasons.Purchase, "s1");
        Assert.True(await CreditWallet.TryAdjustAsync(db, "u1", -5, CreditReasons.AdminGrant, "fix"));
        Assert.Equal(0, await CreditWallet.BalanceAsync(db, "u1"));
    }

    [Fact]
    public async Task Zero_is_refused_because_it_records_a_decision_that_changed_nothing()
    {
        using var db = NewDb();
        Assert.False(await CreditWallet.TryAdjustAsync(db, "u1", 0, CreditReasons.AdminGrant, "nothing"));
        Assert.Equal(0, await db.CreditEntries.CountAsync());
    }

    [Fact]
    public async Task An_adjustment_belongs_to_one_owner_only()
    {
        using var db = NewDb();
        await CreditWallet.TryAdjustAsync(db, "u1", 10, CreditReasons.AdminGrant, "a");
        Assert.Equal(0, await CreditWallet.BalanceAsync(db, "u2"));
    }
}
