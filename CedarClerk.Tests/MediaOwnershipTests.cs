using System.Security.Claims;
using System.Text.Encodings.Web;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
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

    /// <summary>A's library upload, drawn on a board B collaborates on and claimed by no post.</summary>
    private readonly Guid boardAsset = Guid.NewGuid();

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

        SeedPost(Guid.NewGuid(), OwnerA, isPrivate: false, publicAsset, sharedAsset, assetA);
        SeedPost(Guid.NewGuid(), Legacy, isPrivate: false, assetLegacy);
        SeedPost(privatePost, OwnerA, isPrivate: true, privateAsset, sharedAsset);
        SeedPost(Guid.NewGuid(), OwnerA, isPrivate: true, sentToTelegram: true, channelPostAsset);

        Seed(boardAsset, OwnerA);
        SeedBoard(boardAsset, OwnerA, memberUserId: OwnerB);

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

    /// <summary>A project whose board draws one of the owner's uploads, shared with one account.</summary>
    private void SeedBoard(Guid asset, string ownerId, string memberUserId)
    {
        var project = new Project { OwnerId = ownerId, Name = "Cedar Quest" };
        var board = new CanvasBoard { OwnerId = ownerId, ProjectId = project.Id, Name = "Mood" };

        db.Projects.Add(project);
        db.CanvasBoards.Add(board);
        db.CanvasItems.Add(new CanvasItem
        {
            OwnerId = ownerId,
            ProjectId = project.Id,
            BoardId = board.Id,
            Kind = CanvasItemKinds.Image,
            Width = 460,
            Height = 460,
            Payload = $$"""{"url":"/media/asset_{{asset}}.jpg","naturalWidth":800,"naturalHeight":800,"alt":""}""",
        });
        db.ProjectMembers.Add(new ProjectMember
        {
            OwnerId = ownerId,
            ProjectId = project.Id,
            Email = "b@example.test",
            Role = ProjectRoles.Viewer,
            MemberUserId = memberUserId,
            AcceptedAt = DateTime.UtcNow,
            InvitedByUserId = ownerId,
        });
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
            services.AddSingleton<MediaGrant>();
            services.AddSingleton(sp => new MediaOwnerIndex(scopes ?? sp.GetRequiredService<IServiceScopeFactory>(), new TenantOwnerCache.ForMedia()));
            services.AddSingleton(sp => new MediaVisibilityIndex(scopes ?? sp.GetRequiredService<IServiceScopeFactory>()));
            services.AddSingleton(sp => new CanvasMediaIndex(scopes ?? sp.GetRequiredService<IServiceScopeFactory>()));

            provider = services.BuildServiceProvider();

            var pipeline = new ApplicationBuilder(provider);
            pipeline.UseTenantMedia(mediaDir);
            handler = pipeline.Build();
        }

        public PrivateAccess Access => provider.GetRequiredService<PrivateAccess>();
        public MediaGrant Grants => provider.GetRequiredService<MediaGrant>();
        public MediaVisibilityIndex Visibility => provider.GetRequiredService<MediaVisibilityIndex>();
        public MediaOwnerIndex Owners => provider.GetRequiredService<MediaOwnerIndex>();

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

    // The application host is the origin advertised to Telegram's fetcher and to OG scrapers, so
    // it stays open — but only for what is genuinely public. It used to serve anything nothing
    // gated, which meant a stranger with a GUID could read a draft's pictures and an untouched
    // library from a host that names no tenant.
    [Fact]
    public async Task The_application_host_serves_what_is_public_and_nothing_else()
    {
        var published = await GetAsync($"/media/asset_{publicAsset}.jpg", blogOwner: null);
        Assert.Equal(StatusCodes.Status200OK, published.Response.StatusCode);

        var unclaimed = await GetAsync($"/media/asset_{assetB}.jpg", blogOwner: null);
        Assert.Equal(StatusCodes.Status404NotFound, unclaimed.Response.StatusCode);

        var owner = await GetAsync($"/media/asset_{assetB}.jpg", blogOwner: null,
            c => c.Request.Headers["X-Test-User"] = OwnerB);
        Assert.Equal(StatusCodes.Status200OK, owner.Response.StatusCode);
    }

    // Package imports used to name the file with one GUID and give the row another. The owner's own
    // library then answered 404 for every imported picture, because the owner was looked up by the
    // GUID in the name alone (08.10.2026).
    [Fact]
    public async Task A_file_whose_name_does_not_carry_its_row_id_is_found_by_its_name()
    {
        var nameId = Guid.NewGuid();
        var fileName = $"asset_{nameId}.jpg";
        db.Assets.Add(new Asset
        {
            OwnerId = OwnerA,
            FileName = "image 98.png",
            ContentType = "image/jpeg",
            SizeBytes = Bytes.Length,
            LocalPath = fileName,
        });
        db.SaveChanges();
        File.WriteAllBytes(Path.Combine(mediaDir, fileName), Bytes);

        var owner = await GetAsync($"/media/{fileName}", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerA);
        var stranger = await GetAsync($"/media/{fileName}", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerB);
        var foreignBlog = await GetAsync($"/media/{fileName}", OwnerB);

        Assert.Equal(StatusCodes.Status200OK, owner.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, stranger.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, foreignBlog.Response.StatusCode);
    }

    [Fact]
    public async Task A_file_with_no_row_behind_it_is_not_found()
    {
        var orphan = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{orphan}.jpg"), Bytes);

        var ctx = await GetAsync($"/media/asset_{orphan}.jpg", OwnerA);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    // The edge caches /media by extension and the browser was handed a four-hour TTL on top: one
    // anonymous fetch of a draft's picture answered 404, and the signed-in owner then read that 404
    // back for the same URL until it expired — the picture looked deleted while the file and its
    // row were fine (14.09.2026). Every refusal says no-store, whichever question it failed.
    [Fact]
    public async Task A_refusal_is_never_cacheable()
    {
        var unparseable = await GetAsync("/media/not-an-asset.jpg", blogOwner: null);
        var noRow = await GetAsync($"/media/asset_{Guid.NewGuid()}.jpg", blogOwner: null);
        var anotherTenant = await GetAsync($"/media/asset_{assetB}.jpg", OwnerA);
        var stranger = await GetAsync($"/media/asset_{assetB}.jpg", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerA);

        foreach (var refused in new[] { unparseable, noRow, anotherTenant, stranger })
        {
            Assert.Equal(StatusCodes.Status404NotFound, refused.Response.StatusCode);
            Assert.Equal("private, no-store", refused.Response.Headers.CacheControl.ToString());
        }
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

        var owner = await new MediaOwnerIndex(scopes, new TenantOwnerCache.ForMedia()).OwnerOfAsync(new MediaRef(MediaRefKind.AssetOriginal, assetB), $"asset_{assetB}.jpg");

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

    // ADR-219. A picture on a shared board is a library upload no post claims, so the three older
    // answers all say no and the last one — "are you the owner" — is the wrong question on a
    // surface whose whole point is that somebody else is looking.
    [Fact]
    public async Task A_picture_on_a_shared_board_is_served_to_the_people_who_share_it()
    {
        var member = await GetAsync($"/media/asset_{boardAsset}.jpg", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerB);
        var stranger = await GetAsync($"/media/asset_{boardAsset}.jpg", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = Legacy);
        var anonymous = await GetAsync($"/media/asset_{boardAsset}.jpg", blogOwner: null);

        Assert.Equal(StatusCodes.Status200OK, member.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, stranger.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, anonymous.Response.StatusCode);
    }

    [Fact]
    public async Task Sharing_a_board_opens_what_is_on_it_and_nothing_else_of_the_owners()
    {
        // The membership is not a key to the account: a file the board does not draw stays shut.
        var elsewhere = await GetAsync($"/media/asset_{privateAsset}.jpg", blogOwner: null,
            prepare: c => c.Request.Headers[SignedInAs.Header] = OwnerB);

        Assert.Equal(StatusCodes.Status404NotFound, elsewhere.Response.StatusCode);
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

    // T-285. The narrowing used to be switched on by the host: a tenant subdomain refused another
    // account's file while the application host — same process, same tunnel — served it to anyone
    // with the GUID. What a file is must not depend on which name the reader typed.
    [Fact]
    public async Task A_file_no_published_post_claims_is_the_owners_alone_on_every_host()
    {
        var loose = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{loose}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = loose, OwnerId = OwnerB, FileName = "draft.jpg", LocalPath = $"asset_{loose}.jpg" });
        db.SaveChanges();

        foreach (var host in new string?[] { null, OwnerA })
        {
            var ctx = await GetAsync($"/media/asset_{loose}.jpg", host);
            Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        }

        var owner = await GetAsync($"/media/asset_{loose}.jpg", null, c => c.Request.Headers["X-Test-User"] = OwnerB);
        Assert.Equal(StatusCodes.Status200OK, owner.Response.StatusCode);
    }

    // The things a blog page draws for a stranger: they belong to no post, and they must stay open.
    [Fact]
    public async Task An_avatar_is_public_because_a_blog_header_draws_it()
    {
        var avatar = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{avatar}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = avatar, OwnerId = OwnerB, FileName = "me.jpg", LocalPath = $"asset_{avatar}.jpg" });
        db.Users.Single(u => u.Id == OwnerB).AvatarUrl = $"/media/asset_{avatar}.jpg";
        db.SaveChanges();

        var ctx = await GetAsync($"/media/asset_{avatar}.jpg", null);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task A_showcase_projects_cover_is_public()
    {
        var cover = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{cover}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = cover, OwnerId = OwnerB, FileName = "cover.jpg", LocalPath = $"asset_{cover}.jpg" });
        db.Projects.Add(new Project
        {
            OwnerId = OwnerB, Name = "Game", ShowcaseSlug = "game",
            CoverUrl = $"/media/asset_{cover}.jpg",
        });
        db.SaveChanges();

        var ctx = await GetAsync($"/media/asset_{cover}.jpg", null);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    // A project with no public page is not a public page, so its cover is not public either.
    [Fact]
    public async Task A_private_projects_cover_is_not_public()
    {
        var cover = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{cover}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = cover, OwnerId = OwnerB, FileName = "wip.jpg", LocalPath = $"asset_{cover}.jpg" });
        db.Projects.Add(new Project { OwnerId = OwnerB, Name = "WIP", CoverUrl = $"/media/asset_{cover}.jpg" });
        db.SaveChanges();

        var ctx = await GetAsync($"/media/asset_{cover}.jpg", null);
        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }


    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 404)]
    public async Task A_cover_inherits_its_posts_audience(bool isPrivate, int status)
    {
        var cover = Guid.NewGuid();
        Seed(cover, OwnerB);
        db.Drafts.Add(new Draft { OwnerId = OwnerB, Title = "Cover", BlogSlug = "cover",
            IsBlogPublished = true, IsPrivate = isPrivate, CoverImagePath = $"/media/asset_{cover}.jpg" });
        db.SaveChanges();
        var ctx = await GetAsync($"/media/asset_{cover}.jpg", null);
        Assert.Equal(status, ctx.Response.StatusCode);
    }

    [Theory]
    [InlineData(true, 200)]
    [InlineData(false, 404)]
    public async Task Gallery_images_require_a_public_showcase(bool published, int status)
    {
        var picture = Guid.NewGuid();
        Seed(picture, OwnerB);
        db.Projects.Add(new Project { OwnerId = OwnerB, Name = "Gallery",
            ShowcaseSlug = published ? "gallery" : null, ShowcaseGallery = $"/media/asset_{picture}.jpg" });
        db.SaveChanges();
        var ctx = await GetAsync($"/media/asset_{picture}.jpg", null);
        Assert.Equal(status, ctx.Response.StatusCode);
    }

    [Theory]
    [InlineData(false, false, 200)]
    [InlineData(true, false, 404)]
    [InlineData(false, true, 404)]
    public async Task Glossary_images_follow_the_rendered_posts_audience(bool isPrivate, bool excluded, int status)
    {
        var picture = Guid.NewGuid();
        Seed(picture, OwnerB);
        var term = new GlossaryEntryLanguage { OwnerId = OwnerB, Language = "en", LocalizedName = "Cedar",
            Entry = new GlossaryEntry { OwnerId = OwnerB, Name = "Cedar", Description = "A tree",
                ImageUrl = $"/media/asset_{picture}.jpg" } };
        var post = new Draft { OwnerId = OwnerB, Title = "Glossary", BlogSlug = "glossary",
            IsBlogPublished = true, IsPrivate = isPrivate, PrimaryLanguage = "en",
            CedarJson = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Cedar"}]}]}""" };
        db.GlossaryEntryLanguages.Add(term);
        db.Drafts.Add(post);
        if (excluded) db.DraftGlossaryExclusions.Add(new DraftGlossaryExclusion {
            OwnerId = OwnerB, DraftId = post.Id, GlossaryTermId = term.Id, Language = "en" });
        db.SaveChanges();
        var ctx = await GetAsync($"/media/asset_{picture}.jpg", null);
        Assert.Equal(status, ctx.Response.StatusCode);
    }

    // Telegram's fetcher is anonymous and pulls a file while the post is still a draft, so nothing
    // published claims it yet. The send hands it a signed key instead of leaving every unclaimed
    // file readable, which is what T-285 was.
    [Fact]
    public async Task A_signed_grant_opens_one_file_and_only_that_one()
    {
        var loose = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(mediaDir, $"asset_{loose}.jpg"), Bytes);
        db.Assets.Add(new Asset { Id = loose, OwnerId = OwnerB, FileName = "d.jpg", LocalPath = $"asset_{loose}.jpg" });
        db.SaveChanges();

        var name = $"asset_{loose}.jpg";
        var refused = await GetAsync($"/media/{name}", null);
        Assert.Equal(StatusCodes.Status404NotFound, refused.Response.StatusCode);

        var granted = await GetAsync($"/media/{name}", null,
            c => c.Request.QueryString = new QueryString($"?{MediaGrant.QueryKey}={Uri.EscapeDataString(harness.Grants.Issue(name))}"));
        Assert.Equal(StatusCodes.Status200OK, granted.Response.StatusCode);

        // A key for one file is not a key for the next.
        var elsewhere = await GetAsync($"/media/asset_{assetB}.jpg", null,
            c => c.Request.QueryString = new QueryString($"?{MediaGrant.QueryKey}={Uri.EscapeDataString(harness.Grants.Issue(name))}"));
        Assert.Equal(StatusCodes.Status404NotFound, elsewhere.Response.StatusCode);

        var forged = await GetAsync($"/media/{name}", null,
            c => c.Request.QueryString = new QueryString($"?{MediaGrant.QueryKey}=not-a-real-token"));
        Assert.Equal(StatusCodes.Status404NotFound, forged.Response.StatusCode);
    }
}
