using System.Security.Claims;
using System.Text.Encodings.Web;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CedarClerk.Tests;

// The whole pipeline the real one has under /media: the access check, then the ordinary static-file
// middleware. Built by hand rather than mocked, because what has to keep working is exactly what
// that middleware does — ranges, ETags, content types.
public class MediaOwnershipTests : IDisposable
{
    private const string OwnerA = "a";
    private const string OwnerB = "b";
    private const string Legacy = "legacy";

    private static readonly byte[] Bytes = "cedar-media-bytes"u8.ToArray();

    private readonly string mediaDir = Path.Combine(Path.GetTempPath(), "cedar-media-tests", Guid.NewGuid().ToString());
    private readonly CedarDbContext db;
    private readonly Harness harness;

    private readonly Guid assetA = Guid.NewGuid();
    private readonly Guid assetB = Guid.NewGuid();
    private readonly Guid assetLegacy = Guid.NewGuid();
    private readonly Guid channelA = Guid.NewGuid();
    private readonly Guid channelB = Guid.NewGuid();

    // A's files by the audience they were published to.
    private readonly Guid publicAsset = Guid.NewGuid();
    private readonly Guid privateAsset = Guid.NewGuid();
    private readonly Guid sharedAsset = Guid.NewGuid();
    private readonly Guid channelPostAsset = Guid.NewGuid();
    private readonly Guid privatePost = Guid.NewGuid();

    public MediaOwnershipTests()
    {
        Directory.CreateDirectory(Path.Combine(mediaDir, "channels"));

        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();

        db.Users.Add(new ApplicationUser { Id = OwnerA, UserName = "a@example.test", TenantUsername = "a" });
        db.Users.Add(new ApplicationUser { Id = OwnerB, UserName = "b@example.test", TenantUsername = "b" });
        db.Users.Add(new ApplicationUser { Id = Legacy, UserName = "l@example.test", TenantUsername = "legacy" });

        Seed(assetA, OwnerA);
        Seed(assetB, OwnerB);
        Seed(assetLegacy, Legacy);
        Seed(publicAsset, OwnerA);
        Seed(privateAsset, OwnerA);
        Seed(sharedAsset, OwnerA);
        Seed(channelPostAsset, OwnerA);
        SeedChannel(channelA, OwnerA);
        SeedChannel(channelB, OwnerB);

        SeedPost(Guid.NewGuid(), OwnerA, isPrivate: false, publicAsset, sharedAsset);
        SeedPost(privatePost, OwnerA, isPrivate: true, privateAsset, sharedAsset);
        SeedPost(Guid.NewGuid(), OwnerA, isPrivate: true, sentToTelegram: true, channelPostAsset);

        db.SaveChanges();
        harness = new Harness(db, mediaDir);
    }

    private void Seed(Guid id, string ownerId)
    {
        db.Assets.Add(new Asset
        {
            Id = id,
            OwnerId = ownerId,
            FileName = "picture.jpg",
            ContentType = "image/jpeg",
            SizeBytes = Bytes.Length,
            LocalPath = $"asset_{id}.jpg",
        });
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{id}.jpg"), Bytes);
    }

    private void SeedChannel(Guid id, string ownerId)
    {
        db.Channels.Add(new Channel { Id = id, OwnerId = ownerId, Title = ownerId, AvatarPath = $"channels/{id}.jpg" });
        File.WriteAllBytes(Path.Combine(mediaDir, "channels", $"{id}.jpg"), Bytes);
    }

    private void SeedPost(Guid id, string ownerId, bool isPrivate, params Guid[] assets) =>
        SeedPost(id, ownerId, isPrivate, sentToTelegram: false, assets);

    private void SeedPost(Guid id, string ownerId, bool isPrivate, bool sentToTelegram, params Guid[] assets)
    {
        var images = string.Join(",", assets.Select(a =>
            $$$"""{"type":"image","attrs":{"src":"/media/asset_{{{a}}}.jpg"}}"""));

        db.Drafts.Add(new Draft
        {
            Id = id,
            OwnerId = ownerId,
            Title = "post",
            BlogSlug = id.ToString("N"),
            IsBlogPublished = true,
            IsPrivate = isPrivate,
            LastTelegramChatId = sentToTelegram ? "-100" : null,
            CedarJson = $$"""{"type":"doc","content":[{{images}}]}""",
        });
    }

