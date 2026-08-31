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

    // T-152/T-351 (31.08.2026, table approved whole) — AI moved off the flat daily quota onto
    // credits: a document translation is the priciest call in the app, the small calls (an AI
    // edit, a glossary/form/profile translate) cost a fraction of it. The daily cap stays as an
    // abuse ceiling, not as the price.
    public const int AiTranslateCost = 2;
    public const int AiSmallCost = 1;

    /// <summary>Credits granted with every successful Pro+ subscription payment.</summary>
    public const int ProPlusMonthlyCredits = 30;

    public static readonly IReadOnlyList<CreditPack> All =
    [
        new("10", 10, 400, 200),
        new("50", 50, 1800, 900),
        new("100", 100, 3000, 1500),
    ];

    public static CreditPack? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// ADR-189 — the list rate for buying credits by the number, taken from the smallest pack so the
    /// packs are visibly a discount rather than a second price list. Derived, never written twice:
    /// moving a pack's price moves this with it.
    /// </summary>
    public static int UnitPriceUsdCents => All[0].PriceUsdCents / All[0].Credits;

    public static int UnitPriceStars => All[0].PriceStars / All[0].Credits;

    /// <summary>
    /// Below five the provider's own fee is most of the charge; above a thousand it stops being a
    /// top-up and a four-figure charge nobody meant to make is a refund conversation.
    /// </summary>
    public const int MinCustomCredits = 5;
    public const int MaxCustomCredits = 1000;

    /// <summary>The credits a request asks for, or null when it asks for nothing sellable.</summary>
    public static int? ValidCustom(int? credits) =>
        credits is >= MinCustomCredits and <= MaxCustomCredits ? credits : null;
}

/// <summary>Reasons a CreditEntry row exists — the ledger's vocabulary, shared with the UI.</summary>
public static class CreditReasons
{
    public const string Purchase = "purchase";
    public const string XPost = "x-post";
    public const string AdminGrant = "admin-grant";
    public const string Ai = "ai";
    public const string ProPlusMonthly = "proplus-monthly";
}
