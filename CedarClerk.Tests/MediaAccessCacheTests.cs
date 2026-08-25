using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace CedarClerk.Tests;

// What a stranger walking made-up names under /media costs. The shape check has always been free;
// what was not is a name shaped like a file nobody owns — that answer used to be recomputed on
// every repeat, each on a freshly built context and its own SQLite connection.
public class MediaAccessCacheTests : IDisposable
{
    private const string Owner = "a";

    private readonly string mediaDir = Path.Combine(Path.GetTempPath(), "cedar-media-cache-tests", Guid.NewGuid().ToString());
    private readonly CedarDbContext db;
    private readonly ServiceProvider provider;
    private readonly CountingScopes scopes;

    private readonly Guid asset = Guid.NewGuid();

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now = now.Add(by);
    }

    // One scope is one context and one connection — the cost the cache exists to remove.
    private sealed class CountingScopes(IServiceProvider inner) : IServiceScopeFactory
    {
        public int Opened { get; private set; }

        public IServiceScope CreateScope()
        {
            Opened++;
            return inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CedarClerk.Tests";
        public string EnvironmentName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    public MediaAccessCacheTests()
    {
        Directory.CreateDirectory(mediaDir);

        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();

        db.Users.Add(new ApplicationUser { Id = Owner, UserName = "a@example.test", TenantUsername = "a" });
        db.Assets.Add(new Asset
        {
            Id = asset,
            OwnerId = Owner,
            FileName = "picture.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 3,
            LocalPath = $"asset_{asset}.jpg",
        });
        // Published, so the file is public — this suite measures cost, not the access rule.
        db.Drafts.Add(new Draft
        {
            OwnerId = Owner, Title = "post", BlogSlug = "post", IsBlogPublished = true,
            CedarJson = "{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"/media/asset_" + asset + ".jpg\"}}]}",
        });

        db.SaveChanges();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{asset}.jpg"), "abc"u8.ToArray());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddAuthentication();
        services.AddSingleton<IWebHostEnvironment, TestEnvironment>();
        services.AddSingleton(db);
        services.AddScoped<TenantProvider>();
        services.AddSingleton<PrivateAccess>();
        services.AddSingleton<MediaGrant>();

        var inner = services.BuildServiceProvider();
        scopes = new CountingScopes(inner);

        services.AddSingleton(new MediaOwnerIndex(scopes, new TenantOwnerCache.ForMedia()));
        services.AddSingleton(new MediaVisibilityIndex(scopes));
        provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        provider.Dispose();
        db.Dispose();
        try { Directory.Delete(mediaDir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private async Task<int> GetAsync(string path)
    {
        var pipeline = new ApplicationBuilder(provider);
        pipeline.UseTenantMedia(mediaDir);
        var handler = pipeline.Build();

        using var scope = provider.CreateScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = HttpMethods.Get;
        ctx.Request.Path = path;
        ctx.Request.Host = new HostString("cedarclerk.mooexe.dev");
        ctx.Response.Body = new MemoryStream();

        await handler(ctx);
        return ctx.Response.StatusCode;
    }

    [Fact]
    public async Task A_well_formed_name_nobody_owns_is_asked_about_once()
    {
        var guess = $"/media/asset_{Guid.NewGuid()}.jpg";

        for (var i = 0; i < 25; i++)
            Assert.Equal(StatusCodes.Status404NotFound, await GetAsync(guess));

        Assert.Equal(1, scopes.Opened);
    }

    [Fact]
    public async Task A_flood_of_distinct_guesses_costs_one_query_each_and_no_more()
    {
        var guesses = Enumerable.Range(0, 10).Select(_ => $"/media/asset_{Guid.NewGuid()}.jpg").ToList();

        foreach (var guess in guesses.Concat(guesses).Concat(guesses))
            Assert.Equal(StatusCodes.Status404NotFound, await GetAsync(guess));

        Assert.Equal(guesses.Count, scopes.Opened);
    }

    [Fact]
    public async Task A_page_full_of_one_owners_images_asks_about_visibility_once()
    {
        var file = $"/media/asset_{asset}.jpg";

        for (var i = 0; i < 12; i++)
            Assert.Equal(StatusCodes.Status200OK, await GetAsync(file));

        // The owner lookup and the account-wide visibility pass, once each.
        Assert.Equal(2, scopes.Opened);
    }

    [Fact]
    public async Task A_guess_is_asked_about_again_once_the_cache_has_let_it_go()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        var index = new MediaOwnerIndex(scopes, new TenantOwnerCache.ForMedia(clock));
        var missing = new MediaRef(MediaRefKind.AssetOriginal, Guid.NewGuid());

        Assert.Null(await index.OwnerOfAsync(missing));
        Assert.Null(await index.OwnerOfAsync(missing));
        Assert.Equal(1, scopes.Opened);

        clock.Advance(TenantOwnerCache.MissLifetime + TimeSpan.FromSeconds(1));
        Assert.Null(await index.OwnerOfAsync(missing));

        Assert.Equal(2, scopes.Opened);
    }

    [Fact]
    public async Task A_file_that_has_an_owner_is_looked_up_once()
    {
        var index = new MediaOwnerIndex(scopes, new TenantOwnerCache.ForMedia());
        var reference = new MediaRef(MediaRefKind.AssetOriginal, asset);

        for (var i = 0; i < 25; i++)
            Assert.Equal(Owner, await index.OwnerOfAsync(reference));

        Assert.Equal(1, scopes.Opened);
    }

    // The visibility answer is the account's, not the file's: a second file costs nothing, and the
    // pass runs again only after its own lifetime.
    [Fact]
    public async Task The_visibility_pass_is_one_per_account_per_lifetime()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        var index = new MediaVisibilityIndex(scopes, clock);

        Assert.False((await index.OfAsync(Owner)).Gated.ContainsKey(asset));
        Assert.Null((await index.OfAsync(Owner)).Gated.GetValueOrDefault(Guid.NewGuid()));
        Assert.Equal(1, scopes.Opened);

        clock.Advance(MediaVisibilityIndex.Lifetime + TimeSpan.FromSeconds(1));
        Assert.False((await index.OfAsync(Owner)).Gated.ContainsKey(asset));

        Assert.Equal(2, scopes.Opened);
    }

    private sealed class FailingScopes(IServiceProvider inner) : IServiceScopeFactory
    {
        public bool Fail { get; set; }
        public int Opened { get; private set; }

        public IServiceScope CreateScope()
        {
            Opened++;
            if (Fail) throw new InvalidOperationException("load failed");
            return inner.CreateScope();
        }
    }

    // The stale snapshot is extended before the reload is awaited, so that one caller refreshes
    // while the rest keep reading. A reload that throws must not leave that extension standing:
    // the reload's token is the client's, so a reader who aborts at each expiry could otherwise
    // hold a "not gated" verdict open for as long as it kept aborting.
    [Fact]
    public async Task A_failed_refresh_does_not_extend_the_answer_it_failed_to_replace()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        var failing = new FailingScopes(provider);
        var index = new MediaVisibilityIndex(failing, clock);

        Assert.False((await index.OfAsync(Owner)).Gated.ContainsKey(asset));
        var afterFirst = failing.Opened;

        clock.Advance(MediaVisibilityIndex.Lifetime + TimeSpan.FromSeconds(1));
        failing.Fail = true;
        await Assert.ThrowsAnyAsync<Exception>(() => index.OfAsync(Owner).AsTask());

        // Still expired: the next caller must try again rather than be handed the old answer.
        failing.Fail = false;
        Assert.False((await index.OfAsync(Owner)).Gated.ContainsKey(asset));
        Assert.True(failing.Opened > afterFirst + 1,
            "a failed reload left the expired snapshot extended, so nobody re-read it");
    }
}
