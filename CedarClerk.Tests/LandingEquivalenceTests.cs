using System.Text.RegularExpressions;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// ADR-323 §5 — the page drawn from a legacy LandingSettings row through the block model is the page
// the pre-block renderer drew from that row. Compared after collapsing every run of whitespace to
// one space, which is all that differs: the block renderer indents its fragments differently.
public class LandingEquivalenceTests
{
    public static TheoryData<string> Rows => [.. LegacyRows.All.Keys];

    private static string Collapse(string html) => Regex.Replace(html, @"\s+", " ");

    private static void AssertSamePage(string name, string? configuredShowcase, DiscoveryEndpoints.Snapshot discovery,
        string? analyticsKey, Func<string, string>? adjustLegacy = null)
    {
        var row = LegacyRows.All[name]();
        foreach (var ru in new[] { false, true })
        {
            var legacy = LegacyLandingRenderer.Render(ru, LandingContent.From(row, configuredShowcase), discovery,
                analyticsKey, "https://eu.example");
            var blocks = LandingRenderer.Render(ru ? "ru" : "en", LandingDocument.FromRow(row, configuredShowcase), discovery,
                configuredShowcase, analyticsKey, "https://eu.example");

            var expected = Collapse(legacy);
            if (adjustLegacy is not null) expected = adjustLegacy(expected);
            var actual = Collapse(blocks);
            Assert.Contains("<footer class=\"ruler\">", actual);
            Assert.Contains("id=\"waitlist-form\"", actual);
            if (expected == actual) continue;

            var at = Enumerable.Range(0, Math.Min(expected.Length, actual.Length)).FirstOrDefault(i => expected[i] != actual[i], Math.Min(expected.Length, actual.Length));
            var from = Math.Max(0, at - 120);
            Assert.Fail($"{name} ({(ru ? "ru" : "en")}) differs at {at}:\nlegacy: …{Slice(expected, from)}\nblocks: …{Slice(actual, from)}");
        }
    }

    private static string Slice(string text, int from) => text[from..Math.Min(text.Length, from + 320)];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_legacy_row_draws_the_same_page_through_the_block_model(string name)
    {
        AssertSamePage(name, null, LegacyRows.EmptyDiscovery, null);
        AssertSamePage(name, null, LegacyRows.BusyDiscovery, "phc_key");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("empty")]
    [InlineData("edited")]
    [InlineData("everything-on")]
    public void A_configured_showcase_blog_draws_the_same_links(string name) =>
        AssertSamePage(name, "blog.cedar.test", LegacyRows.BusyDiscovery, null);

