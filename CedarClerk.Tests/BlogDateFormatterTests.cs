using CedarClerk.Localization;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-094 — the blog used to print a hardcoded Russian month heading above an English card date on
// the same page. What is tested is that both follow the page's language, and that Russian inflects
// the month after a day number, which is the difference between a date and a machine-made date.
public class BlogDateFormatterTests
{
    private static readonly DateTime August17 = new(2026, 8, 17, 14, 5, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ru", "Август 2026")]
    [InlineData("en", "August 2026")]
    [InlineData("de", "August 2026")]
    [InlineData("ja", "2026年8月")]
    public void Month_headings_follow_the_page_language(string lang, string expected) =>
        Assert.Equal(expected, BlogDateFormatter.MonthHeading(August17, lang));

    [Theory]
    [InlineData("ru", "17 августа 2026")]
    [InlineData("uk", "17 серпня 2026")]
    [InlineData("en", "17 August 2026")]
    [InlineData("ja", "2026年8月17日")]
    public void Dates_use_the_form_that_follows_a_number(string lang, string expected) =>
        Assert.Equal(expected, BlogDateFormatter.Date(August17, lang));

    [Fact]
    public void A_heading_and_a_date_on_one_page_agree_with_each_other()
    {
        // The actual defect: these two came from different code paths and different languages.
        Assert.Contains("Август", BlogDateFormatter.MonthHeading(August17, "ru"));
        Assert.Contains("августа", BlogDateFormatter.Date(August17, "ru"));
        Assert.Contains("August", BlogDateFormatter.MonthHeading(August17, "en"));
        Assert.Contains("August", BlogDateFormatter.Date(August17, "en"));
    }

    [Fact]
    public void An_unknown_or_missing_language_falls_back_to_english()
    {
        Assert.Equal("August 2026", BlogDateFormatter.MonthHeading(August17, null));
        Assert.Equal("August 2026", BlogDateFormatter.MonthHeading(August17, "xx"));
    }

    [Fact]
    public void The_short_form_carries_a_time()
    {
        Assert.Equal("17 августа 2026, 14:05", BlogDateFormatter.DateTimeShort(August17, "ru"));
    }
}
