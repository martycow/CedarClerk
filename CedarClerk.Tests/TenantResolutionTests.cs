using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// The middleware end to end: Host header in, either a populated TenantContext or a 404 out.
// A 404 and never an exception — an address nobody registered is a missing page, not a fault.
public class TenantResolutionTests
{
    private const string Domain = "cedarclerk.app";

    private sealed class Harness
    {
        public required CedarDbContext Db { get; init; }
        public required HttpContext Context { get; init; }
        public required TenantContext Tenant { get; init; }
        public bool NextCalled { get; set; }
    }

    private static Harness Build(string host)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();

        var tenant = new TenantContext();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(tenant);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = "/";
        ctx.Response.Body = new MemoryStream();

        return new Harness { Db = db, Context = ctx, Tenant = tenant };
    }

    private static async Task<Harness> RunAsync(string host, Action<CedarDbContext>? seed = null)
    {
        var harness = Build(host);
        seed?.Invoke(harness.Db);

        var middleware = new TenantResolutionMiddleware(
            _ => { harness.NextCalled = true; return Task.CompletedTask; }, Domain);
        await middleware.InvokeAsync(harness.Context);

        return harness;
    }

    private static void SeedUser(CedarDbContext db, string username)
    {
        db.Users.Add(new ApplicationUser
        {
            UserName = $"{username}@example.test",
            Email = $"{username}@example.test",
            TenantUsername = username,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task An_existing_tenant_is_resolved_and_the_request_continues()
    {
        var harness = await RunAsync("marty.cedarclerk.app", db => SeedUser(db, "marty"));

        Assert.True(harness.NextCalled);
        Assert.True(harness.Tenant.IsTenantRequest);
        Assert.Equal("marty", harness.Tenant.Username);
        Assert.NotNull(harness.Tenant.OwnerId);
        Assert.Equal(StatusCodes.Status200OK, harness.Context.Response.StatusCode);
    }

    [Fact]
    public async Task The_resolved_owner_is_the_account_that_holds_the_name()
    {
        var harness = await RunAsync("marty.cedarclerk.app", db =>
        {
            SeedUser(db, "sasha");
            SeedUser(db, "marty");
        });

        var marty = harness.Db.Users.Single(u => u.TenantUsername == "marty");
        Assert.Equal(marty.Id, harness.Tenant.OwnerId);
    }

    [Fact]
    public async Task An_unknown_tenant_is_404_and_the_request_stops()
    {
        var harness = await RunAsync("nobody.cedarclerk.app");

        Assert.False(harness.NextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, harness.Context.Response.StatusCode);
        Assert.False(harness.Tenant.IsTenantRequest);
    }

    [Fact]
    public async Task An_invalid_name_is_404_and_never_reaches_the_database()
    {
        var harness = await RunAsync("-nobody.cedarclerk.app");

        Assert.False(harness.NextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, harness.Context.Response.StatusCode);
    }

    [Fact]
    public async Task A_reserved_subdomain_passes_through_as_an_ordinary_request()
    {
        var harness = await RunAsync("www.cedarclerk.app", db => SeedUser(db, "marty"));

        Assert.True(harness.NextCalled);
        Assert.False(harness.Tenant.IsTenantRequest);
        Assert.Null(harness.Tenant.Username);
    }

    [Fact]
    public async Task The_apex_passes_through_as_an_ordinary_request()
    {
        var harness = await RunAsync("cedarclerk.app");

        Assert.True(harness.NextCalled);
        Assert.False(harness.Tenant.IsTenantRequest);
    }

    [Theory]
    [InlineData("cedarclerk.mooexe.dev")]
    [InlineData("blog.mooexe.dev")]
    [InlineData("localhost")]
    public async Task Hosts_outside_the_tenant_domain_are_untouched(string host)
    {
        var harness = await RunAsync(host);

        Assert.True(harness.NextCalled);
        Assert.False(harness.Tenant.IsTenantRequest);
        Assert.Equal(StatusCodes.Status200OK, harness.Context.Response.StatusCode);
    }

    [Fact]
    public async Task A_tenant_reached_in_the_wrong_case_is_the_same_tenant()
    {
        var harness = await RunAsync("MARTY.cedarclerk.app", db => SeedUser(db, "marty"));

        Assert.True(harness.NextCalled);
        Assert.Equal("marty", harness.Tenant.Username);
    }

    // A stored name is always canonical, so a row that somehow holds one in the wrong case must
    // not become reachable at a host nobody can type.
    [Fact]
    public async Task A_name_with_a_dot_inside_is_never_a_tenant()
    {
        var harness = await RunAsync("marty.blog.cedarclerk.app", db => SeedUser(db, "marty"));

        Assert.False(harness.NextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, harness.Context.Response.StatusCode);
    }

    [Fact]
    public async Task A_port_on_the_host_does_not_change_the_answer()
    {
        var harness = await RunAsync("marty.cedarclerk.app:8080", db => SeedUser(db, "marty"));

        Assert.True(harness.NextCalled);
        Assert.Equal("marty", harness.Tenant.Username);
    }

    [Fact]
    public async Task Accounts_without_a_name_are_not_reachable_at_any_subdomain()
    {
        var harness = await RunAsync("marty.cedarclerk.app", db =>
        {
            db.Users.Add(new ApplicationUser { UserName = "m@example.test", Email = "m@example.test" });
            db.SaveChanges();
        });

        Assert.False(harness.NextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, harness.Context.Response.StatusCode);
    }
}