    public void Dispose()
    {
        harness.Dispose();
        db.Dispose();
        try { Directory.Delete(mediaDir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
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

    // Stands in for the Identity cookie: the middleware only ever asks the default scheme who this
    // is, and building a real key ring for that would test DataProtection rather than the door.
    private sealed class SignedInAs(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Context.Request.Headers[Header].ToString() is not { Length: > 0 } userId)
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    // One provider for the whole test, so a grant minted through it validates on the next request —
    // a fresh DataProtection key ring per call would refuse the cookie it had just written.
    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly RequestDelegate handler;
        private readonly string mediaDir;

        public Harness(CedarDbContext db, string mediaDir, IServiceScopeFactory? scopes = null)
        {
            this.mediaDir = mediaDir;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddScheme<AuthenticationSchemeOptions, SignedInAs>(IdentityConstants.ApplicationScheme, _ => { });
            services.AddSingleton<IWebHostEnvironment, TestEnvironment>();
            services.AddSingleton(db);
            services.AddScoped<TenantProvider>();
            services.AddSingleton<PrivateAccess>();
            services.AddSingleton(sp => new MediaOwnerIndex(scopes ?? sp.GetRequiredService<IServiceScopeFactory>()));
            services.AddSingleton(sp => new MediaVisibilityIndex(scopes ?? sp.GetRequiredService<IServiceScopeFactory>()));

            provider = services.BuildServiceProvider();

            var pipeline = new ApplicationBuilder(provider);
            pipeline.UseTenantMedia(mediaDir);
            handler = pipeline.Build();
        }

        public PrivateAccess Access => provider.GetRequiredService<PrivateAccess>();

        /// <param name="blogOwner">
        /// Whose blog this host renders — a tenant subdomain or the legacy blog host. Null is the
        /// application host, which renders nobody's.
        /// </param>
        public async Task<HttpContext> GetAsync(string path, string? blogOwner, Action<HttpContext>? prepare = null)
        {
            var scope = provider.CreateScope();
            if (blogOwner is not null)
                scope.ServiceProvider.GetRequiredService<TenantProvider>().UseTenant(blogOwner);

            var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            ctx.Request.Method = HttpMethods.Get;
            ctx.Request.Path = path;
            ctx.Request.Host = new HostString(blogOwner is null ? "cedarclerk.mooexe.dev" : $"{blogOwner}.cedarclerk.app");
            ctx.Response.Body = new MemoryStream();
            prepare?.Invoke(ctx);

            await handler(ctx);
            scope.Dispose();
            return ctx;
        }

        public void Dispose() => provider.Dispose();
    }

    private Task<HttpContext> GetAsync(string path, string? blogOwner, Action<HttpContext>? prepare = null) =>
        harness.GetAsync(path, blogOwner, prepare);

    private void GrantFor(HttpContext ctx, Guid draftId) =>
        ctx.Request.Headers.Cookie = $"{PrivateAccess.CookieName(draftId)}={harness.Access.Grant(draftId, "token")}";

    [Fact]
    public async Task A_tenant_is_served_its_own_asset()
    {
        var ctx = await GetAsync($"/media/asset_{assetA}.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Equal("image/jpeg", ctx.Response.ContentType);
        Assert.Equal(Bytes.Length, ctx.Response.Body.Length);
    }

    [Fact]
    public async Task Another_tenants_asset_is_not_found()
    {
        var ctx = await GetAsync($"/media/asset_{assetB}.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.Equal(0, ctx.Response.Body.Length);
    }

    [Fact]
    public async Task Another_tenants_telegram_derivative_is_not_found()
    {
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{assetB}_tg.jpg"), Bytes);

        var ctx = await GetAsync($"/media/asset_{assetB}_tg.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_channel_avatar_is_not_found()
    {
        var own = await GetAsync($"/media/channels/{channelA}.jpg", OwnerA);
        var foreign = await GetAsync($"/media/channels/{channelB}.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status200OK, own.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, foreign.Response.StatusCode);
    }

    // Replaces "the app host serves every owner's media", which asserted the hole rather than the
    // property: the legacy host is one account's blog too, and a subdomain that refuses a file must
    // not be talked out of it by a request to blog.mooexe.dev for the same bytes.
    [Fact]
    public async Task The_legacy_blog_host_serves_only_the_legacy_owners_media()
    {
        var own = await GetAsync($"/media/asset_{assetLegacy}.jpg", Legacy);
        var foreign = await GetAsync($"/media/asset_{assetA}.jpg", Legacy);

        Assert.Equal(StatusCodes.Status200OK, own.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, foreign.Response.StatusCode);
    }

    // Telegram's fetcher and every OG scraper are anonymous, have no cookie and arrive at the
    // application host by a URL built on Cedar:MainHost. Whose account the file belongs to is not a
    // question they can answer, so what is published stays readable there.
    [Fact]
    public async Task The_application_host_serves_a_published_file_anonymously()
    {
        var published = await GetAsync($"/media/asset_{publicAsset}.jpg", blogOwner: null);
        var unpublished = await GetAsync($"/media/asset_{assetB}.jpg", blogOwner: null);

        Assert.Equal(StatusCodes.Status200OK, published.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, unpublished.Response.StatusCode);
    }

    [Fact]
    public async Task A_file_with_no_row_behind_it_is_not_found()
    {
        var orphan = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{orphan}.jpg"), Bytes);

        var ctx = await GetAsync($"/media/asset_{orphan}.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    private sealed class ExplodingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("The database must not be reached.");
    }

    [Fact]
    public async Task An_unparseable_name_never_reaches_the_database()
    {
        using var blind = new Harness(db, mediaDir, new ExplodingScopes());

        var ctx = await blind.GetAsync("/media/../cedar.db", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task A_path_outside_the_three_shapes_never_reaches_the_database()
    {
        using var blind = new Harness(db, mediaDir, new ExplodingScopes());

        var ctx = await blind.GetAsync("/media/dataprotection-keys/key.xml", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    private sealed class RecordingScopes(IServiceProvider inner) : IServiceScopeFactory
    {
        public TenantProvider? Opened { get; private set; }

        public IServiceScope CreateScope()
        {
            var scope = inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
            Opened = scope.ServiceProvider.GetRequiredService<TenantProvider>();
            return scope;
        }
    }

    // Under the tenant filter every foreign file would resolve to "no owner" — which fails closed,
    // so the mistake would never announce itself.
    [Fact]
    public async Task The_owner_lookup_runs_in_a_platform_scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<TenantProvider>();
        var scopes = new RecordingScopes(services.BuildServiceProvider());

        var owner = await new MediaOwnerIndex(scopes).OwnerOfAsync(new MediaRef(MediaRefKind.AssetOriginal, assetB));

        Assert.Equal(OwnerB, owner);
        Assert.True(scopes.Opened?.IsPlatform);
    }

    [Fact]
    public async Task A_range_request_for_an_allowed_file_is_partial_content()
    {
        var ctx = await GetAsync($"/media/asset_{assetA}.jpg", OwnerA,
            prepare: c => c.Request.Headers.Range = "bytes=0-3");

        Assert.Equal(StatusCodes.Status206PartialContent, ctx.Response.StatusCode);
        Assert.Equal(4, ctx.Response.ContentLength);
        Assert.StartsWith("bytes 0-3/", ctx.Response.Headers.ContentRange.ToString());
    }

    [Fact]
    public async Task A_conditional_get_for_an_allowed_file_is_still_not_modified()
    {
        var first = await GetAsync($"/media/asset_{assetA}.jpg", OwnerA);
        var etag = first.Response.Headers.ETag.ToString();
        Assert.NotEmpty(etag);

        var second = await GetAsync($"/media/asset_{assetA}.jpg", OwnerA,
            prepare: c => c.Request.Headers.IfNoneMatch = etag);

        Assert.Equal(StatusCodes.Status304NotModified, second.Response.StatusCode);
    }

    [Fact]
    public async Task A_private_posts_image_needs_the_same_grant_the_page_needs()
    {
        var uninvited = await GetAsync($"/media/asset_{privateAsset}.jpg", OwnerA);
        var invited = await GetAsync($"/media/asset_{privateAsset}.jpg", OwnerA,
            prepare: c => GrantFor(c, privatePost));

        Assert.Equal(StatusCodes.Status404NotFound, uninvited.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, invited.Response.StatusCode);
        Assert.Equal("private, no-store", invited.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task A_grant_for_another_post_opens_nothing()
    {
        var ctx = await GetAsync($"/media/asset_{privateAsset}.jpg", OwnerA,
            prepare: c => GrantFor(c, Guid.NewGuid()));

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    // The application host is where the gate would otherwise be a matter of which name was typed.
    [Fact]
    public async Task A_private_posts_image_is_refused_on_the_application_host_too()
    {
        var ctx = await GetAsync($"/media/asset_{privateAsset}.jpg", blogOwner: null);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task A_private_posts_telegram_derivative_is_gated_with_it()
    {
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{privateAsset}_tg.jpg"), Bytes);

        var ctx = await GetAsync($"/media/asset_{privateAsset}_tg.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    // The answer for a file two posts use: the open one wins, because the picture is already out.
    [Fact]
    public async Task A_file_a_public_post_also_uses_is_not_gated()
    {
        var ctx = await GetAsync($"/media/asset_{sharedAsset}.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    // Channel history cannot be edited, so a picture that has been sent to one is not something the
    // blog page's gate can take back — and refusing it would break the send that is fetching it.
    [Fact]
    public async Task A_private_post_already_sent_to_telegram_does_not_gate_its_media()
    {
        var ctx = await GetAsync($"/media/asset_{channelPostAsset}.jpg", blogOwner: null);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task The_owner_is_served_their_own_gated_file()
    {
        var owner = await GetAsync($"/media/asset_{privateAsset}.jpg", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerA);
        var somebodyElse = await GetAsync($"/media/asset_{privateAsset}.jpg", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerB);

        Assert.Equal(StatusCodes.Status200OK, owner.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, somebodyElse.Response.StatusCode);
    }

    // Taking a private post down must not be what opens its pictures. The gate was derived from
    // published posts only, so unpublishing removed the post from the index and its files fell
    // into the "nothing claims this" branch.
    [Fact]
    public async Task Unpublishing_a_private_post_does_not_open_its_media()
    {
        var post = Guid.NewGuid();
        var asset = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{asset}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = asset, OwnerId = OwnerA, FileName = "p.jpg", LocalPath = $"asset_{asset}.jpg" });
        SeedPost(post, OwnerA, isPrivate: true, asset);
        db.SaveChanges();

        var gated = await GetAsync($"/media/asset_{asset}.jpg", OwnerA);
        Assert.Equal(StatusCodes.Status404NotFound, gated.Response.StatusCode);

        db.Drafts.Single(d => d.Id == post).IsBlogPublished = false;
        db.SaveChanges();

        var afterUnpublish = await GetAsync($"/media/asset_{asset}.jpg", OwnerA);
        Assert.Equal(StatusCodes.Status404NotFound, afterUnpublish.Response.StatusCode);
    }

    [Fact]
    public async Task A_private_post_that_was_never_published_still_gates_its_media()
    {
        var post = Guid.NewGuid();
        var asset = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{asset}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = asset, OwnerId = OwnerA, FileName = "n.jpg", LocalPath = $"asset_{asset}.jpg" });
        SeedPost(post, OwnerA, isPrivate: true, asset);
        db.SaveChanges();
        db.Drafts.Single(d => d.Id == post).IsBlogPublished = false;
        db.SaveChanges();

        var ctx = await GetAsync($"/media/asset_{asset}.jpg", OwnerA);
        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    // The owner still reaches it - the editor asks for these files while the post is a draft.
    [Fact]
    public async Task The_owner_still_reads_an_unpublished_private_posts_media()
    {
        var post = Guid.NewGuid();
        var asset = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{asset}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = asset, OwnerId = OwnerA, FileName = "o.jpg", LocalPath = $"asset_{asset}.jpg" });
        SeedPost(post, OwnerA, isPrivate: true, asset);
        db.SaveChanges();
        db.Drafts.Single(d => d.Id == post).IsBlogPublished = false;
        db.SaveChanges();

        var ctx = await GetAsync($"/media/asset_{asset}.jpg", OwnerA, c => c.Request.Headers["X-Test-User"] = OwnerA);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }
}
