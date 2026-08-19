using CedarClerk.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// Shared harness for driving BlogEndpoints.HandleRequest end to end: in-memory SQLite plus a
// DefaultHttpContext whose response body can be read back. IConfiguration is registered empty on
// purpose — the endpoints then fall back to the Consts hosts, which keeps asserted URLs
// deterministic on any machine.
internal static class BlogTestHost
{
    public static HttpContext Request(string method, string path, CedarDbContext db, string query = "")
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDataProtection();
        services.AddSingleton<PrivateAccess>();

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (query.Length > 0)
            ctx.Request.QueryString = new QueryString(query);
        ctx.Request.Host = new HostString("blog.mooexe.dev");
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    public static CedarDbContext EmptyDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    public static string Body(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
