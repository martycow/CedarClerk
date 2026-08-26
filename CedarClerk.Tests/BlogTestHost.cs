using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// Shared harness for driving BlogEndpoints.HandleRequest end to end: in-memory SQLite plus a
// DefaultHttpContext whose response body can be read back. The database is a *platform* context on
// purpose — no owner filter is in the model at all, so a page that renders only its owner's rows
// does so because the query said so, which is the thing worth testing.
internal static class BlogTestHost
{
    public static HttpContext Request(
        string method, string path, CedarDbContext db, string query = "",
        string? host = null, string? tenantOwnerId = null,
        Dictionary<string, string?>? config = null, string? showcaseDomainSlug = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? []).Build());
        services.AddDataProtection();
        services.AddSingleton<PrivateAccess>();
        // The follow paths reach for a mailer and a logger. Unconfigured, Resend logs and skips —
        // which is exactly the shape a test wants: the row is written, nothing leaves the machine.
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<CedarClerk.Server.Email.ResendEmailProvider>();

        // What TenantResolutionMiddleware would have written. A blog only ever renders for a
        // resolved subdomain now, so a test that names no owner gets the database's first account —
        // which is what every single-owner fixture here means.
        var owner = db.Users.OrderBy(u => u.Id).FirstOrDefault();
        var ownerId = tenantOwnerId ?? owner?.Id;
        var username = (tenantOwnerId is not null ? null : owner?.TenantUsername) ?? "tenant";
        host ??= $"{username}.{Consts.URLs.TenantHost}";

        var tenant = new TenantContext();
        // T-300 — a custom domain resolves to an owner and the showcase that answers at its root,
        // with no username in the host to read.
        if (showcaseDomainSlug is not null && ownerId is not null)
            tenant.ResolveShowcaseDomain(ownerId, showcaseDomainSlug);
        else if (ownerId is not null)
            tenant.Resolve(host.Split('.')[0], ownerId);
        services.AddSingleton(tenant);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (query.Length > 0)
            ctx.Request.QueryString = new QueryString(query);
        ctx.Request.Host = new HostString(host);
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    public static CedarDbContext EmptyDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options, TenantProvider.Platform());
        db.Database.EnsureCreated();
        return db;
    }

    /// The account the legacy blog host resolves to: the one admin. Every blog page needs one.
    public static CedarDbContext WithOwner(this CedarDbContext db, string id = "o1")
    {
        db.Users.Add(new ApplicationUser { Id = id, UserName = id, IsAdmin = true });
        db.SaveChanges();
        return db;
    }

    public static string Body(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
