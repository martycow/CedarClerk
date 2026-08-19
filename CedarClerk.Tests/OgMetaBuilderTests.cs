using CedarClerk.Core;

namespace CedarClerk.Tests;

public class OgMetaBuilderTests
{
    private static OgMetaInput Input(
        string title = "Title",
        string? description = "Description.",
        string? imageUrl = "https://blog.mooexe.dev/og-default.png",
        IReadOnlyList<(string, string)>? alternates = null,
        bool isArticle = true) =>
        new(title, description, "https://blog.mooexe.dev/hello", imageUrl, 1200, 630,
            "Dev Diary", "ru",
            alternates ?? [("ru", "https://blog.mooexe.dev/hello"), ("en", "https://blog.mooexe.dev/hello?lang=en")],
            "https://blog.mooexe.dev/hello",
            new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 18, 9, 30, 0, DateTimeKind.Utc),
            isArticle);

    [Fact]
    public void None_emits_nothing()
    {
        Assert.Equal("", OgMetaBuilder.Build(Input(), OgMetaPolicy.None));
    }

    [Fact]
    public void Title_is_escaped()
    {
        var html = OgMetaBuilder.Build(Input(title: "<b>\"Q&A\"</b>"), OgMetaPolicy.Full);

        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;b&gt;&quot;Q&amp;A&quot;&lt;/b&gt;", html);
    }

    [Fact]
    public void Title_image_only_withholds_description_times_and_cluster()
    {
        var html = OgMetaBuilder.Build(Input(), OgMetaPolicy.TitleImageOnly);

        Assert.Contains("og:title", html);
        Assert.Contains("og:image", html);
        Assert.Contains("og:image:width\" content=\"1200", html);
        Assert.DoesNotContain("description", html);
        Assert.DoesNotContain("article:", html);
        Assert.DoesNotContain("hreflang", html);
    }

    [Fact]
    public void Full_carries_times_description_and_hreflang_cluster()
    {
        var html = OgMetaBuilder.Build(Input(), OgMetaPolicy.Full);

        Assert.Contains("article:published_time\" content=\"2026-08-01T12:00:00Z", html);
        Assert.Contains("article:modified_time\" content=\"2026-08-18T09:30:00Z", html);
        Assert.Contains("og:description\" content=\"Description.", html);
        Assert.Contains("hreflang=\"en\" href=\"https://blog.mooexe.dev/hello?lang=en", html);
        Assert.Contains("hreflang=\"x-default\" href=\"https://blog.mooexe.dev/hello\"", html);
        Assert.Contains("og:locale\" content=\"ru_RU", html);
        Assert.Contains("og:locale:alternate\" content=\"en_US", html);
    }

    [Fact]
    public void A_page_without_image_emits_no_image_tags()
    {
        var html = OgMetaBuilder.Build(Input(imageUrl: null), OgMetaPolicy.Full);

        Assert.DoesNotContain("og:image", html);
        Assert.DoesNotContain("twitter:image", html);
    }

    [Fact]
    public void Description_truncates_at_a_word_boundary()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 60));

        var cut = OgMetaBuilder.TruncateAtWord(text)!;

        Assert.True(cut.Length <= OgMetaBuilder.DescriptionMaxLength + 1);
        Assert.EndsWith("word…", cut);
        Assert.DoesNotContain("wor…", cut.Replace("word…", ""));
    }

    [Fact]
    public void Short_description_passes_untouched()
    {
        Assert.Equal("short", OgMetaBuilder.TruncateAtWord("  short  "));
        Assert.Null(OgMetaBuilder.TruncateAtWord(null));
    }
}
