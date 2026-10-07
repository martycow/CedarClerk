using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// ADR-239 clause 10. The database is a platform context on purpose: no owner filter is in the
// model, so a preview that answers only its owner does so because the query said so.
public class PreviewEndpointTests
{
    private const string RuDoc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Русский текст."}]}]}""";
    private const string EnDoc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"English text."}]}]}""";

    private static readonly PublishCapabilities Telegram = new()
    {
        Network = PublishNetworks.Telegram,
        MaxCharacters = Consts.Telegram.MaxPostChars,
        ThreadPartCharacters = Consts.Telegram.ThreadPartChars,
        MaxMediaItems = 10,
    };

    private static (CedarDbContext Db, SqliteConnection Connection) Build()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1" });
        db.Users.Add(new ApplicationUser { Id = "owner-2", UserName = "owner-2" });
        db.SaveChanges();
        return (db, connection);
    }

    private static Draft Seed(CedarDbContext db, string? ctaButtonsJson = null, string? previewToken = null, bool withTranslation = true)
    {
        var draft = new Draft
        {
            OwnerId = "owner-1", Title = "Заголовок", CedarJson = RuDoc, PrimaryLanguage = Languages.Russian,
            CtaButtonsJson = ctaButtonsJson, PreviewToken = previewToken,
        };
        db.Drafts.Add(draft);
        if (withTranslation)
            db.DraftTranslations.Add(new DraftTranslation
            {
                OwnerId = "owner-1", DraftId = draft.Id, Language = Languages.English, Title = "Title", CedarJson = EnDoc,
            });
        db.SaveChanges();
        return draft;
    }

    private static HttpContext Http()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("app.test.local");
        return ctx;
    }

    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode!.Value;

    [Fact]
    public async Task Telegram_preview_answers_the_owner_with_the_primary_language_by_default()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);

            var result = await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, null, Telegram);

            var preview = Assert.IsType<Ok<TelegramPreview>>(result).Value!;
            Assert.Equal(Languages.Russian, preview.Language);
            Assert.Equal(1, preview.MessageCount);
            Assert.Equal("Русский текст.", Assert.Single(preview.Messages[0].Blocks).Text);
        }
    }

    // ADR-313 / #3 — the send path sends one message unless the author chose a thread.
    [Fact]
    public async Task Telegram_preview_is_one_message_unless_the_thread_flag_is_on()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var paragraph = $$"""{"type":"paragraph","content":[{"type":"text","text":"{{new string('x', 2000)}}"}]}""";
            var draft = Seed(db);
            draft.CedarJson = $$"""{"type":"doc","content":[{{paragraph}},{{paragraph}}]}""";
            db.SaveChanges();

            var single = Assert.IsType<Ok<TelegramPreview>>(
                await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, null, Telegram)).Value!;
            var threaded = Assert.IsType<Ok<TelegramPreview>>(
                await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, null, Telegram, thread: true)).Value!;

            Assert.Equal(1, single.MessageCount);
            Assert.Equal(Consts.Telegram.MaxPostChars, single.MaxCharactersPerMessage);
            Assert.Equal(2, threaded.MessageCount);
            Assert.Equal(Consts.Telegram.ThreadPartChars, threaded.MaxCharactersPerMessage);
        }
    }

    [Fact]
    public async Task Telegram_preview_resolves_a_translation_when_asked()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);

            var result = await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, Languages.English, Telegram);

            var preview = Assert.IsType<Ok<TelegramPreview>>(result).Value!;
            Assert.Equal(Languages.English, preview.Language);
            Assert.Equal("English text.", Assert.Single(preview.Messages[0].Blocks).Text);
        }
    }

    [Fact]
    public async Task Telegram_preview_is_404_for_another_owners_draft()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);

            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await PreviewEndpoints.TelegramAsync(db, "owner-2", draft.Id, null, Telegram)));
        }
    }

    [Fact]
    public async Task Telegram_preview_is_404_for_a_language_the_draft_does_not_have()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db, withTranslation: false);

            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, Languages.English, Telegram)));
        }
    }

    [Fact]
    public async Task Telegram_preview_carries_the_drafts_cta_buttons()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db, ctaButtonsJson: """[{"text":"Play","url":"https://example.com/play"},{"text":"","url":"https://x"}]""");

            var preview = Assert.IsType<Ok<TelegramPreview>>(
                await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, null, Telegram)).Value!;

            var button = Assert.Single(preview.Buttons);
            Assert.Equal("Play", button.Text);
            Assert.Equal("https://example.com/play", button.Url);
        }
    }

    [Fact]
    public async Task Telegram_preview_writes_nothing_and_leaves_assets_alone()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);
            db.Assets.Add(new Asset { OwnerId = "owner-1", LocalPath = "a.jpg", FileName = "a.jpg", ContentType = "image/jpeg" });
            db.SaveChanges();

            await PreviewEndpoints.TelegramAsync(db, "owner-1", draft.Id, null, Telegram);

            Assert.Empty(db.PublishJobs);
            Assert.Empty(db.ScheduledPosts);
            Assert.Empty(db.DraftRevisions.Where(r => r.Kind != DraftRevisionService.Kinds.Save));
            Assert.Null(db.Assets.Single().TelegramFileId);
        }
    }

    [Fact]
    public async Task Blog_preview_serves_the_language_asked_for_inside_a_frameable_page()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);
            var ctx = Http();

            var result = await PreviewEndpoints.BlogAsync(db, "owner-1", draft.Id, Languages.English, "dark", ctx.Response);

            var content = Assert.IsType<ContentHttpResult>(result);
            Assert.Equal("text/html; charset=utf-8", content.ContentType);
            Assert.Contains("English text.", content.ResponseContent);
            Assert.DoesNotContain("Русский текст.", content.ResponseContent);
            Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", content.ResponseContent);
            Assert.Contains("noindex, nofollow", content.ResponseContent);
            Assert.Equal("SAMEORIGIN", ctx.Response.Headers["X-Frame-Options"]);
            Assert.Equal("frame-ancestors 'self'", ctx.Response.Headers["Content-Security-Policy"]);
            Assert.Equal("noindex, nofollow", ctx.Response.Headers["X-Robots-Tag"]);
        }
    }

    [Fact]
    public async Task Blog_preview_without_a_theme_leaves_the_reader_in_charge()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);

            var result = await PreviewEndpoints.BlogAsync(db, "owner-1", draft.Id, null, null, Http().Response);

            var html = Assert.IsType<ContentHttpResult>(result).ResponseContent!;
            Assert.Contains("<html lang=\"ru\">", html);
            Assert.Contains("Русский текст.", html);
        }
    }

    [Fact]
    public async Task Blog_preview_is_404_for_another_owner_and_for_a_missing_language()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db, withTranslation: false);

            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await PreviewEndpoints.BlogAsync(db, "owner-2", draft.Id, null, null, Http().Response)));
            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await PreviewEndpoints.BlogAsync(db, "owner-1", draft.Id, Languages.English, null, Http().Response)));
        }
    }

    [Fact]
    public async Task Reading_the_preview_link_returns_it_without_rotating()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db, previewToken: "tok-1");

            var result = await DraftPreviewEndpoints.ReadLinkAsync(db, "owner-1", draft.Id, Http());

            var link = Assert.IsType<Ok<DraftPreviewEndpoints.PreviewLinkResponse>>(result).Value!;
            Assert.Equal("https://app.test.local/preview/tok-1", link.Url);
            Assert.Equal("tok-1", db.Drafts.Single().PreviewToken);
        }
    }

    [Fact]
    public async Task Reading_a_missing_preview_link_is_404_and_so_is_another_owners()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var unlinked = Seed(db);
            var linked = Seed(db, previewToken: "tok-2");

            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await DraftPreviewEndpoints.ReadLinkAsync(db, "owner-1", unlinked.Id, Http())));
            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await DraftPreviewEndpoints.ReadLinkAsync(db, "owner-2", linked.Id, Http())));
            Assert.Equal("tok-2", db.Drafts.Single(d => d.Id == linked.Id).PreviewToken);
        }
    }

    private static readonly PublishCapabilities X = new()
    {
        Network = PublishNetworks.X, MaxCharacters = XPostBuilder.MaxWeightedChars, MaxMediaItems = 0,
        SupportsThreads = true, DerivesShortPost = true,
    };

    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    [Fact]
    public async Task Micro_preview_answers_the_owner_with_the_teaser_and_the_translation_when_asked()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);

            var ru = Assert.IsType<Ok<MicroPreview>>(await PreviewEndpoints.MicroAsync(db, EmptyConfig, "owner-1", draft.Id, PublishNetworks.X, null, X)).Value!;
            var en = Assert.IsType<Ok<MicroPreview>>(await PreviewEndpoints.MicroAsync(db, EmptyConfig, "owner-1", draft.Id, PublishNetworks.X, Languages.English, X)).Value!;

            Assert.Equal("Русский текст.", ru.Single.Text);
            Assert.Equal("English text.", en.Single.Text);
            Assert.Null(ru.BlogUrl);
            Assert.False(ru.HasAuthorText);
        }
    }

    [Fact]
    public async Task Micro_preview_carries_the_authors_own_text_for_that_network_and_language()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db);
            db.DraftTargetTexts.Add(new DraftTargetText { OwnerId = "owner-1", DraftId = draft.Id, Network = PublishNetworks.X, Language = Languages.Russian, Text = "Своими словами." });
            db.DraftTargetTexts.Add(new DraftTargetText { OwnerId = "owner-1", DraftId = draft.Id, Network = PublishNetworks.Bluesky, Language = Languages.Russian, Text = "Not for X." });
            db.SaveChanges();

            var preview = Assert.IsType<Ok<MicroPreview>>(await PreviewEndpoints.MicroAsync(db, EmptyConfig, "owner-1", draft.Id, PublishNetworks.X, null, X)).Value!;

            Assert.Equal("Своими словами.", preview.Single.Text);
            Assert.True(preview.HasAuthorText);
        }
    }

    [Fact]
    public async Task Micro_preview_is_404_for_another_owners_draft_and_a_missing_language()
    {
        var (db, connection) = Build();
        using (connection)
        {
            var draft = Seed(db, withTranslation: false);

            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await PreviewEndpoints.MicroAsync(db, EmptyConfig, "owner-2", draft.Id, PublishNetworks.X, null, X)));
            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await PreviewEndpoints.MicroAsync(db, EmptyConfig, "owner-1", draft.Id, PublishNetworks.X, Languages.English, X)));
        }
    }
}
