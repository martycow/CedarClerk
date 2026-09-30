using CedarClerk.Localization;
using CedarClerk.Server;

namespace CedarClerk.Tests;

public class LandingContentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("broken")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Missing_or_malformed_editorial_uses_defaults(string? json)
    {
        var content = LandingContent.From(new LandingSettings { EditorialJson = json }, null);
        Assert.Equal("Write", content.Copy("writeTitle", false));
        Assert.Equal("Напишите", content.Copy("writeTitle", true));
    }

    [Fact]
    public void Editorial_roundtrip_keeps_languages_and_uses_defaults_for_blank_halves()
    {
        var json = LandingContent.SerializeEditorial(new()
        {
            ["writeTitle"] = new("  A custom title  ", " "),
            ["faq1Answer"] = new(null, "Ответ автора"),
            ["unknown"] = new("Not a field", null),
        });
        var stored = LandingContent.ReadEditorial(json);
        Assert.DoesNotContain("unknown", stored.Keys);
        Assert.Null(stored["writeTitle"].Ru);
        var content = LandingContent.From(new LandingSettings { EditorialJson = json }, null);
        Assert.Equal("A custom title", content.Copy("writeTitle", false));
        Assert.Equal("Напишите", content.Copy("writeTitle", true));
        Assert.Equal("Ответ автора", content.Copy("faq1Answer", true));
    }
}
