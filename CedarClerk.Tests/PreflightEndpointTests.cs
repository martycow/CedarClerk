using System.Net;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// T-238. Preflight is a checklist, not a gate: whatever it finds, the answer is warnings in a 200
// body — so the tests are about the warnings being right, and about the probe budget (one request
// per distinct link across every language, and none of it ever throwing).
public class PreflightEndpointTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(respond(request));
        }
    }

    private static string DocWithLink(string href) =>
        $$$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"see","marks":[{"type":"link","attrs":{"href":"{{{href}}}"}}]}]}]}""";

    private static (ServiceProvider Provider, SqliteConnection Connection) Build()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<TenantProvider>();
        services.AddDbContext<CedarDbContext>(o => o.UseSqlite(connection));
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreatePlatformScope())
            scope.ServiceProvider.GetRequiredService<CedarDbContext>().Database.EnsureCreated();

        return (provider, connection);
    }

    private static async Task<PreflightEndpoints.PreflightResponse> RunAsync(
        ServiceProvider provider, Draft draft, string[] languages, StubHandler handler)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        var stored = await db.Drafts.FirstAsync(d => d.Id == draft.Id);
        // Fixture hostnames don't exist in real DNS; resolve them to a public address so the
        // SSRF guard lets the stub handler answer.
        var linkCheck = new LinkCheckService(new HttpClient(handler),
            (_, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }));
        return await PreflightEndpoints.RunAsync(db, stored, languages, linkCheck);
    }

    private static async Task<Draft> SeedAsync(ServiceProvider provider, string cedarJson, (string Language, string CedarJson)? translation = null)
    {
        using var scope = provider.CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
        db.Users.Add(new ApplicationUser { Id = "owner-1", UserName = "owner-1", Email = "owner-1@test.local" });
        var draft = new Draft { OwnerId = "owner-1", Title = "T", CedarJson = cedarJson, PrimaryLanguage = Languages.Russian };
        db.Drafts.Add(draft);
        if (translation is { } t)
            db.DraftTranslations.Add(new DraftTranslation
            {
                OwnerId = "owner-1", DraftId = draft.Id, Language = t.Language, Title = "T", CedarJson = t.CedarJson,
            });
        await db.SaveChangesAsync();
        return draft;
    }

    private static StubHandler Ok() => new(_ => new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task An_empty_ticked_language_version_warns()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var draft = await SeedAsync(provider, "{}");

            var result = await RunAsync(provider, draft, [Languages.Russian], Ok());

            var lang = Assert.Single(result.PerLanguage);
            Assert.True(lang.EmptyVersion);
            Assert.Empty(lang.DeadLinks);
        }
    }

    [Fact]
    public async Task A_language_with_no_version_at_all_warns_as_empty()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var draft = await SeedAsync(provider, DocWithLink("https://ok.example/"));

            var result = await RunAsync(provider, draft, [Languages.English], Ok());

            var lang = Assert.Single(result.PerLanguage);
            Assert.True(lang.EmptyVersion);
            Assert.Empty(lang.DeadLinks);
        }
    }

    [Fact]
    public async Task A_version_with_text_does_not_warn()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var draft = await SeedAsync(provider, DocWithLink("https://ok.example/"));

            var result = await RunAsync(provider, draft, [Languages.Russian], Ok());

            Assert.False(Assert.Single(result.PerLanguage).EmptyVersion);
        }
    }

    [Fact]
    public async Task A_dead_link_lands_on_the_language_that_carries_it()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var draft = await SeedAsync(provider, DocWithLink("https://gone.example/"),
                (Languages.English, DocWithLink("https://ok.example/")));
            var handler = new StubHandler(r => new HttpResponseMessage(
                r.RequestUri!.Host == "gone.example" ? HttpStatusCode.NotFound : HttpStatusCode.OK));

            var result = await RunAsync(provider, draft, [Languages.Russian, Languages.English], handler);

            var ru = result.PerLanguage.Single(l => l.Language == Languages.Russian);
            var dead = Assert.Single(ru.DeadLinks);
            Assert.Equal("https://gone.example/", dead.Url);
            Assert.Equal("404", dead.Status);
            Assert.Empty(result.PerLanguage.Single(l => l.Language == Languages.English).DeadLinks);
        }
    }

    [Fact]
    public async Task The_same_link_in_two_languages_is_probed_once()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var draft = await SeedAsync(provider, DocWithLink("https://shared.example/"),
                (Languages.English, DocWithLink("https://shared.example/")));
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var result = await RunAsync(provider, draft, [Languages.Russian, Languages.English], handler);

            Assert.Equal(1, handler.Requests);
            Assert.All(result.PerLanguage, l => Assert.Single(l.DeadLinks));
        }
    }

    [Fact]
    public async Task Every_probe_failing_still_yields_warnings_not_an_exception()
    {
        var (provider, connection) = Build();
        using (connection)
        {
            var draft = await SeedAsync(provider, DocWithLink("https://down.example/"));
            var handler = new StubHandler(_ => throw new HttpRequestException("refused"));

            var result = await RunAsync(provider, draft, [Languages.Russian], handler);

            var dead = Assert.Single(Assert.Single(result.PerLanguage).DeadLinks);
            Assert.Equal(LinkCheckService.Unreachable, dead.Status);
        }
    }
}
