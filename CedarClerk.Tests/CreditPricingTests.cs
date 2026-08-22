using CedarClerk.Core;

namespace CedarClerk.Tests;

// ADR-189. Buying credits by the number is priced on the server and nowhere else, so the two
// properties that keep it honest are pinned here: the rate is the packs' own, and the bounds are
// what the checkout endpoints refuse outside of.
public class CreditPricingTests
{
    [Fact]
    public void TheListRateIsTheSmallestPacksRate()
    {
        var smallest = CreditPacks.All[0];
        Assert.Equal(smallest.PriceUsdCents / smallest.Credits, CreditPacks.UnitPriceUsdCents);
        Assert.Equal(smallest.PriceStars / smallest.Credits, CreditPacks.UnitPriceStars);
    }

    // The whole reason the rate is the smallest pack's: a pack has to be worth buying. If a pack
    // ever costs more per credit than typing the number, the packs are a trap and this goes red.
    [Fact]
    public void EveryPackIsCheaperPerCreditThanTheListRate()
    {
        foreach (var pack in CreditPacks.All)
        {
            var perCredit = (double)pack.PriceUsdCents / pack.Credits;
            Assert.True(perCredit <= CreditPacks.UnitPriceUsdCents,
                $"pack {pack.Id} costs {perCredit}¢ per credit, above the {CreditPacks.UnitPriceUsdCents}¢ list rate");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1001)]
    [InlineData(-10)]
    public void RefusesAnAmountOutsideTheBounds(int? credits)
    {
        Assert.Null(CreditPacks.ValidCustom(credits));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(27)]
    [InlineData(1000)]
    public void AcceptsAnAmountInsideTheBounds(int credits)
    {
        Assert.Equal(credits, CreditPacks.ValidCustom(credits));
    }

    // The price a custom order is charged is arithmetic on the server's own numbers — never a
    // figure from the request body, which is the oldest hole in this shape.
    [Fact]
    public void PricesACustomOrderAtTheListRate()
    {
        Assert.Equal(40, CreditPacks.UnitPriceUsdCents);
        Assert.Equal(20, CreditPacks.UnitPriceStars);
        Assert.Equal(1080, 27 * CreditPacks.UnitPriceUsdCents);
        Assert.Equal(540, 27 * CreditPacks.UnitPriceStars);
    }
}
