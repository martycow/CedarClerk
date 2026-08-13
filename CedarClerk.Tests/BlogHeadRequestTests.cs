using CedarClerk.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// 13.08.2026: the blog answered 404 to HEAD while answering 200 to GET, so the first uptime monitor
// pointed at it reported the site as down from the minute it was created. A monitor that cries wolf
// on day one is worse than no monitor — it teaches you to ignore it.
public class BlogHeadRequestTests
{
    private static HttpContext Request(string method, string path, CedarDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Host = new HostString("blog.mooexe.dev");
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static CedarDbContext EmptyDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new CedarDbContext(new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("GET")]
    public async Task The_index_answers_reads(string method)
    {
        using var db = EmptyDatabase();
        var ctx = Request(method, "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        Assert.NotEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task A_write_to_a_page_is_still_refused()
    {
        using var db = EmptyDatabase();
        var ctx = Request("POST", "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }
}
