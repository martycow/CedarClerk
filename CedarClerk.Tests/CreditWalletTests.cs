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
}
