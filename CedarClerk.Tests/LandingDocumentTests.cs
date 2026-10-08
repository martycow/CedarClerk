using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// ADR-323 — the landing's block document: what the server accepts, what it draws, and who may
// change it.
public class LandingDocumentTests
{
    private const string Attack = "<script>alert(1)</script>\" onmouseover='x' &amp;";

    private static Dictionary<string, string> Map(string en, string? ru = null, string? de = null)
    {
        var map = new Dictionary<string, string> { ["en"] = en, ["ru"] = ru ?? en };
        if (de is not null) map["de"] = de;
        return map;
    }

    private static LandingDocument Legacy() => LandingDocument.FromLegacy(null, null);

    private static string Draw(LandingDocument document, string language = "en", string? showcase = null) =>
        LandingRenderer.Render(language, document, LegacyRows.BusyDiscovery, showcase, null, "https://eu.example");

    /// <summary>Every block type in every style, each text field carrying its own marked payload.</summary>
    private static (LandingDocument Document, List<string> Payloads) EveryBlock(string payload, bool flow)
    {
        var payloads = new List<string>();
        Dictionary<string, string> Text(string at)
        {
            var text = $"{at} {payload}";
            payloads.Add(text);
            return Map(text);
        }

        var document = new LandingDocument
        {
            Features = [new() { Id = "one", Icon = "cube", Title = Text("feature.title"), Body = Text("feature.body"), Shot = "/assets/review/editor.png" }],
        };
        var n = 0;
        foreach (var spec in LandingBlocks.All)
        foreach (var style in spec.Styles)
        {
            var at = $"{spec.Type}.{style.Id}";
            var block = new LandingBlock { Id = $"b{++n}", Type = spec.Type, Style = style.Id };
            foreach (var field in style.Fields) block.Text[field] = Text($"{at}.{field}");
            if (style.Url) block.Url = "https://ok.example/path?a=1&b=2";
            if (style.Image) block.Image = "story.png";
            if (style.ItemFields.Length > 0 || style.Files)
            {
                var item = new LandingItem();
                foreach (var field in style.ItemFields) item.Text[field] = Text($"{at}.item.{field}");
                if (style.Icons) item.Icon = "timer";
                if (style.Files) item.File = "shot.png";
                if (style.Marks) item.Mark = "doing";
                if (style.Entries) item.Entries.Add(Text($"{at}.entry"));
                block.Items.Add(item);
            }
            foreach (var option in spec.Options) block.Options[option] = true;
            document.Sections.Add(new LandingSection
            {
                Id = $"s{n}", Layout = flow ? LandingLayouts.Flow : LandingLayouts.Stack, Anchor = $"a{n}",
                Nav = Text($"{at}.nav"), Label = flow ? [] : Text($"{at}.label"), Blocks = [block],
            });
        }
        return (document, payloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_text_field_of_every_block_type_is_drawn_escaped(bool flow)
    {
        var (document, payloads) = EveryBlock(Attack, flow);
        Assert.Null(document.Validate());
        Assert.Equal(LandingBlocks.All.Select(s => s.Type), document.Sections.Select(s => s.Blocks[0].Type).Distinct());

        foreach (var language in new[] { "en", "ru" })
        {
            var html = Draw(document, language);
            Assert.DoesNotContain("<script>alert(1)", html);
            Assert.DoesNotContain("onmouseover='x'", html);
            Assert.DoesNotContain("\" onmouseover", html);
            foreach (var payload in payloads)
                Assert.Contains(WebUtility.HtmlEncode(payload), html);
        }
    }

    [Fact]
    public void A_headline_keeps_line_breaks_and_nothing_else()
    {
        var document = Legacy();
        document.Sections[0].Blocks[0].Text["title"] = Map("One<br>two<br/>three<br />four<b>bold</b><BR onload=x>");
        document.Sections.Single(s => s.Id == "pricing").Blocks[0].Text["title"] = Map("Plans<br>here");

        var html = Draw(document);

        Assert.Contains("<h1>One<br>two<br>three<br>four&lt;b&gt;bold&lt;/b&gt;&lt;BR onload=x&gt;</h1>", html);
        Assert.Contains("<h2>Plans&lt;br&gt;here</h2>", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/x.png")]
    [InlineData("https://evil.example/x.png")]
    [InlineData("/\\evil.example/x.png")]
    [InlineData("data:image/svg+xml,<svg onload=alert(1)>")]
    [InlineData("x\" onerror=\"alert(1)")]
    [InlineData("../cedar.db")]
    public void An_image_reference_off_this_site_is_refused_and_never_drawn_as_written(string file)
    {
        var document = Legacy();
        document.Sections[0].Blocks[2].Items[0].File = file;
        Assert.NotNull(document.Validate());

        var story = Legacy();
        story.Sections.Single(s => s.Id == "story").Blocks[1].Image = file;
        Assert.NotNull(story.Validate());

        var feature = Legacy();
        feature.Features[0].Shot = file;
        Assert.NotNull(feature.Validate());

        var html = Draw(document);
        Assert.DoesNotContain($"src=\"{file}\"", html);
        Assert.DoesNotContain("onerror=\"alert", html);
        Assert.DoesNotContain("evil.example", html.Replace(Uri.EscapeDataString(file), ""));
    }

    [Theory]
    [InlineData("shot_0123456789abcdef.png")]
    [InlineData("/assets/review/editor.png")]
    [InlineData("/landing-blog.png")]
    public void An_uploaded_file_or_a_path_on_this_site_is_an_image(string file)
    {
        var document = Legacy();
        document.Sections[0].Blocks[2].Items[0].File = file;
        Assert.Null(document.Validate());
        Assert.Contains($"src=\"{LandingContent.ShotUrl(file)}\"", Draw(document));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://plain.example")]
    [InlineData("//evil.example")]
    [InlineData("https://user:pass@evil.example")]
    [InlineData("https://ok.example/\" onclick=\"x")]
    [InlineData("mailto:someone@example.test")]
    public void A_link_that_is_not_https_a_site_path_or_an_anchor_is_refused_and_not_drawn(string url)
    {
        var document = Legacy();
        var copy = document.Sections.Single(s => s.Id == "examples").Blocks[0];
        copy.Url = url;

        Assert.Equal(ErrorMessages.LandingUrlInvalid("sections[3].blocks[1].url"), document.Validate());
        var html = Draw(document);
        Assert.DoesNotContain("&rarr;</a>", html);
        Assert.DoesNotContain("onclick=\"x", html);
    }

    [Theory]
    [InlineData("https://blog.example/path?a=1&b=2", "https://blog.example/path?a=1&amp;b=2")]
    [InlineData("/discovery", "/discovery")]
    [InlineData("#pricing", "#pricing")]
    public void A_safe_link_is_drawn_escaped(string url, string drawn)
    {
        var document = Legacy();
        document.Sections.Single(s => s.Id == "examples").Blocks[0].Url = url;
        Assert.Null(document.Validate());
        Assert.Contains($"<a href=\"{drawn}\">Explore a live blog &rarr;</a>", Draw(document));
    }

    [Theory]
    [InlineData("evil.example\" onclick=\"x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("blog.example/<script>")]
    public void A_showcase_host_that_is_not_a_host_is_refused_and_not_drawn(string host)
    {
        var document = Legacy();
        document.ShowcaseBlog = host;
        Assert.Equal(ErrorMessages.LandingUrlInvalid("showcaseBlog"), document.Validate());
        Assert.DoesNotContain(LandingTexts.LiveBlog(false), Draw(document));
        Assert.DoesNotContain(LandingTexts.LiveBlog(false), Draw(Legacy(), showcase: host));
        Assert.Contains("<a href=\"https://blog.cedar.test\">a live blog</a>", Draw(Legacy(), showcase: "blog.cedar.test"));
    }

    [Fact]
    public void A_hidden_section_and_a_hidden_block_are_not_drawn()
    {
        var document = Legacy();
        Assert.Contains("<section id=\"pricing\">", Draw(document));
        Assert.Contains("<a href=\"#pricing\">Pricing</a>", Draw(document));
        Assert.Contains("class=\"network-strip\"", Draw(document));

        document.Sections.Single(s => s.Id == "pricing").Hidden = true;
        document.Sections.Single(s => s.Id == "features").Blocks[1].Hidden = true;
        var html = Draw(document);

        Assert.DoesNotContain("id=\"pricing\"", html);
        Assert.DoesNotContain("href=\"#pricing\"", html);
        Assert.DoesNotContain("class=\"plans\"", html);
        Assert.DoesNotContain("class=\"network-strip\"", html);
        Assert.Contains("<ol class=\"workflow\">", html);
        Assert.Contains("<a href=\"#features\">How it works</a>", html);
    }

    [Fact]
    public void A_section_whose_blocks_are_all_hidden_leaves_no_wrapper_and_no_nav_link()
    {
        var document = Legacy();
        foreach (var block in document.Sections.Single(s => s.Id == "features").Blocks) block.Hidden = true;
        var html = Draw(document);
        Assert.DoesNotContain("id=\"features\"", html);
        Assert.DoesNotContain("href=\"#features\"", html);
    }

    [Fact]
    public void A_page_without_a_visible_form_still_carries_one_for_the_waitlist_dialog()
    {
        var document = Legacy();
        document.Sections[0].Blocks[1].Hidden = true;
        var html = Draw(document);
        Assert.DoesNotContain("class=\"wait-wrap\"", html);
        Assert.Contains("<div hidden><form class=\"waitlist\" id=\"waitlist-form\">", html);
        Assert.Single(Regex("id=\"waitlist-form\"", html));
        Assert.DoesNotContain("<div hidden>", Draw(Legacy()));
    }

    private static System.Text.RegularExpressions.MatchCollection Regex(string pattern, string html) =>
        System.Text.RegularExpressions.Regex.Matches(html, pattern);

    [Fact]
    public void Sections_are_drawn_in_document_order_and_a_band_stands_outside_the_column()
    {
        var document = Legacy();
        var faq = document.Sections.Single(s => s.Id == "faq");
        document.Sections.Remove(faq);
        document.Sections.Insert(1, faq);
        var closing = document.Sections.Single(s => s.Id == "closing");
        document.Sections.Remove(closing);
        document.Sections.Insert(2, closing);

        var html = Draw(document);

        Assert.True(html.IndexOf("id=\"faq\"", StringComparison.Ordinal) < html.IndexOf("id=\"features\"", StringComparison.Ordinal));
        Assert.Single(Regex("<main class=\"wrap\">", html));
        Assert.Contains("</main>\n<section class=\"band\">", html);
        Assert.Contains("</section>\n<div class=\"wrap\">\n<section id=\"features\"", html);
    }

    [Fact]
    public void A_block_outside_a_flow_section_draws_without_a_section_of_its_own()
    {
        var document = Legacy();
        document.Sections.Single(s => s.Id == "faq").Layout = LandingLayouts.Stack;
        document.Sections.Single(s => s.Id == "tools").Layout = LandingLayouts.Stack;
        var html = Draw(document);
        Assert.Contains("<section id=\"faq\">\n<div class=\"faq\"><h2>", html);
        Assert.Contains("<div class=\"tools\" aria-labelledby=\"tools-title\">", html);
        Assert.DoesNotContain("<section class=\"tools\"", html);
    }

    [Fact]
    public void The_tools_block_reads_the_feature_list_and_attaches_screenshots_by_feature()
    {
        var document = Legacy();
        var glossary = document.Features.Single(f => f.Id == "glossary");
        document.Features.Remove(glossary);
        document.Features.Insert(0, glossary);
        document.Features.RemoveAll(f => f.Id is "builds" or "tasks");
        document.Features.Add(new() { Id = "new", Icon = "cube", Title = Map("A new tool"), Body = Map("It is new."), Shot = "shot_new.png" });
        Assert.Null(document.Validate());

        var html = Draw(document);

        Assert.Contains("Explore all tools · 15</h3>", html);
        var first = html[html.IndexOf("id=\"tool-panel-0\"", StringComparison.Ordinal)..html.IndexOf("id=\"tool-panel-1\"", StringComparison.Ordinal)];
        Assert.Contains("<h3>Glossary</h3>", first);
        Assert.Contains("src=\"/assets/review/glossary.png\"", first);
        var last = html[html.IndexOf("id=\"tool-panel-14\"", StringComparison.Ordinal)..];
        Assert.Contains("<h3>A new tool</h3>", last);
        Assert.Contains("<img src=\"/landing-media/shot_new.png\" alt=\"View workspace\" loading=\"lazy\">", last);
        Assert.Contains("role=\"tablist\"", html);
        Assert.Contains("aria-controls=\"tool-panel-14\"", html);
    }

    [Fact]
    public void Prices_and_counts_come_from_the_code_and_are_not_in_the_document()
    {
        var document = Legacy();
        var json = document.Serialize();
        Assert.DoesNotContain("$", json);
        Assert.DoesNotContain($"{PlanLimitations.MaxChannels(PlanTiers.ProPlus)} channels", json);
        Assert.DoesNotContain($"{Languages.ContentLanguages.Count} languages", json);

        var html = Draw(document);
        Assert.Contains($"<span class=\"n\">${Consts.Plans.ProPrice}</span>", html);
        Assert.Contains($"<span class=\"n\">${Consts.Plans.ProPlusPrice}</span>", html);
        Assert.Contains($"{Languages.ContentLanguages.Count} languages in one entry", html);
        Assert.Contains($"{PublishNetworks.All.Count} networks, one text", html);
        foreach (var network in PublishNetworks.All) Assert.Contains($"<span>{network}</span>", html);

        var pricing = document.Sections.Single(s => s.Id == "pricing").Blocks[1];
        pricing.Options.Clear();
        var bare = Draw(document);
        Assert.Contains("class=\"plans\"", bare);
        Assert.DoesNotContain("class=\"plan-foot\"", bare);
        Assert.DoesNotContain("class=\"comparison-toggle\"", bare);

        pricing.Options["price"] = true;
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[6].blocks[2].options", "price"), document.Validate());
        pricing.Options.Clear();
        pricing.Text["price"] = Map("$1");
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[6].blocks[2].text", "price"), document.Validate());
    }

    // ---------- languages ----------

    [Fact]
    public void Text_falls_back_to_english_then_russian_then_anything_written()
    {
        Assert.Equal("Hallo", LandingDocument.Pick(Map("Hello", "Привет", "Hallo"), "de"));
        Assert.Equal("Hello", LandingDocument.Pick(Map("Hello", "Привет"), "de"));
        Assert.Equal("Привет", LandingDocument.Pick(new() { ["ru"] = "Привет", ["de"] = " " }, "de"));
        Assert.Equal("Привет", LandingDocument.Pick(new() { ["ru"] = "Привет" }, "en"));
        Assert.Equal("Hello", LandingDocument.Pick(new() { ["en"] = "Hello" }, "ru"));
        Assert.Equal("Bonjour", LandingDocument.Pick(new() { ["fr"] = " Bonjour " }, "de"));
        Assert.Equal("", LandingDocument.Pick([], "en"));
        Assert.Equal("", LandingDocument.Pick(null, "en"));
    }

    [Theory]
    [InlineData("de", "", "de")]
    [InlineData("ja", "ru-RU", "ru")]
    [InlineData("", "de-AT,ru;q=0.8", "de")]
    [InlineData("", "ru-RU,en;q=0.5", "ru")]
    [InlineData("", "fr-FR,ru;q=0.9", "en")]
    [InlineData("", "", "en")]
    [InlineData("xx", " ,en", "en")]
    public void The_visitor_language_is_chosen_among_the_documents_languages(string query, string accept, string expected) =>
        Assert.Equal(expected, LandingRenderer.ChooseLanguage(query, accept, ["en", "ru", "de"]));

    [Theory]
    [InlineData("de", "en")]
    [InlineData("", "en")]
    public void A_language_the_document_does_not_carry_is_not_served(string query, string expected) =>
        Assert.Equal(expected, LandingRenderer.ChooseLanguage(query, "de-DE,ru;q=0.8", ["en", "ru"]));

    [Fact]
    public void An_added_language_gets_its_text_its_document_language_and_a_switch()
    {
        var document = Legacy();
        document.Languages.Add("de");
        document.Sections[0].Blocks[0].Text["title"]["de"] = "Ein Entwurf.";
        Assert.Null(document.Validate());

        var german = Draw(document, "de");
        Assert.Contains("<html lang=\"de\">", german);
        Assert.Contains("<h1>Ein Entwurf.</h1>", german);
        Assert.Contains(LandingTexts.HeroSub.En, german);
        Assert.Contains("hreflang=\"de\" href=\"" + Consts.URLs.MainHost + "/?lang=de\"", german);
        Assert.Contains("<a href=\"?lang=de\" aria-current=\"true\">DE</a>", german);
        Assert.Contains("language: 'de'", german);

        var english = Draw(document, "en");
        Assert.Contains("<a href=\"?lang=ru\">RU</a><a href=\"?lang=en\" aria-current=\"true\">EN</a><a href=\"?lang=de\">DE</a>", english);
        Assert.DoesNotContain("hreflang=\"de\"", Draw(Legacy()));
    }

    // ---------- validation ----------

    private static string? Problem(Action<LandingDocument> change)
    {
        var document = Legacy();
        change(document);
        return document.Normalize().Validate();
    }

    [Fact]
    public void Russian_and_english_are_required_and_other_languages_must_be_added_first()
    {
        Assert.Equal(ErrorMessages.LandingTextRequired("sections[1].blocks[1].text.title", "RU"),
            Problem(d => d.Sections[0].Blocks[0].Text["title"].Remove("ru")));
        Assert.Equal(ErrorMessages.LandingTextRequired("sections[1].blocks[1].text.title", "EN"),
            Problem(d => d.Sections[0].Blocks[0].Text["title"]["en"] = "  "));
        Assert.Equal(ErrorMessages.LandingTextRequired("sections[1].blocks[1].text.title", "EN"),
            Problem(d => d.Sections[0].Blocks[0].Text.Remove("title")));
        Assert.Equal(ErrorMessages.LandingTextRequired("sections[1].blocks[2].text.proof", "RU"),
            Problem(d => d.Sections[0].Blocks[1].Text["proof"] = new() { ["en"] = "Only English" }));
        Assert.Null(Problem(d => d.Sections[0].Blocks[1].Text.Remove("hint")));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[1].blocks[1].text.title", "de"),
            Problem(d => d.Sections[0].Blocks[0].Text["title"]["de"] = "Nicht hinzugefügt"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("languages", "xx"), Problem(d => d.Languages.Add("xx")));
        Assert.Null(Problem(d => d.Languages.Add("ja")));
        Assert.Equal(ErrorMessages.LandingTextRequired("features[1].title", "RU"), Problem(d => d.Features[0].Title.Remove("ru")));
        Assert.Equal(ErrorMessages.LandingTextRequired("sections[7].blocks[1].items[2].text.answer", "EN"),
            Problem(d => d.Sections[6].Blocks[0].Items[1].Text.Remove("answer")));

        var withoutRequired = Legacy();
        withoutRequired.Languages = ["de"];
        Assert.Equal(["de", "en", "ru"], withoutRequired.Normalize().Languages);
    }

    [Fact]
    public void Unknown_types_layouts_styles_and_fields_are_refused()
    {
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[1].blocks[1].type", "html"), Problem(d => d.Sections[0].Blocks[0].Type = "html"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[1].layout", "carousel"), Problem(d => d.Sections[0].Layout = "carousel"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[1].blocks[1].style", "marquee"), Problem(d => d.Sections[0].Blocks[0].Style = "marquee"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[1].blocks[1].text", "html"),
            Problem(d => d.Sections[0].Blocks[0].Text["html"] = Map("<b>raw</b>")));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[2].blocks[1].items[1].icon", "no-such-icon"),
            Problem(d => d.Sections[1].Blocks[0].Items[0].Icon = "no-such-icon"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[5].blocks[1].items[1].icon", "no-such-icon"),
            Problem(d => d.Sections[4].Blocks[0].Items[0].Icon = "no-such-icon"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("features[2].icon", ""), Problem(d => d.Features[1].Icon = ""));
        Assert.Equal(ErrorMessages.LandingUrlInvalid("sections[1].blocks[1].url"), Problem(d => d.Sections[0].Blocks[0].Url = "/welcome"));
        Assert.Equal(ErrorMessages.LandingValueUnknown("sections[4].blocks[1]", "items"),
            Problem(d => d.Sections[3].Blocks[0].Items.Add(new())));
    }

    [Fact]
    public void Ids_and_anchors_are_well_formed_unique_and_clear_of_the_pages_own_ids()
    {
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[2]"), Problem(d => d.Sections[1].Id = d.Sections[0].Id));
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[1]"), Problem(d => d.Sections[0].Id = "Has Spaces"));
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[2].blocks[1]"), Problem(d => d.Sections[1].Blocks[0].Id = d.Sections[0].Blocks[0].Id));
        Assert.Equal(ErrorMessages.LandingIdInvalid("features[2]"), Problem(d => d.Features[1].Id = d.Features[0].Id));
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[1].anchor"), Problem(d => d.Sections[0].Anchor = "waitlist"));
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[1].anchor"), Problem(d => d.Sections[0].Anchor = "tool-panel-0"));
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[1].anchor"), Problem(d => d.Sections[0].Anchor = "x\" onclick=\"y"));
        Assert.Equal(ErrorMessages.LandingIdInvalid("sections[6].anchor"), Problem(d => d.Sections[0].Anchor = "pricing"));
    }

    [Fact]
    public void Size_limits_hold()
    {
        Assert.Equal(ErrorMessages.LandingTextTooLong("sections[1].blocks[1].text.body", LandingDocument.MaxTextLength),
            Problem(d => d.Sections[0].Blocks[0].Text["body"]["en"] = new string('a', LandingDocument.MaxTextLength + 1)));
        Assert.Equal(ErrorMessages.LandingTooMany("sections", LandingDocument.MaxSections), Problem(d =>
        {
            for (var i = 0; i < LandingDocument.MaxSections; i++) d.Sections.Add(new() { Id = $"extra-{i}" });
        }));
        Assert.Equal(ErrorMessages.LandingTooMany("sections[1].blocks", LandingDocument.MaxBlocks), Problem(d =>
        {
            for (var i = 0; i < LandingDocument.MaxBlocks; i++)
                d.Sections[0].Blocks.Add(new() { Id = $"extra-{i}", Type = LandingBlocks.Discovery });
        }));
        Assert.Equal(ErrorMessages.LandingTooMany("sections[7].blocks[1].items", LandingDocument.MaxItems), Problem(d =>
        {
            for (var i = 0; i < LandingDocument.MaxItems; i++) d.Sections[6].Blocks[0].Items.Add(new());
        }));
        Assert.Equal(ErrorMessages.LandingTooMany("features", LandingDocument.MaxFeatures), Problem(d =>
        {
            for (var i = 0; i < LandingDocument.MaxFeatures; i++) d.Features.Add(new() { Id = $"extra-{i}" });
        }));
        Assert.Equal(ErrorMessages.LandingDocumentTooLarge(LandingDocument.MaxBytes / 1024), Problem(d =>
        {
            d.Languages.AddRange(["de", "fr", "es", "ja", "uk", "be", "ka"]);
            foreach (var feature in d.Features)
            foreach (var language in d.Languages)
                feature.Body[language] = new string('x', LandingDocument.MaxTextLength);
        }));
    }

    [Fact]
    public void One_form_and_one_tools_block_at_most()
    {
        Assert.Equal(ErrorMessages.LandingTooMany("sections[11].blocks[2].style", 1),
            Problem(d => d.Sections[10].Blocks[1].Style = "form"));
        Assert.Equal(ErrorMessages.LandingTooMany("sections[11].blocks[3].type", 1), Problem(d =>
            d.Sections[10].Blocks.Add(new() { Id = "second-tools", Type = LandingBlocks.Tools, Text = { ["title"] = Map("Tools") } })));
    }

    [Fact]
    public void A_null_riddled_document_normalises_instead_of_throwing()
    {
        var document = JsonSerializer.Deserialize<LandingDocument>(
            """{"languages":null,"features":[{"id":null,"title":null}],"sections":[{"id":"a","blocks":[{"id":"b","type":"faq","text":null,"items":[{"text":null,"entries":null}],"options":null}]},{"blocks":null}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.NotNull(document.Normalize().Validate());
        Assert.Contains("<!doctype html>", Draw(document));
    }

    // ---------- the admin endpoints ----------

    private static CedarDbContext Database() => BlogTestHost.EmptyDatabase();

    private static ApplicationUser Admin(CedarDbContext db, bool admin = true)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"), UserName = $"{Guid.NewGuid():N}@x.test", Email = "who@x.test", IsAdmin = admin,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;

    [Fact]
    public async Task Saving_stores_the_document_and_leaves_the_legacy_columns_alone()
    {
        using var db = Database();
        var legacy = LegacyRows.All["edited"]()!;
        db.LandingSettings.Add(legacy);
        db.SaveChanges();
        var before = (legacy.KickerEn, legacy.HeroTitleEn, legacy.ShotsJson, legacy.RoadmapJson, legacy.StoryJson, legacy.EditorialJson, legacy.ShowRoadmap, legacy.ShowcaseBlog);

        var document = LandingDocument.FromRow(legacy, null);
        document.Sections[0].Blocks[0].Text["title"] = Map("  A saved headline  ", "Сохранённый заголовок");
        var result = await AdminEndpoints.SaveLandingAsync(document, Admin(db), db);

        Assert.Equal(200, Status(result));
        db.ChangeTracker.Clear();
        var row = await db.LandingSettings.SingleAsync();
        Assert.Equal(before, (row.KickerEn, row.HeroTitleEn, row.ShotsJson, row.RoadmapJson, row.StoryJson, row.EditorialJson, row.ShowRoadmap, row.ShowcaseBlog));
        Assert.True(LandingDocument.IsStored(row));
        Assert.Equal("A saved headline", LandingDocument.FromRow(row, null).Sections[0].Blocks[0].Text["title"]["en"]);
        Assert.Contains("<h1>A saved headline</h1>", Draw(await LandingDocument.LoadAsync(db, new ConfigurationBuilder().Build())));
        Assert.Equal("landing", (await db.AdminAuditEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task An_invalid_document_is_refused_and_nothing_is_stored()
    {
        using var db = Database();
        var actor = Admin(db);
        var document = Legacy();
        document.Sections[0].Blocks[0].Type = "html";

        Assert.Equal(400, Status(await AdminEndpoints.SaveLandingAsync(document, actor, db)));
        Assert.Equal(400, Status(await AdminEndpoints.SaveLandingAsync(null, actor, db)));
        Assert.Empty(db.LandingSettings);
        Assert.Empty(db.AdminAuditEntries);
    }

    [Fact]
    public async Task The_preview_is_the_real_renderer_on_the_unsaved_document_and_is_never_cached()
    {
        using var db = Database();
        var document = Legacy();
        document.Languages.Add("de");
        document.Sections[0].Blocks[0].Text["title"] = Map("Unsaved <b>headline</b>", "Несохранённый", "Ungespeichert");
        var http = new DefaultHttpContext();

        var result = await AdminEndpoints.PreviewLandingAsync(new(document, "de"), http.Response, db, new ConfigurationBuilder().Build());

        Assert.Equal("private, no-store", http.Response.Headers.CacheControl.ToString());
        var payload = JsonSerializer.SerializeToNode(((IValueHttpResult)result).Value)!;
        var html = payload["Html"]!.GetValue<string>();
        Assert.Equal("de", payload["Language"]!.GetValue<string>());
        Assert.Null(payload["Problem"]);
        Assert.Contains("<h1>Ungespeichert</h1>", html);
        Assert.Contains("<base target=\"_blank\">", html);
        Assert.Empty(db.LandingSettings);

        document.Sections[0].Blocks[0].Text["title"].Remove("ru");
        var invalid = await AdminEndpoints.PreviewLandingAsync(new(document, "en"), http.Response, db, new ConfigurationBuilder().Build());
        var problem = JsonSerializer.SerializeToNode(((IValueHttpResult)invalid).Value)!;
        Assert.Equal(ErrorMessages.LandingTextRequired("sections[1].blocks[1].text.title", "RU"), problem["Problem"]!.GetValue<string>());
        Assert.Contains("Unsaved &lt;b&gt;headline&lt;/b&gt;", problem["Html"]!.GetValue<string>());

        Assert.Equal(400, Status(await AdminEndpoints.PreviewLandingAsync(new(null, "en"), http.Response, db, new ConfigurationBuilder().Build())));
    }

    [Fact]
    public async Task Every_landing_route_refuses_an_account_that_is_not_an_admin()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var directory = Directory.CreateTempSubdirectory("cedar-landing-test");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Tests" });
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddScoped(_ => TenantProvider.Platform());
            builder.Services.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
            builder.Services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<CedarDbContext>();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(new LandingPaths(directory.FullName));
            // The group is built as a whole, so its other routes' services have to resolve too.
            builder.Services.AddSingleton(new MediaPaths(directory.FullName));
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<TenantOwnerCache.ForHosts>();
            await using var app = builder.Build();
            app.MapAdminEndpoints();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
            db.Database.EnsureCreated();
            var admin = Admin(db);
            var member = Admin(db, admin: false);

            var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Where(e => e.RoutePattern.RawText!.StartsWith("/api/admin/landing", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(
                ["DELETE /api/admin/landing/files/{file}", "GET /api/admin/landing", "GET /api/admin/landing/waitlist",
                    "POST /api/admin/landing/preview", "POST /api/admin/landing/upload", "PUT /api/admin/landing"],
                routes.Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0]} {e.RoutePattern.RawText}").Order());

            async Task<int> Call(RouteEndpoint route, ApplicationUser? user)
            {
                var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
                http.Request.Method = route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0];
                http.Request.ContentType = "application/json";
                http.Request.Body = new MemoryStream("{}"u8.ToArray());
                http.Response.Body = new MemoryStream();
                http.Request.RouteValues["file"] = "nothing.png";
                if (route.RoutePattern.RawText!.EndsWith("/upload", StringComparison.Ordinal))
                {
                    http.Request.ContentType = "multipart/form-data; boundary=x";
                    http.Request.Form = new FormCollection([], new FormFileCollection
                    {
                        new FormFile(new MemoryStream([1]), 0, 1, "file", "shot.gif") { Headers = new HeaderDictionary(), ContentType = "image/gif" },
                    });
                }
                if (user is not null)
                    http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
                await route.RequestDelegate!(http);
                return http.Response.StatusCode;
            }

            foreach (var route in routes)
            {
                Assert.NotNull(route.Metadata.GetMetadata<IAuthorizeData>());
                Assert.Equal(404, await Call(route, member));
                Assert.Equal(404, await Call(route, null));
            }
            Assert.Equal(200, await Call(routes.Single(e => e.RoutePattern.RawText == "/api/admin/landing/waitlist"), admin));
            Assert.Equal(400, await Call(routes.Single(e => e.RoutePattern.RawText == "/api/admin/landing"
                && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0] == "PUT"), admin));
        }
        finally
        {
            directory.Delete(recursive: true);
            connection.Dispose();
        }
    }
}
