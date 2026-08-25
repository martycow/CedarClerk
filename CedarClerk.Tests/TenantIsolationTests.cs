using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CedarClerk.Tests;

// The test the whole phase exists for: tenant A must not see tenant B's rows, and the code that
// legitimately reads across owners must have said so out loud.
public class TenantIsolationTests
{
    private static Microsoft.Data.Sqlite.SqliteConnection SharedDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var seed = Open(connection, TenantProvider.Platform());
        seed.Database.EnsureCreated();
        return connection;
    }

    private static CedarDbContext Open(Microsoft.Data.Sqlite.SqliteConnection connection, TenantProvider tenant) =>
        new(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, tenant);

    private static CedarDbContext AsTenant(Microsoft.Data.Sqlite.SqliteConnection c, string ownerId) =>
        Open(c, TenantProvider.For(ownerId));

    private static CedarDbContext AsPlatform(Microsoft.Data.Sqlite.SqliteConnection c) =>
        Open(c, TenantProvider.Platform());

    private const string A = "tenant-a";
    private const string B = "tenant-b";

    private static Microsoft.Data.Sqlite.SqliteConnection Seeded()
    {
        var connection = SharedDatabase();
        using var db = AsPlatform(connection);

        // Channel.OwnerId is a real foreign key into AspNetUsers, so the accounts have to exist.
        db.Users.Add(new ApplicationUser { Id = A, UserName = "a@x.test", Email = "a@x.test" });
        db.Users.Add(new ApplicationUser { Id = B, UserName = "b@x.test", Email = "b@x.test" });
        db.SaveChanges();

        db.Drafts.Add(new Draft { OwnerId = A, Title = "A's post" });
        db.Drafts.Add(new Draft { OwnerId = B, Title = "B's post" });
        db.Assets.Add(new Asset { OwnerId = A, FileName = "a.png", LocalPath = "a.png" });
        db.Assets.Add(new Asset { OwnerId = B, FileName = "b.png", LocalPath = "b.png" });
        db.Channels.Add(new Channel { OwnerId = A, Title = "A's channel", TelegramChatId = 1 });
        db.Channels.Add(new Channel { OwnerId = B, Title = "B's channel", TelegramChatId = 2 });
        db.Folders.Add(new Folder { OwnerId = A, Name = "A" });
        db.Folders.Add(new Folder { OwnerId = B, Name = "B" });
        db.SaveChanges();

        return connection;
    }

    [Fact]
    public void A_tenant_sees_only_its_own_rows()
    {
        using var connection = Seeded();
        using var db = AsTenant(connection, A);

        Assert.Equal(["A's post"], db.Drafts.Select(d => d.Title).ToList());
        Assert.Equal(["a.png"], db.Assets.Select(a => a.FileName).ToList());
        Assert.Equal(["A's channel"], db.Channels.Select(c => c.Title).ToList());
        Assert.Equal(["A"], db.Folders.Select(f => f.Name).ToList());
    }

    // Knowing the id is not access. Every "fetch by id" call site in the app looks exactly like
    // this, and before the filter existed the OwnerId check beside it was the only thing stopping
    // it — 61 hand-written checks, any one of which could have been forgotten.
    [Fact]
    public void Knowing_another_tenants_id_does_not_grant_access()
    {
        using var connection = Seeded();

        Guid bsDraftId;
        using (var platform = AsPlatform(connection))
            bsDraftId = platform.Drafts.Single(d => d.OwnerId == B).Id;

        using var db = AsTenant(connection, A);
        Assert.Null(db.Drafts.FirstOrDefault(d => d.Id == bsDraftId));
        Assert.Null(db.Drafts.Find(bsDraftId));
    }

    [Fact]
    public void Counts_and_aggregates_are_scoped_too()
    {
        using var connection = Seeded();
        using var db = AsTenant(connection, A);

        Assert.Equal(1, db.Drafts.Count());
        Assert.Equal(1, db.Assets.Count());
        Assert.False(db.Drafts.Any(d => d.OwnerId == B));
    }

    [Fact]
    public void A_tenant_cannot_delete_what_it_cannot_see()
    {
        using var connection = Seeded();

        using (var db = AsTenant(connection, A))
        {
            db.Drafts.RemoveRange(db.Drafts);
            db.SaveChanges();
        }

        using var platform = AsPlatform(connection);
        Assert.Single(platform.Drafts.Where(d => d.OwnerId == B));
    }

    // An unset tenant is the shape of a bug — a request that forgot to say who it is for. It must
    // read as "nothing", never as "everything".
    [Fact]
    public void An_unresolved_tenant_sees_nothing_rather_than_everything()
    {
        using var connection = Seeded();
        using var db = Open(connection, new TenantProvider());

        Assert.Empty(db.Drafts.ToList());
        Assert.Empty(db.Assets.ToList());
        Assert.Empty(db.Channels.ToList());
    }

    [Fact]
    public void Platform_access_sees_every_tenant()
    {
        using var connection = Seeded();
        using var db = AsPlatform(connection);

        Assert.Equal(2, db.Drafts.Count());
        Assert.Equal(2, db.Assets.Count());
        Assert.Equal(2, db.Channels.Count());
    }

    // Identity reads AspNetUsers on every authorized request, and sign-in happens before anyone
    // knows which tenant the request is for. A filter here would lock everybody out.
    [Fact]
    public void Accounts_themselves_are_never_filtered()
    {
        using var connection = SharedDatabase();
        using (var platform = AsPlatform(connection))
        {
            platform.Users.Add(new ApplicationUser { Id = A, UserName = "a@x.test", Email = "a@x.test" });
            platform.Users.Add(new ApplicationUser { Id = B, UserName = "b@x.test", Email = "b@x.test" });
            platform.SaveChanges();
        }

        using var db = AsTenant(connection, A);
        Assert.Equal(2, db.Users.Count());
    }

    // The rows that hang off a draft rather than carrying an owner of their own. Every one of them
    // is reachable by DraftId alone in the endpoint files, so an owner filter on Draft does not
    // cover them — the id is the only thing in the way, and an id is not a permission.
    [Fact]
    public void A_drafts_children_are_scoped_with_it()
    {
        using var connection = Seeded();

        Guid bsDraft;
        using (var platform = AsPlatform(connection))
        {
            bsDraft = platform.Drafts.Single(d => d.OwnerId == B).Id;
            var asDraft = platform.Drafts.Single(d => d.OwnerId == A).Id;

            platform.DraftTranslations.Add(new DraftTranslation { DraftId = bsDraft, OwnerId = B, Language = "en", Title = "B" });
            platform.DraftTranslations.Add(new DraftTranslation { DraftId = asDraft, OwnerId = A, Language = "en", Title = "A" });
            platform.Comments.Add(new Comment { DraftId = bsDraft, OwnerId = B, Text = "B's reader" });
            platform.Reactions.Add(new Reaction { DraftId = bsDraft, OwnerId = B, Kind = "like" });
            platform.PostRegistrations.Add(new PostRegistration { DraftId = bsDraft, OwnerId = B, Email = "reader@x.test" });
            platform.DraftRevisions.Add(new DraftRevision { DraftId = bsDraft, OwnerId = B, Title = "B" });
            platform.SaveChanges();
        }

        using var db = AsTenant(connection, A);

        Assert.Empty(db.DraftTranslations.Where(t => t.DraftId == bsDraft).ToList());
        Assert.Empty(db.Comments.Where(c => c.DraftId == bsDraft).ToList());
        Assert.Empty(db.Reactions.Where(r => r.DraftId == bsDraft).ToList());
        Assert.Empty(db.PostRegistrations.Where(r => r.DraftId == bsDraft).ToList());
        Assert.Empty(db.DraftRevisions.Where(r => r.DraftId == bsDraft).ToList());

        Assert.Single(db.DraftTranslations.ToList());
    }

    [Fact]
    public void A_channels_children_are_scoped_with_it()
    {
        using var connection = Seeded();

        Guid bsChannel;
        using (var platform = AsPlatform(connection))
        {
            bsChannel = platform.Channels.Single(c => c.OwnerId == B).Id;
            platform.ChannelStatSnapshots.Add(new ChannelStatSnapshot { ChannelId = bsChannel, OwnerId = B, MemberCount = 99 });
            platform.ChannelPosts.Add(new ChannelPost
            {
                ChannelId = bsChannel, OwnerId = B,
                DraftId = platform.Drafts.Single(d => d.OwnerId == B).Id, TelegramMessageId = 7,
            });
            platform.SaveChanges();
        }

        using var db = AsTenant(connection, A);
        Assert.Empty(db.ChannelStatSnapshots.Where(s => s.ChannelId == bsChannel).ToList());
        Assert.Empty(db.ChannelPosts.Where(p => p.ChannelId == bsChannel).ToList());
    }

    // A blog host used to be given a platform scope — a public surface with no owner filter in its
    // model at all. It is one account's blog now, and it is scoped like one.
    private static (HttpContext Ctx, TenantProvider Tenant, TenantContext Resolved, bool[] Reached) BlogHostRequest(
        Microsoft.Data.Sqlite.SqliteConnection connection, string path = "/")
    {
        var tenant = new TenantProvider();
        var services = new ServiceCollection();
        services.AddSingleton(AsPlatform(connection));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Host = new HostString($"martycow.{Consts.URLs.TenantHost}");
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();

        // What TenantResolutionMiddleware writes for a subdomain.
        var resolved = new TenantContext();
        resolved.Resolve("martycow", A);

        return (ctx, tenant, resolved, new bool[1]);
    }

    private static Task RunScope(HttpContext ctx, TenantProvider tenant, TenantContext resolved, bool[] reached) =>
        new TenantScopeMiddleware(_ => { reached[0] = true; return Task.CompletedTask; })
            .InvokeAsync(ctx, tenant, resolved);

    [Fact]
    public async Task The_blog_host_is_scoped_to_its_owner_and_not_to_the_platform()
    {
        using var connection = Seeded();
        using (var platform = AsPlatform(connection))
        {
            platform.Users.Single(u => u.Id == A).IsAdmin = true;
            platform.SaveChanges();
        }

        var (ctx, tenant, resolved, reached) = BlogHostRequest(connection);
        await RunScope(ctx, tenant, resolved, reached);

        Assert.True(reached[0]);
        Assert.Equal(A, tenant.TenantId);
        Assert.False(tenant.IsPlatform);
    }

}