    // The one tolerated difference. With screenshots switched off and a showcase blog set, the old
    // page kept an empty gallery container beside the examples copy; a hidden block draws nothing.
    [Theory]
    [InlineData("screenshots-off")]
    [InlineData("all-off")]
    public void Hidden_screenshots_drop_only_the_empty_gallery_container(string name)
    {
        const string emptyGallery = """<div id="shots" class="gallery"> </div> """;
        var seen = 0;
        AssertSamePage(name, "blog.cedar.test", LegacyRows.EmptyDiscovery, null, legacy =>
        {
            Assert.Contains(emptyGallery, legacy);
            seen++;
            return legacy.Replace(emptyGallery, "");
        });
        Assert.Equal(2, seen);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_document_read_from_a_legacy_row_is_one_the_editor_can_save(string name)
    {
        var document = LandingDocument.FromRow(LegacyRows.All[name](), "blog.cedar.test");
        if (name == "untidy")
        {
            // Milestones and columns saved without a title were drawn as they were; saving them
            // again asks for the title.
            Assert.NotNull(document.Validate());
            return;
        }
        Assert.Null(document.Validate());
        var reread = LandingDocument.Parse(document.Serialize())!;
        Assert.Equal(document.Serialize(), reread.Serialize());
    }

    [Fact]
    public void The_conversion_is_deterministic()
    {
        var row = LegacyRows.All["edited"]();
        Assert.Equal(
            LandingDocument.FromLegacy(row, "blog.cedar.test").Serialize(),
            LandingDocument.FromLegacy(row, "blog.cedar.test").Serialize());
    }

    [Fact]
    public void Legacy_content_lands_in_the_blocks_it_was_drawn_from()
    {
        var document = LandingDocument.FromLegacy(LegacyRows.All["edited"](), null);

        Assert.Equal(["hero", "features", "examples", "discovery", "tools", "pricing", "faq", "roadmap", "story", "download", "closing"],
            document.Sections.Select(s => s.Id));
        Assert.Equal(["en", "ru"], document.Languages);
        Assert.Equal("showcase.cedar.test", document.ShowcaseBlog);

        var hero = document.Sections[0];
        Assert.Equal("Edited <br> headline & more", hero.Blocks[0].Text["title"]["en"]);
        Assert.Equal(LandingTexts.HeroTitle.Ru, hero.Blocks[0].Text["title"]["ru"]);
        Assert.Equal("78 makers already publish", hero.Blocks[1].Text["proof"]["en"]);
        Assert.Equal("78 makers already publish", hero.Blocks[1].Text["proof"]["ru"]);
        Assert.Equal("shot_hero.png", hero.Blocks[2].Items.Single().File);

        var examples = document.Sections[2];
        Assert.Equal("https://showcase.cedar.test", examples.Blocks[0].Url);
        Assert.Equal(["shot_two.webp", "shot_three.jpg"], examples.Blocks[1].Items.Select(i => i.File));

        Assert.Equal(16, document.Features.Count);
        Assert.Equal(16, document.Features.Select(f => f.Id).Distinct().Count());
        Assert.Equal("/assets/review/glossary.png", document.Features.Single(f => f.Id == "glossary").Shot);
        Assert.Equal("{networks} networks, one text", document.Features.Single(f => f.Id == "networks").Title["en"]);
        Assert.Equal("{languages} языков в одной записи", document.Features.Single(f => f.Id == "languages").Title["ru"]);

        var roadmap = document.Sections.Single(s => s.Id == "roadmap");
        Assert.False(roadmap.Hidden);
        Assert.Equal(["done", "next"], roadmap.Blocks[1].Items.Select(i => i.Mark));
        Assert.Equal(2, roadmap.Blocks[1].Items[0].Entries.Count);
        Assert.Equal(11, LandingBlocks.All.Count);
    }

    [Fact]
    public void A_stored_document_wins_over_the_legacy_columns_and_a_broken_one_does_not()
    {
        var row = LegacyRows.All["edited"]();
        var stored = LandingDocument.FromLegacy(null, null);
        stored.Sections[0].Blocks[0].Text["title"]["en"] = "Stored headline";
        row.DocumentJson = stored.Serialize();
        Assert.Equal("Stored headline", LandingDocument.FromRow(row, null).Sections[0].Blocks[0].Text["title"]["en"]);
        Assert.True(LandingDocument.IsStored(row));

        row.DocumentJson = "{ not json";
        Assert.Equal("Edited <br> headline & more", LandingDocument.FromRow(row, null).Sections[0].Blocks[0].Text["title"]["en"]);
        Assert.False(LandingDocument.IsStored(row));
    }
}

internal static class LegacyRows
{
    private static LandingText T(string? en, string? ru) => new(en, ru);

