using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// Two properties of the pipeline around the tenant filters: who a request on a blog host is allowed
// to act as, and how many times a host costs a query.
public class TenantScopeTests
{
    private const string Domain = "cedarclerk.app";
    private const string BlogHost = Consts.URLs.BlogHost;

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now = now.Add(by);
    }

    private static FakeClock Clock() => new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    // Counts what the middleware asks for rather than what SQLite is asked: one resolved context is
    // one connection and one round-trip, which is the cost the cache exists to remove.
    private sealed class CountingServices(IServiceProvider inner) : IServiceProvider
    {
        public int DbResolutions { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(CedarDbContext)) DbResolutions++;
            return inner.GetService(serviceType);
        }
    }

    private static CedarDbContext Database()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    private static string SeedUser(CedarDbContext db, string username, bool admin = false)
    {
        var user = new ApplicationUser
        {
            UserName = $"{username}@example.test",
            Email = $"{username}@example.test",
            TenantUsername = username,
            IsAdmin = admin,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private static ClaimsPrincipal SignedInAs(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestCookie"));

    #region The cache itself

    [Fact]
    public async Task The_cache_answers_what_the_lookup_answered_and_asks_once()
    {
        var cache = new TenantOwnerCache(Clock());
        var calls = 0;

        var first = await cache.GetAsync("beta", _ => { calls++; return Task.FromResult<string?>("owner-b"); });
        var second = await cache.GetAsync("beta", _ => { calls++; return Task.FromResult<string?>("owner-b"); });

        Assert.Equal("owner-b", first);
        Assert.Equal(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task An_unknown_name_is_cached_too()
    {
        var cache = new TenantOwnerCache(Clock());
        var calls = 0;

        for (var i = 0; i < 25; i++)
            Assert.Null(await cache.GetAsync("nobody", _ => { calls++; return Task.FromResult<string?>(null); }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task An_answer_is_re_read_once_its_lifetime_is_over()
    {
        var clock = Clock();
        var cache = new TenantOwnerCache(clock);
        await cache.GetAsync("beta", _ => Task.FromResult<string?>("owner-b"));

        clock.Advance(TenantOwnerCache.HitLifetime + TimeSpan.FromSeconds(1));
        var after = await cache.GetAsync("beta", _ => Task.FromResult<string?>("owner-c"));

        Assert.Equal("owner-c", after);
    }

    [Fact]
    public async Task An_unknown_name_is_re_read_sooner_than_a_known_one()
    {
        var clock = Clock();
        var cache = new TenantOwnerCache(clock);
        await cache.GetAsync("beta", _ => Task.FromResult<string?>("owner-b"));
        await cache.GetAsync("nobody", _ => Task.FromResult<string?>(null));

        clock.Advance(TenantOwnerCache.MissLifetime + TimeSpan.FromSeconds(1));

        Assert.Equal("owner-n", await cache.GetAsync("nobody", _ => Task.FromResult<string?>("owner-n")));
        Assert.Equal("owner-b", await cache.GetAsync("beta", _ => Task.FromResult<string?>("changed")));
    }

    [Fact]
    public async Task The_cache_stays_within_its_bound()
    {
        var cache = new TenantOwnerCache(Clock());

        for (var i = 0; i < TenantOwnerCache.Capacity + 2000; i++)
            await cache.GetAsync($"name{i}", _ => Task.FromResult<string?>(null));

        Assert.InRange(cache.Count, 1, TenantOwnerCache.Capacity);
    }

    #endregion

    #region Host resolution costs one query per window

    private static (HttpContext Ctx, CountingServices Services) Request(
        string host, CedarDbContext db, TenantContext? tenant = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(tenant ?? new TenantContext());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var counting = new CountingServices(services.BuildServiceProvider());
        var ctx = new DefaultHttpContext { RequestServices = counting };
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = "/";
        ctx.Response.Body = new MemoryStream();
        return (ctx, counting);
    }

    [Fact]
    public async Task A_subdomain_is_looked_up_once_and_then_served_from_the_cache()
    {
        using var db = Database();
        var ownerId = SeedUser(db, "beta");

        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache(Clock()));
        var owners = new List<string?>();
        var queries = 0;

        for (var i = 0; i < 25; i++)
        {
            var tenant = new TenantContext();
            var (ctx, services) = Request("beta.cedarclerk.app", db, tenant);
            await middleware.InvokeAsync(ctx);
            owners.Add(tenant.OwnerId);
            queries += services.DbResolutions;
        }

        Assert.All(owners, id => Assert.Equal(ownerId, id));
        Assert.Equal(1, queries);
    }

    [Fact]
    public async Task The_cached_owner_is_the_one_an_uncached_lookup_returns()
    {
        using var db = Database();
        SeedUser(db, "alpha");
        var ownerId = SeedUser(db, "beta");

        var cached = new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache(Clock()));
        var uncached = new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache(Clock()));

        var fromCache = new TenantContext();
        var (warm, _) = Request("beta.cedarclerk.app", db, fromCache);
        await cached.InvokeAsync(warm);

        var again = new TenantContext();
        var (second, _) = Request("beta.cedarclerk.app", db, again);
        await cached.InvokeAsync(second);

        var fresh = new TenantContext();
        var (third, _) = Request("beta.cedarclerk.app", db, fresh);
        await uncached.InvokeAsync(third);

        Assert.Equal(ownerId, fromCache.OwnerId);
        Assert.Equal(ownerId, again.OwnerId);
        Assert.Equal(fresh.OwnerId, again.OwnerId);
    }

    [Fact]
    public async Task An_unknown_subdomain_costs_one_query_for_the_whole_flood()
    {
        using var db = Database();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache(Clock()));
        var queries = 0;

        for (var i = 0; i < 25; i++)
        {
            var (ctx, services) = Request("nobody.cedarclerk.app", db);
            await middleware.InvokeAsync(ctx);
            queries += services.DbResolutions;
            Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        }

        Assert.Equal(1, queries);
    }

    [Fact]
    public async Task A_renamed_account_stops_answering_at_its_old_name_within_the_window()
    {
        using var db = Database();
        var ownerId = SeedUser(db, "beta");

        var clock = Clock();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache(clock));

        var before = new TenantContext();
        var (first, _) = Request("beta.cedarclerk.app", db, before);
        await middleware.InvokeAsync(first);
        Assert.Equal(ownerId, before.OwnerId);

        db.Users.Single(u => u.Id == ownerId).TenantUsername = "gamma";
        db.SaveChanges();

        clock.Advance(TenantOwnerCache.HitLifetime + TimeSpan.FromSeconds(1));

        var after = new TenantContext();
        var (second, _) = Request("beta.cedarclerk.app", db, after);
        await middleware.InvokeAsync(second);

        Assert.False(after.IsTenantRequest);
        Assert.Equal(StatusCodes.Status404NotFound, second.Response.StatusCode);
    }

    [Fact]
    public async Task A_new_account_starts_answering_once_the_miss_expires()
    {
        using var db = Database();
        var clock = Clock();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache(clock));

        var (cold, _) = Request("beta.cedarclerk.app", db);
        await middleware.InvokeAsync(cold);
        Assert.Equal(StatusCodes.Status404NotFound, cold.Response.StatusCode);

        var ownerId = SeedUser(db, "beta");
        clock.Advance(TenantOwnerCache.MissLifetime + TimeSpan.FromSeconds(1));

        var resolved = new TenantContext();
        var (warm, _) = Request("beta.cedarclerk.app", db, resolved);
        await middleware.InvokeAsync(warm);

        Assert.Equal(ownerId, resolved.OwnerId);
    }

    #endregion

    #region Who a request on a blog host acts as

    private sealed record Outcome(bool Reached, string? TenantId, ClaimsPrincipal User)
    {
        public string? ActsAs => User.FindFirstValue(ClaimTypes.NameIdentifier);
        public bool Authenticated => User.Identity?.IsAuthenticated == true;
    }

    // The pipeline in the order Program.cs runs it: the host resolved first, the scope chosen
    // before authentication, the user-derived tenant straight after it.
    private static async Task<Outcome> RunAsync(
        string host, CedarDbContext db, ClaimsPrincipal? user = null, string path = "/")
    {
        var resolved = new TenantContext();
        var cfg = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(resolved);
        services.AddSingleton<IConfiguration>(cfg);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (user is not null) ctx.User = user;

        await new TenantResolutionMiddleware(_ => Task.CompletedTask, Domain, new TenantOwnerCache())
            .InvokeAsync(ctx);

        var tenant = new TenantProvider();
        var reached = false;
        var fromUser = new TenantFromUserMiddleware(
            _ => { reached = true; return Task.CompletedTask; }, BlogHost);

        await new TenantScopeMiddleware(
                c => fromUser.InvokeAsync(c, tenant, NullLogger<TenantFromUserMiddleware>.Instance),
                BlogHost, new TenantOwnerCache())
            .InvokeAsync(ctx, tenant, resolved, cfg, NullLogger<TenantScopeMiddleware>.Instance);

        return new Outcome(reached, tenant.TenantId, ctx.User);
    }

    [Fact]
    public async Task A_signed_in_visitor_on_a_tenant_subdomain_acts_as_nobody()
    {
        using var db = Database();
        var subdomainOwner = SeedUser(db, "beta");
        var visitor = SeedUser(db, "marty");

        var outcome = await RunAsync("beta.cedarclerk.app", db, SignedInAs(visitor));

        Assert.True(outcome.Reached);
        Assert.False(outcome.Authenticated);
        Assert.Null(outcome.ActsAs);
        // The reader's own account is not the tenant either — the host decides what is readable.
        Assert.Equal(subdomainOwner, outcome.TenantId);
    }

    [Fact]
    public async Task A_visitor_signed_in_as_the_subdomain_owner_is_no_different()
    {
        using var db = Database();
        var subdomainOwner = SeedUser(db, "beta");

        var outcome = await RunAsync("beta.cedarclerk.app", db, SignedInAs(subdomainOwner));

        Assert.False(outcome.Authenticated);
        Assert.Null(outcome.ActsAs);
    }

    [Fact]
    public async Task A_signed_in_visitor_on_the_legacy_blog_host_acts_as_nobody()
    {
        using var db = Database();
        var blogOwner = SeedUser(db, "marty", admin: true);
        var visitor = SeedUser(db, "sasha");

        var outcome = await RunAsync(BlogHost, db, SignedInAs(visitor));

        Assert.True(outcome.Reached);
        Assert.False(outcome.Authenticated);
        Assert.Equal(blogOwner, outcome.TenantId);
    }

    [Fact]
    public async Task On_the_application_host_a_signed_in_user_is_still_their_own_tenant()
    {
        using var db = Database();
        var userId = SeedUser(db, "marty");

        var outcome = await RunAsync("cedarclerk.app", db, SignedInAs(userId), "/api/drafts");

        Assert.True(outcome.Reached);
        Assert.True(outcome.Authenticated);
        Assert.Equal(userId, outcome.TenantId);
    }

    [Fact]
    public async Task A_platform_path_keeps_both_its_scope_and_its_user()
    {
        using var db = Database();
        var userId = SeedUser(db, "marty", admin: true);

        var outcome = await RunAsync("cedarclerk.app", db, SignedInAs(userId), "/api/admin/users");

        Assert.True(outcome.Authenticated);
        Assert.Null(outcome.TenantId);
    }

    [Fact]
    public async Task An_anonymous_reader_on_a_subdomain_is_unchanged()
    {
        using var db = Database();
        var subdomainOwner = SeedUser(db, "beta");

        var outcome = await RunAsync("beta.cedarclerk.app", db);

        Assert.True(outcome.Reached);
        Assert.False(outcome.Authenticated);
        Assert.Equal(subdomainOwner, outcome.TenantId);
    }

    [Fact]
    public async Task The_legacy_blog_host_looks_its_owner_up_once()
    {
        using var db = Database();
        var blogOwner = SeedUser(db, "marty", admin: true);

        var cfg = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IConfiguration>(cfg);
        var counting = new CountingServices(services.BuildServiceProvider());

        var cache = new TenantOwnerCache(Clock());
        var tenants = new List<string?>();

        for (var i = 0; i < 25; i++)
        {
            var tenant = new TenantProvider();
            var ctx = new DefaultHttpContext { RequestServices = counting };
            ctx.Request.Host = new HostString(BlogHost);
            ctx.Request.Path = "/fonts/inter.woff2";
            ctx.Response.Body = new MemoryStream();

            await new TenantScopeMiddleware(_ => Task.CompletedTask, BlogHost, cache)
                .InvokeAsync(ctx, tenant, new TenantContext(), cfg,
                    NullLogger<TenantScopeMiddleware>.Instance);

            tenants.Add(tenant.TenantId);
        }

        Assert.All(tenants, id => Assert.Equal(blogOwner, id));
        Assert.Equal(1, counting.DbResolutions);
    }

    #endregion
}
