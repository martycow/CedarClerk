using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// ADR-092 — the prepaid credit wallet over the CreditEntry ledger. Everything here goes through
/// the (Reason, Ref) unique index: a grant or a charge that already happened is a no-op, not a
/// second movement, which is what lets webhook retries and the publish queue's re-runs stay safe.
/// </summary>
public static class CreditWallet
{
    public static Task<int> BalanceAsync(CedarDbContext db, string ownerId, CancellationToken ct = default) =>
        db.CreditEntries.Where(c => c.OwnerId == ownerId).SumAsync(c => c.Delta, ct);

    /// <summary>Adds credits (a purchase or an admin grant). Idempotent by (reason, ref).</summary>
    public static async Task GrantAsync(CedarDbContext db, string ownerId, int credits, string reason, string? @ref, CancellationToken ct = default)
    {
        if (credits <= 0) throw new ArgumentOutOfRangeException(nameof(credits));
        if (@ref is not null && await db.CreditEntries.AnyAsync(c => c.Reason == reason && c.Ref == @ref, ct))
            return;
        db.CreditEntries.Add(new CreditEntry { OwnerId = ownerId, Delta = credits, Reason = reason, Ref = @ref });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deducts credits, refusing (false) on an insufficient balance. A charge whose (reason, ref)
    /// already exists reports success without a second deduction — the queue retried a job whose
    /// cost was already taken.
    /// </summary>
    public static async Task<bool> TryChargeAsync(CedarDbContext db, string ownerId, int credits, string reason, string @ref, CancellationToken ct = default)
    {
        if (credits <= 0) throw new ArgumentOutOfRangeException(nameof(credits));
        if (await db.CreditEntries.AnyAsync(c => c.Reason == reason && c.Ref == @ref, ct))
            return true;
        if (await BalanceAsync(db, ownerId, ct) < credits)
            return false;
        db.CreditEntries.Add(new CreditEntry { OwnerId = ownerId, Delta = -credits, Reason = reason, Ref = @ref });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// An admin's own correction to a balance: a **signed** movement, positive to top somebody up
    /// and negative to take back a mistake. Returns false when a deduction would leave the balance
    /// below zero — a negative balance is not a state anything else in the app knows how to read.
    ///
    /// Separate from <see cref="GrantAsync"/> and <see cref="TryChargeAsync"/> rather than folded
    /// into either: both of those refuse a non-positive amount on purpose, because a purchase of
    /// -10 or a charge of 0 is a bug at the call site. This one is the single place where a human
    /// deliberately moves a balance in either direction, and it is written to the same ledger, so
    /// the correction is as visible afterwards as whatever it corrects.
    /// </summary>
    public static async Task<bool> TryAdjustAsync(
        CedarDbContext db, string ownerId, int delta, string reason, string? @ref, CancellationToken ct = default)
    {
        if (delta == 0) return false;

        if (delta < 0 && await BalanceAsync(db, ownerId, ct) + delta < 0)
            return false;

        db.CreditEntries.Add(new CreditEntry { OwnerId = ownerId, Delta = delta, Reason = reason, Ref = @ref });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
