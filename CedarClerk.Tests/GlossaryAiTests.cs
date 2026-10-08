using System.Text.Json;
using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Tenancy;
using CedarClerk.Server.Translation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// ADR-320 clauses 4 and 5 — the glossary's two AI actions and the sweep. No provider is ever
// called: FakeProvider records what it was asked and answers from the test. What matters here is
// the money: what each action charges, and that a refusal or a failure costs nothing.
public class GlossaryAiTests : IDisposable
{
    private const string Owner = "owner-pro";
    private const string Free = "owner-free";

    private readonly Microsoft.Data.Sqlite.SqliteConnection connection;
    private readonly CedarDbContext db;
    private readonly ProductAnalytics analytics =
        new(null, new ConfigurationBuilder().Build(), NullLogger<ProductAnalytics>.Instance);
    private readonly string mediaDir = Path.Combine(Path.GetTempPath(), "cedar-glossary-ai-" + Guid.NewGuid().ToString("N"));
    private readonly FakeProvider provider = new();

    public GlossaryAiTests()
    {
        connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "p@x.test", TenantUsername = "p", PlanTier = PlanTiers.Forever });
        db.Users.Add(new ApplicationUser { Id = Free, UserName = "f@x.test", TenantUsername = "f" });
        db.SaveChanges();
        Directory.CreateDirectory(mediaDir);
        CreditWallet.GrantAsync(db, Owner, 10, CreditReasons.Purchase, "seed").GetAwaiter().GetResult();
        CreditWallet.GrantAsync(db, Free, 10, CreditReasons.Purchase, "seed-free").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
        Directory.Delete(mediaDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class FakeProvider : ITranslationProvider, IGlossaryAiProvider
    {
        public string Name => "fake";
        public List<(IReadOnlyList<GlossaryTermSource> Terms, string Target)> Translations { get; } = [];
        public List<(string Term, string Language, AiImage? Image)> Descriptions { get; } = [];
        public Func<GlossaryTermSource, GlossaryTermTranslation> Translate { get; set; } =
            term => new($"{term.Name}-t", [$"{term.Name}-ta", $"{term.Name}-tb"], term.Description.Length > 0 ? $"{term.Description}-t" : "");
        public string Description { get; set; } = "A written description";
        public bool Fails { get; set; }
        public string? Unsupported { get; set; }

        public Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool SupportsTargetLanguage(string code) => code != Unsupported;

        public Task<IReadOnlyList<GlossaryTermTranslation>> TranslateTermsAsync(
            IReadOnlyList<GlossaryTermSource> terms, string targetLanguage, CancellationToken ct)
        {
            Translations.Add((terms, targetLanguage));
            if (Fails) throw new TranslationException("provider down");
            return Task.FromResult<IReadOnlyList<GlossaryTermTranslation>>(terms.Select(Translate).ToList());
        }

        public Task<string> DescribeTermAsync(string term, string language, AiImage? image, CancellationToken ct)
        {
            Descriptions.Add((term, language, image));
            if (Fails) throw new TranslationException("provider down");
            return Task.FromResult(Description);
        }
    }

    private sealed class PlainProvider : ITranslationProvider
    {
        public string Name => "plain";
        public Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;

    private static JsonNode Body(IResult result) =>
        JsonSerializer.SerializeToNode(((IValueHttpResult)result).Value)!;

    private Task<int> BalanceAsync(string owner = Owner) => CreditWallet.BalanceAsync(db, owner);

    private Task<IResult> TranslateAsync(GlossaryAi.TranslateRequest request, string owner = Owner, ITranslationProvider? with = null) =>
        GlossaryAi.TranslateAsync(db, owner, with ?? provider, analytics, request, CancellationToken.None);

    private Task<IResult> DescribeAsync(GlossaryAi.DescribeRequest request, string owner = Owner, ITranslationProvider? with = null) =>
        GlossaryAi.DescribeAsync(db, owner, with ?? provider, analytics, new MediaPaths(mediaDir), request, CancellationToken.None);

    private Task<IResult> TranslateAllAsync(string source, string target, string owner = Owner) =>
        GlossaryAi.TranslateAllAsync(db, owner, provider, analytics, new(source, target), CancellationToken.None);

    private string SeedImage(string owner = Owner, string contentType = "image/png", int bytes = 64, string? telegramCopy = null)
    {
        var name = $"asset_{Guid.NewGuid()}.png";
        File.WriteAllBytes(Path.Combine(mediaDir, name), new byte[bytes]);
        if (telegramCopy is not null) File.WriteAllBytes(Path.Combine(mediaDir, telegramCopy), [1, 2, 3]);
        db.Assets.Add(new Asset { OwnerId = owner, FileName = "picture.png", ContentType = contentType, LocalPath = name,
            SizeBytes = bytes, TelegramLocalPath = telegramCopy });
        db.SaveChanges();
        return "/media/" + name;
    }

    private async Task<GlossaryEntry> SeedEntryAsync(string name, params (string Language, string Name)[] languages)
    {
        var result = await GlossaryEntries.CreateAsync(db, Owner, new(name, $"about {name}", null, false, null,
            languages.Select(l => new GlossaryEntries.LanguageInput(l.Language, l.Name, [], null)).ToList()));
        var id = ((GlossaryEntries.EntryDto)((IValueHttpResult)result).Value!).Id;
        db.ChangeTracker.Clear();
        return await db.GlossaryEntries.Include(e => e.Languages).SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task Translate_proposes_a_name_spellings_and_description_for_one_small_call()
    {
        var result = await TranslateAsync(new(" renderer ", "Draws frames", "en", "ru"));

        Assert.Equal(200, Status(result));
        var body = Body(result);
        Assert.Equal("ru", body["language"]!.GetValue<string>());
        Assert.Equal("renderer-t", body["localizedName"]!.GetValue<string>());
        Assert.Equal(["renderer-ta", "renderer-tb"], body["spellings"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal("Draws frames-t", body["localizedDescription"]!.GetValue<string>());
        var call = Assert.Single(provider.Translations);
        Assert.Equal(("ru", new GlossaryTermSource("renderer", "Draws frames")), (call.Target, Assert.Single(call.Terms)));
        Assert.Equal(10 - CreditPacks.AiSmallCost, await BalanceAsync());
        Assert.Empty(db.GlossaryEntries);
    }

    [Fact]
    public async Task Translate_drops_spellings_that_repeat_the_name_and_fits_the_stored_limit()
    {
        provider.Translate = _ => new("Рендерер",
            ["рендерер", " рендерера ", "", "РЕНДЕРЕРА", new string('я', 390), new string('ю', 390)], "");

        var body = Body(await TranslateAsync(new("renderer", "", "en", "ru")));

        var spellings = body["spellings"]!.AsArray().Select(s => s!.GetValue<string>()).ToList();
        Assert.Equal(["рендерера", new string('я', 390)], spellings);
        Assert.Equal("", body["localizedDescription"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("", "ru", 400)]
    [InlineData("Unity", "xx", 400)]
    [InlineData("Unity", "en", 400)]
    public async Task Translate_refuses_a_bad_request_before_anything_is_charged(string name, string target, int status)
    {
        var result = await TranslateAsync(new(name, "d", "en", target));

        Assert.Equal(status, Status(result));
        Assert.Empty(provider.Translations);
        Assert.Equal(10, await BalanceAsync());
    }

    [Fact]
    public async Task Translate_refuses_a_plan_without_ai_and_a_provider_that_cannot_do_it()
    {
        Assert.Equal(403, Status(await TranslateAsync(new("Unity", "d", "en", "ru"), Free)));
        Assert.Equal(501, Status(await TranslateAsync(new("Unity", "d", "en", "ru"), with: new PlainProvider())));
        provider.Unsupported = "ka";
        Assert.Equal(501, Status(await TranslateAsync(new("Unity", "d", "en", "ka"))));

        Assert.Empty(provider.Translations);
        Assert.Equal(10, await BalanceAsync());
        Assert.Equal(10, await BalanceAsync(Free));
    }

    [Fact]
    public async Task Translate_refuses_an_empty_wallet_without_calling_the_provider()
    {
        await CreditWallet.TryChargeAsync(db, Owner, 10, CreditReasons.XPost, "drain");

        var result = await TranslateAsync(new("Unity", "d", "en", "ru"));

        Assert.Equal(402, Status(result));
        Assert.Empty(provider.Translations);
    }

    [Fact]
    public async Task Translate_refuses_past_the_daily_ceiling()
    {
        db.AiUsages.Add(new AiUsage { OwnerId = Owner, Day = DateTime.UtcNow.Date, Count = PlanLimitations.AiDailyLimit });
        await db.SaveChangesAsync();

        Assert.Equal(429, Status(await TranslateAsync(new("Unity", "d", "en", "ru"))));
        Assert.Empty(provider.Translations);
        Assert.Equal(10, await BalanceAsync());
    }

    [Fact]
    public async Task Translate_hands_the_credit_back_when_the_provider_fails_or_answers_nothing_usable()
    {
        provider.Fails = true;
        Assert.Equal(502, Status(await TranslateAsync(new("Unity", "d", "en", "ru"))));
        Assert.Equal(10, await BalanceAsync());

        provider.Fails = false;
        provider.Translate = _ => new("  ", ["x"], "d");
        Assert.Equal(502, Status(await TranslateAsync(new("Unity", "d", "en", "ru"))));
        provider.Translate = _ => new(new string('x', GlossaryEntries.NameMaxLength + 1), [], "d");
        Assert.Equal(502, Status(await TranslateAsync(new("Unity", "d", "en", "ru"))));
        Assert.Equal(10, await BalanceAsync());
    }

    [Fact]
    public async Task Describe_from_the_term_alone_is_a_small_call_and_sends_no_image()
    {
        var result = await DescribeAsync(new(" Unity ", "en", null));

        Assert.Equal(200, Status(result));
        Assert.Equal("A written description", Body(result)["description"]!.GetValue<string>());
        Assert.Equal(CreditPacks.AiSmallCost, Body(result)["credits"]!.GetValue<int>());
        var call = Assert.Single(provider.Descriptions);
        Assert.Equal(("Unity", "en"), (call.Term, call.Language));
        Assert.Null(call.Image);
        Assert.Equal(10 - CreditPacks.AiSmallCost, await BalanceAsync());
    }

    [Fact]
    public async Task Describe_with_the_entrys_image_sends_it_and_charges_the_image_price()
    {
        var image = SeedImage(bytes: 64);

        var result = await DescribeAsync(new("Unity", "ru", image));

        Assert.Equal(200, Status(result));
        Assert.Equal(CreditPacks.AiImageDescribeCost, Body(result)["credits"]!.GetValue<int>());
        var call = Assert.Single(provider.Descriptions);
        Assert.Equal("image/png", call.Image!.MediaType);
        Assert.Equal(64, call.Image.Bytes.Length);
        Assert.Equal(10 - CreditPacks.AiImageDescribeCost, await BalanceAsync());
    }

    [Fact]
    public async Task Describe_works_from_an_image_alone_and_prefers_the_small_copy_of_a_large_one()
    {
        var image = SeedImage(contentType: "image/jpeg", bytes: 128, telegramCopy: $"asset_{Guid.NewGuid()}_tg.jpg");

        var result = await DescribeAsync(new("", "en", image));

        Assert.Equal(200, Status(result));
        var call = Assert.Single(provider.Descriptions);
        Assert.Equal("", call.Term);
        Assert.Equal(("image/jpeg", 3), (call.Image!.MediaType, call.Image.Bytes.Length));
    }

    [Fact]
    public async Task Describe_never_sends_a_file_the_caller_does_not_own_or_ai_cannot_read()
    {
        var theirs = SeedImage(owner: Free);
        var video = SeedImage(contentType: "video/mp4");
        var huge = SeedImage(bytes: (int)GlossaryAi.ImageMaxBytes + 1);

        Assert.Equal(400, Status(await DescribeAsync(new("Unity", "en", theirs))));
        Assert.Equal(400, Status(await DescribeAsync(new("Unity", "en", video))));
        Assert.Equal(400, Status(await DescribeAsync(new("Unity", "en", huge))));
        Assert.Equal(400, Status(await DescribeAsync(new("Unity", "en", "/media/asset_missing.png"))));

        Assert.Empty(provider.Descriptions);
        Assert.Equal(10, await BalanceAsync());
    }

    [Fact]
    public async Task Describe_treats_a_url_that_is_not_this_servers_media_as_no_image()
    {
        Assert.Equal(400, Status(await DescribeAsync(new("", "en", "https://evil.test/x.png"))));

        var result = await DescribeAsync(new("Unity", "en", "https://evil.test/x.png"));

        Assert.Equal(200, Status(result));
        Assert.Null(Assert.Single(provider.Descriptions).Image);
        Assert.Equal(10 - CreditPacks.AiSmallCost, await BalanceAsync());
    }

    [Fact]
    public async Task Describe_refuses_cleanly_and_charges_nothing()
    {
        Assert.Equal(400, Status(await DescribeAsync(new("", "en", null))));
        Assert.Equal(400, Status(await DescribeAsync(new("Unity", "xx", null))));
        Assert.Equal(403, Status(await DescribeAsync(new("Unity", "en", null), Free)));
        Assert.Equal(501, Status(await DescribeAsync(new("Unity", "en", null), with: new PlainProvider())));
        Assert.Empty(provider.Descriptions);
        Assert.Equal(10, await BalanceAsync());

        await CreditWallet.TryChargeAsync(db, Owner, 9, CreditReasons.XPost, "drain");
        var image = SeedImage();
        Assert.Equal(402, Status(await DescribeAsync(new("Unity", "en", image))));
        Assert.Empty(provider.Descriptions);
        Assert.Equal(1, await BalanceAsync());
    }

    [Fact]
    public async Task Describe_hands_back_exactly_what_it_charged_when_the_call_fails()
    {
        var image = SeedImage();
        provider.Fails = true;
        Assert.Equal(502, Status(await DescribeAsync(new("Unity", "en", image))));
        Assert.Equal(10, await BalanceAsync());

        provider.Fails = false;
        provider.Description = "   ";
        Assert.Equal(502, Status(await DescribeAsync(new("Unity", "en", null))));
        Assert.Equal(10, await BalanceAsync());
    }

    [Fact]
    public async Task The_sweep_adds_the_target_language_to_entries_that_lack_it_for_one_charge()
    {
        var unity = await SeedEntryAsync("Unity", ("en", "Unity"));
        var ferry = await SeedEntryAsync("Ferry", ("en", "ferry"), ("ru", "паром"));
        await SeedEntryAsync("Только русский", ("ru", "слово"));
        db.Drafts.Add(new Draft { OwnerId = Owner, Title = "ru", CedarJson =
            """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Unity-t и Unity-ta"}]}]}""",
            PrimaryLanguage = "ru" });
        await db.SaveChangesAsync();

        var result = await TranslateAllAsync("en", "ru");

        Assert.Equal(200, Status(result));
        Assert.Equal((1, 0), (Body(result)["added"]!.GetValue<int>(), Body(result)["skipped"]!.GetValue<int>()));
        var call = Assert.Single(provider.Translations);
        Assert.Equal(new GlossaryTermSource("Unity", "about Unity"), Assert.Single(call.Terms));
        db.ChangeTracker.Clear();
        var added = await db.GlossaryEntryLanguages.SingleAsync(l => l.EntryId == unity.Id && l.Language == "ru");
        Assert.Equal(("Unity-t", "about Unity-t"), (added.LocalizedName, added.LocalizedDescription));
        Assert.Equal(["Unity-ta", "Unity-tb"], GlossaryEntries.ParseSpellings(added.SpellingsJson));
        Assert.Equal("паром", (await db.GlossaryEntryLanguages.SingleAsync(l => l.EntryId == ferry.Id && l.Language == "ru")).LocalizedName);
        Assert.Equal(2, (await db.GlossaryTermUsages.SingleAsync(u => u.GlossaryTermId == added.Id)).Occurrences);
        Assert.Equal(10 - CreditPacks.AiSmallCost, await BalanceAsync());
    }

    [Fact]
    public async Task The_sweep_costs_nothing_when_every_entry_already_has_the_language()
    {
        await SeedEntryAsync("Ferry", ("en", "ferry"), ("ru", "паром"));

        var result = await TranslateAllAsync("en", "ru");

        Assert.Equal(200, Status(result));
        Assert.Equal(0, Body(result)["added"]!.GetValue<int>());
        Assert.Empty(provider.Translations);
        Assert.Equal(10, await BalanceAsync());
        Assert.Equal(400, Status(await TranslateAllAsync("de", "ru")));
        Assert.Equal(400, Status(await TranslateAllAsync("en", "en")));
    }

    [Fact]
    public async Task The_sweep_skips_an_unusable_translation_and_refunds_a_failed_run()
    {
        await SeedEntryAsync("Unity", ("en", "Unity"));
        await SeedEntryAsync("Ferry", ("en", "ferry"));
        provider.Translate = term => term.Name == "ferry"
            ? new("", [], "d")
            : new("Юнити", [], "движок");

        var partial = await TranslateAllAsync("en", "ru");
        Assert.Equal((1, 1), (Body(partial)["added"]!.GetValue<int>(), Body(partial)["skipped"]!.GetValue<int>()));
        Assert.Equal(9, await BalanceAsync());

        provider.Fails = true;
        Assert.Equal(502, Status(await TranslateAllAsync("en", "ru")));
        Assert.Equal(9, await BalanceAsync());
    }

    [Fact]
    public async Task The_sweep_refuses_a_plan_without_ai()
    {
        db.GlossaryEntryLanguages.Add(new GlossaryEntryLanguage { OwnerId = Free, Language = "en", LocalizedName = "Unity",
            Entry = new GlossaryEntry { OwnerId = Free, Name = "Unity", Description = "d" } });
        await db.SaveChangesAsync();

        Assert.Equal(403, Status(await TranslateAllAsync("en", "de", Free)));
        Assert.Empty(provider.Translations);
        Assert.Equal(10, await BalanceAsync(Free));
    }
}

public class GlossaryAiPromptGeneratorTests
{
    [Fact]
    public void The_translate_prompt_asks_for_word_forms_of_the_translated_name_not_for_renderings()
    {
        var prompt = GlossaryAiPromptGenerator.BuildTranslate([new("renderer", "Draws frames"), new("ferry", "")], "ru");

        Assert.Contains("ISO code \"ru\"", prompt);
        Assert.Contains("grammatical inflections", prompt);
        Assert.Contains("never translations of other words", prompt);
        Assert.Contains("""{"0":{"name":"renderer","description":"Draws frames"},"1":{"name":"ferry","description":""}}""", prompt);
    }

    [Fact]
    public void A_keyed_answer_is_read_by_index_whatever_else_the_model_adds()
    {
        var parsed = GlossaryAiPromptGenerator.ParseTranslate("""
            ```json
            {"1":{"name":" паром ","spellings":["парома"," ",7,"паромом"],"description":"Судно"},
             "0":{"name":"рендерер","description":"Рисует"},
             "9":{"name":"extra"}}
            ```
            """, 2);

        Assert.Equal(new GlossaryTermTranslation("рендерер", [], "Рисует"), parsed[0] with { Spellings = [] });
        Assert.Empty(parsed[0].Spellings);
        Assert.Equal("паром", parsed[1].Name);
        Assert.Equal(["парома", "паромом"], parsed[1].Spellings);
    }

    [Fact]
    public void The_list_of_forms_is_capped()
    {
        var forms = string.Join(",", Enumerable.Range(0, 30).Select(i => $"\"f{i}\""));
        var parsed = GlossaryAiPromptGenerator.ParseTranslate("{\"0\":{\"name\":\"n\",\"spellings\":[" + forms + "]}}", 1);

        Assert.Equal(GlossaryAiPromptGenerator.MaxSpellings, parsed[0].Spellings.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"1":{"name":"only the second"}}""")]
    [InlineData("""{"0":"a bare string"}""")]
    public void A_malformed_or_incomplete_answer_is_a_translation_failure(string output)
    {
        Assert.Throws<TranslationException>(() => GlossaryAiPromptGenerator.ParseTranslate(output, 1));
    }

    [Fact]
    public void The_describe_prompt_names_the_term_and_the_image_only_when_each_is_there()
    {
        var both = GlossaryAiPromptGenerator.BuildDescribe("the \"ferry\"", "en", hasImage: true);
        var termOnly = GlossaryAiPromptGenerator.BuildDescribe("ferry", "ru", hasImage: false);
        var imageOnly = GlossaryAiPromptGenerator.BuildDescribe(" ", "en", hasImage: true);

        Assert.Contains("\"the \\u0022ferry\\u0022\"", both);
        Assert.Contains("attached image illustrates", both);
        Assert.Contains("ISO code \"ru\"", termOnly);
        Assert.DoesNotContain("image", termOnly);
        Assert.Contains("what the attached image shows", imageOnly);
        Assert.DoesNotContain("of the term", imageOnly);
    }

    [Theory]
    [InlineData("  A boat.  ", "A boat.")]
    [InlineData("\"A boat.\"", "A boat.")]
    [InlineData("```\nA boat.\n```", "A boat.")]
    public void A_description_is_returned_bare(string output, string expected)
    {
        Assert.Equal(expected, GlossaryAiPromptGenerator.ParseDescribe(output));
    }
}
