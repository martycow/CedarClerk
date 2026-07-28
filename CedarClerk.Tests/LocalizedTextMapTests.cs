using CedarClerk.Core;

namespace CedarClerk.Tests;

// Cross-link labels per content language. Same "primary column + JSON map for the rest" shape as
// RegistrationFormSet, and the same rule: a broken blob must fall back, never throw, because it
// sits on the path that renders a published post.
public class LocalizedTextMapTests
{
    [Fact]
    public void Picks_the_language_when_it_has_one()
    {
        Assert.Equal("Read on the blog", LocalizedTextMap.Pick("Читать в блоге", """{"en":"Read on the blog"}""", "en"));
    }

    [Fact]
    public void Falls_back_to_the_primary_value()
    {
        Assert.Equal("Читать в блоге", LocalizedTextMap.Pick("Читать в блоге", """{"en":"Read on the blog"}""", "de"));
    }

    [Fact]
    public void The_primary_language_never_reads_the_map()
    {
        Assert.Equal("Читать в блоге", LocalizedTextMap.Pick("Читать в блоге", """{"ru":"Другое"}""", "ru"));
    }

    [Fact]
    public void Null_when_nothing_is_set_so_the_caller_uses_its_default()
    {
        Assert.Null(LocalizedTextMap.Pick(null, null, "en"));
    }

    [Fact]
    public void Blank_counts_as_unset_rather_than_as_an_empty_label()
    {
        Assert.Null(LocalizedTextMap.Pick("   ", null, "ru"));
        Assert.Equal("Основной", LocalizedTextMap.Pick("Основной", """{"en":"   "}""", "en"));
    }

    [Fact]
    public void Malformed_json_falls_back_rather_than_throwing()
    {
        Assert.Equal("Основной", LocalizedTextMap.Pick("Основной", "{not json", "en"));
    }

    [Fact]
    public void Set_and_read_back()
    {
        var json = LocalizedTextMap.Set(null, "en", "Read on the blog");
        Assert.Equal("Read on the blog", LocalizedTextMap.Pick("Основной", json, "en"));
    }

    [Fact]
    public void Set_survives_a_second_language()
    {
        var json = LocalizedTextMap.Set(null, "en", "English");
        json = LocalizedTextMap.Set(json, "de", "Deutsch");
        Assert.Equal("English", LocalizedTextMap.Pick("Основной", json, "en"));
        Assert.Equal("Deutsch", LocalizedTextMap.Pick("Основной", json, "de"));
    }

    [Fact]
    public void Clearing_the_last_language_stores_null_rather_than_an_empty_object()
    {
        var json = LocalizedTextMap.Set(null, "en", "English");
        Assert.Null(LocalizedTextMap.Set(json, "en", null));
    }

    [Fact]
    public void All_lists_what_is_set()
    {
        var json = LocalizedTextMap.Set(null, "en", "English");
        Assert.Equal(new Dictionary<string, string> { ["en"] = "English" }, LocalizedTextMap.All(json));
    }
}
