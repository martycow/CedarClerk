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
}
