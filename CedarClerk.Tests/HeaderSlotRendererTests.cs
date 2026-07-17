using CedarClerk.Core;

namespace CedarClerk.Tests;

public class HeaderSlotRendererTests
{
    private static readonly HeaderSlotContext Empty = new(null, null, null, null, 0, 0);

    [Fact]
    public void AuthorSignature_renders_the_display_name()
    {
        var ctx = Empty with { AuthorDisplayName = "Marty" };
        Assert.Equal(new HeaderSlotValue("Marty", null), HeaderSlotRenderer.Render(HeaderSlotType.AuthorSignature, ctx));
    }

    [Fact]
    public void AuthorSignature_returns_null_when_not_set()
    {
        Assert.Null(HeaderSlotRenderer.Render(HeaderSlotType.AuthorSignature, Empty));
    }

    [Fact]
    public void Url_renders_as_a_link_to_itself()
    {
        var ctx = Empty with { ProfileUrl = "https://example.com" };
        var value = HeaderSlotRenderer.Render(HeaderSlotType.Url, ctx);
        Assert.Equal(new HeaderSlotValue("https://example.com", "https://example.com"), value);
    }

    [Fact]
    public void MapLocation_renders_text_and_a_google_maps_search_link()
    {
        var ctx = Empty with { ProfileLocation = "Tbilisi, Georgia" };
        var value = HeaderSlotRenderer.Render(HeaderSlotType.MapLocation, ctx);
        Assert.Equal("Tbilisi, Georgia", value!.Text);
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=Tbilisi%2C%20Georgia", value.LinkUrl);
    }

    [Fact]
    public void PublishedDate_formats_the_date_and_returns_null_when_unpublished()
    {
        var ctx = Empty with { PublishedAt = new DateTime(2026, 7, 17) };
        Assert.Equal(new HeaderSlotValue("17 Jul 2026", null), HeaderSlotRenderer.Render(HeaderSlotType.PublishedDate, ctx));
        Assert.Null(HeaderSlotRenderer.Render(HeaderSlotType.PublishedDate, Empty));
    }

    [Fact]
    public void Length_reports_character_count()
    {
        var ctx = Empty with { CharacterCount = 1234 };
        Assert.Equal(new HeaderSlotValue("1234 characters", null), HeaderSlotRenderer.Render(HeaderSlotType.Length, ctx));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(199, 1)]
    [InlineData(200, 1)]
    [InlineData(201, 2)]
    [InlineData(1000, 5)]
    public void TimeToRead_rounds_up_from_200_words_per_minute_with_a_1_minute_floor(int wordCount, int expectedMinutes)
    {
        var ctx = Empty with { WordCount = wordCount };
        Assert.Equal(new HeaderSlotValue($"{expectedMinutes} min read", null), HeaderSlotRenderer.Render(HeaderSlotType.TimeToRead, ctx));
    }
}
