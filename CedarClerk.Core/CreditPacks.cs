namespace CedarClerk.Core;

/// <summary>
/// ADR-092 — the prepaid credit wallet's price list. A credit is the internal currency for
/// anything that costs Cedar Clerk real money per use (an X post today; other paid networks and
/// AI-quota overage later). Prices are ~2× cost so a shift in a network's pricing does not turn
/// every publish into a loss before the packs can be adjusted.
/// </summary>
public record CreditPack(string Id, int Credits, int PriceUsdCents, int PriceStars);

public static class CreditPacks
{
    /// <summary>Credits charged for one successful X post (cost basis ~$0.20 with a link).</summary>
    public const int XPostCost = 1;

    public static readonly IReadOnlyList<CreditPack> All =
    [
        new("10", 10, 400, 200),
        new("50", 50, 1800, 900),
        new("100", 100, 3000, 1500),
    ];

    public static CreditPack? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
}

/// <summary>Reasons a CreditEntry row exists — the ledger's vocabulary, shared with the UI.</summary>
public static class CreditReasons
{
    public const string Purchase = "purchase";
    public const string XPost = "x-post";
    public const string AdminGrant = "admin-grant";
}