    public static readonly Dictionary<string, Func<LandingSettings?>> All = new()
    {
        ["none"] = () => null,
        ["empty"] = () => new LandingSettings(),
        ["edited"] = Edited,
        ["everything-on"] = () =>
        {
            var row = Edited();
            row.ShowDownload = true;
            row.ShowcaseBlog = null;
            return row;
        },
        ["screenshots-off"] = () =>
        {
            var row = Edited();
            row.ShowShots = false;
            row.ShowcaseBlog = null;
            return row;
        },
        ["features-off"] = () => new LandingSettings { ShowFeatures = false },
        ["pricing-off"] = () => new LandingSettings { ShowPricing = false },
        ["all-off"] = () => new LandingSettings { ShowShots = false, ShowFeatures = false, ShowPricing = false },
        ["on-but-unwritten"] = () => new LandingSettings { ShowRoadmap = true, ShowStory = true, ShowDownload = true },
        ["broken-json"] = () => new LandingSettings
        {
            ShotsJson = "{", RoadmapJson = "nope", StoryJson = "[", EditorialJson = "[]", ShowRoadmap = true, ShowStory = true,
        },
        ["untidy"] = () => new LandingSettings
        {
            ShowRoadmap = true, ShowStory = true,
            RoadmapJson = LandingContent.Serialize(new[]
            {
                new LandingRoadmapColumn(T(null, null), "", [T("Only English", null), T(null, null)]),
            }),
            StoryJson = LandingContent.Serialize(new[] { new LandingStoryStep(T("2025", null), T(null, null), T(null, "Только текст")) }),
            ShotsJson = LandingContent.Serialize(new[] { new LandingShot("only.png", T(null, null)) }),
        },
    };

    private static LandingSettings Edited() => new()
    {
        KickerEn = "  for makers & studios  ",
        HeroTitleEn = "Edited <br> headline & more",
        HeroSubRu = "Отредактированный подзаголовок «в кавычках»",
        ProofEn = "78 makers already publish",
        NoteRu = "Заметка только по-русски",
        ShowcaseBlog = "showcase.cedar.test",
        ShowRoadmap = true,
        ShowStory = true,
        ShotsJson = LandingContent.Serialize(new[]
        {
            new LandingShot("shot_hero.png", T("The editor", "Редактор")),
            new LandingShot("shot_two.webp", T("Only an English caption", null)),
            new LandingShot("shot_three.jpg", T(null, null)),
        }),
        RoadmapJson = LandingContent.Serialize(new[]
        {
            new LandingRoadmapColumn(T("Shipped", "Готово"), "done", [T("Blog & RSS", "Блог и RSS"), T("Telegram", null)]),
            new LandingRoadmapColumn(T("Later", null), "someday", [T(null, "Позже <b>всё</b>")]),
        }),
        StoryJson = LandingContent.Serialize(new[]
        {
            new LandingStoryStep(T("2025", "2025"), T("It began", "Начало"), T("A bot and a text file.", "Бот и текстовый файл.")),
            new LandingStoryStep(T("2026", null), T("It grew", null), T("\"Quoted\" & 'apostrophes'", null)),
        }),
        EditorialJson = LandingContent.SerializeEditorial(new()
        {
            ["workflowTitle"] = T("From \"draft\" to readers", null),
            ["writeTitle"] = T("Write <now>", "Пишите"),
            ["examplesLink"] = T("See a live blog", null),
            ["pricingBody"] = T(null, "Свой текст о тарифах"),
            ["faq2Answer"] = T("Tom & Jerry's answer", null),
            ["closingTitle"] = T("A closing <i>line</i>", "Финал"),
        }),
    };

    private static readonly DiscoveryEndpoints.Settings Settings = new(true, true, true, true,
        new LandingText("Discover & explore", "Открывайте"), new LandingText("What makers publish.", "Что публикуют авторы."));

    private static DiscoveryEndpoints.Item Item(string kind, string title) => new(
        kind, title, $"{title} summary", $"https://maker.test/{Uri.EscapeDataString(title)}",
        "/media/cover.png", "Maker", null, new DateTime(2026, 9, 1), [], DiscoveryCategories.Other,
        kind == "devlog" ? "Mosslight" : null, null, false);

    public static readonly DiscoveryEndpoints.Snapshot EmptyDiscovery = new(Settings, [], [], null, 1, 0);

    public static readonly DiscoveryEndpoints.Snapshot BusyDiscovery = new(Settings,
        [Item("project", "Mosslight")], [Item("blog", "A <notebook>"), Item("devlog", "Build 12")], null, 1, 3);
}
