using CedarClerk.Core;

namespace CedarClerk.Tests;

public class ReaderGeoTests
{
    [Theory]
    [InlineData("DE", "DE")]
    [InlineData("de", "DE")]
    [InlineData(" ru ", "RU")]
    public void NormalizeCountry_keeps_real_codes(string header, string expected)
    {
        Assert.Equal(expected, ReaderGeo.NormalizeCountry(header));
    }

    [Theory]
    [InlineData(null)]          // no Cloudflare in front — local run
    [InlineData("")]
    [InlineData("XX")]          // Cloudflare's own "couldn't tell"
    [InlineData("T1")]          // Tor exit node
    [InlineData("DEU")]
    [InlineData("D")]
    [InlineData("<b>")]
    public void NormalizeCountry_buckets_everything_else_as_unknown(string? header)
    {
        Assert.Equal(Consts.General.UnknownGeo, ReaderGeo.NormalizeCountry(header));
    }

    [Theory]
    [InlineData("ru-RU,ru;q=0.9,en-US;q=0.8", "ru")]
    [InlineData("en-GB", "en")]
    [InlineData("DE", "de")]
    [InlineData("*", "??")]
    public void NormalizeLanguage_takes_the_top_primary_subtag(string header, string expected)
    {
        Assert.Equal(expected, ReaderGeo.NormalizeLanguage(header));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("toolongsubtag")]
    [InlineData("e n")]
    public void NormalizeLanguage_buckets_malformed_headers_as_unknown(string? header)
    {
        Assert.Equal(Consts.General.UnknownGeo, ReaderGeo.NormalizeLanguage(header));
    }
}
