using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Search;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// Wave 1 item 2, blog surface — the /search page over a stub index: what reaches the index (this
// owner, this query), and what the page does with the hits (escapes them). The FTS ranking itself
// is DraftSearchIndexTests' business.
public class BlogSearchPageTests
{
    private sealed class StubIndex : IDraftSearchIndex
    {
        public string? AskedOwner;
        public string? AskedQuery;
        public string? AskedLang;
        public List<BlogSearchHit> Hits = [];

        public Task ReindexDraftAsync(Guid draftId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveDraftAsync(Guid draftId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<BlogSearchHit>> SearchPublishedAsync(
            string ownerId, string query, string? lang, int limit = 20, CancellationToken ct = default)
        {
            AskedOwner = ownerId;
            AskedQuery = query;
            AskedLang = lang;
            return Task.FromResult<IReadOnlyList<BlogSearchHit>>(Hits);
        }

        public Task<IReadOnlyList<DraftSearchHit>> SearchDraftsAsync(
            string ownerId, string query, int limit = 20, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DraftSearchHit>>([]);
    }

    // BlogTestHost's shape with one extra registration: the page resolves IDraftSearchIndex from
    // the request services, and here that is the stub.
    private static HttpContext Request(CedarDbContext db, StubIndex index, string query = "")
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDataProtection();
        services.AddSingleton<PrivateAccess>();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<CedarClerk.Server.Email.ResendEmailProvider>();
        services.AddSingleton<IDraftSearchIndex>(index);

        var owner = db.Users.OrderBy(u => u.Id).First();
        var tenant = new TenantContext();
        tenant.Resolve("tenant", owner.Id);
        services.AddSingleton(tenant);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/search";
        if (query.Length > 0)
            ctx.Request.QueryString = new QueryString(query);
        ctx.Request.Host = new HostString($"tenant.{Consts.URLs.TenantHost}");
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task An_empty_query_renders_the_form_and_asks_the_index_nothing()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var index = new StubIndex();
        var ctx = Request(db, index);

        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        var body = BlogTestHost.Body(ctx);
        Assert.Contains("<form class=\"search-form\"", body);
        Assert.Contains("noindex", body);
        Assert.Null(index.AskedQuery);
    }

    [Fact]
    public async Task Hits_render_as_cards_scoped_to_this_owner()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var index = new StubIndex
        {
            Hits = [new BlogSearchHit("my-post", "My post", "…the words around the match…", DateTime.UtcNow, "ru")],
        };
        var ctx = Request(db, index, "?q=words");

        await BlogEndpoints.HandleRequest(ctx);

        var body = BlogTestHost.Body(ctx);
        Assert.Equal("o1", index.AskedOwner);
        Assert.Equal("words", index.AskedQuery);
        Assert.Contains("href=\"/my-post?lang=ru\"", body);
        Assert.Contains("My post", body);
        Assert.Contains("the words around the match", body);
    }

    [Fact]
    public async Task Hit_text_is_escaped_never_markup()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var index = new StubIndex
        {
            Hits = [new BlogSearchHit("xss", "<script>alert(1)</script>", "<b>bold</b> claim", null, "en")],
        };
        var ctx = Request(db, index, "?q=script");

        await BlogEndpoints.HandleRequest(ctx);

        var body = BlogTestHost.Body(ctx);
        Assert.DoesNotContain("<script>alert(1)</script>", body);
        Assert.DoesNotContain("<b>bold</b>", body);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", body);
    }

    [Fact]
    public async Task No_hits_is_an_honest_empty_state()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var index = new StubIndex();
        var ctx = Request(db, index, "?q=nothing");

        await BlogEndpoints.HandleRequest(ctx);

        var body = BlogTestHost.Body(ctx);
        Assert.Contains("Nothing found.", body);
    }

    [Fact]
    public async Task Russian_search_retains_its_language_when_refined()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var index = new StubIndex();
        var ctx = Request(db, index, "?q=words&lang=ru");

        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal("ru", index.AskedLang);
        var html = BlogTestHost.Body(ctx);
        Assert.Contains("name=\"lang\" value=\"ru\"", html);
        Assert.Contains("href=\"/?lang=ru\"", html);
    }
}
