using CedarClerk.Core;

namespace CedarClerk.Tests;

public class SlugGeneratorTests
{
    [Fact]
    public void Transliterates_cyrillic_title()
    {
        Assert.Equal("privet-mir", SlugGenerator.Slugify("Привет мир"));
    }

    [Fact]
    public void Strips_punctuation_and_collapses_dashes()
    {
        Assert.Equal("hello-world", SlugGenerator.Slugify("Hello,   World!!!"));
    }

    [Fact]
    public void Handles_mixed_cyrillic_latin_and_digits()
    {
        Assert.Equal("moy-post-2026", SlugGenerator.Slugify("Мой Post 2026"));
    }

    [Fact]
    public void Falls_back_to_post_for_empty_or_whitespace_title()
    {
        Assert.Equal("post", SlugGenerator.Slugify("   "));
        Assert.Equal("post", SlugGenerator.Slugify(""));
    }

    [Fact]
    public void Falls_back_to_post_when_title_has_no_sluggable_characters()
    {
        Assert.Equal("post", SlugGenerator.Slugify("!!!???"));
    }

    [Fact]
    public void Is_idempotent_on_already_slug_like_title()
    {
        Assert.Equal("already-a-slug", SlugGenerator.Slugify("already-a-slug"));
    }
}
