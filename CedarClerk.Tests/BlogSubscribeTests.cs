using System.Text;
using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// Wave 1 item 7 — the blog-wide subscription's double opt-in, driven through the real
// HandleRequest: a row is never confirmed by asking, only by the mailed token, and leaving
// deletes the row rather than flagging it.
public class BlogSubscribeTests
{
    private static HttpContext FormPost(CedarDbContext db, string form, string? tenantOwnerId = null)
    {
        var ctx = BlogTestHost.Request("POST", "/subscribe", db, tenantOwnerId: tenantOwnerId);
        var bytes = Encoding.UTF8.GetBytes(form);
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        return ctx;
    }

    private static string Location(HttpContext ctx) => ctx.Response.Headers.Location.ToString();

    [Fact]
    public async Task Subscribing_writes_an_unconfirmed_row_and_redirects_back()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var ctx = FormPost(db, "email=Reader%40Example.com&back=/my-post");

        await BlogEndpoints.HandleRequest(ctx);

        Assert.Equal(StatusCodes.Status303SeeOther, ctx.Response.StatusCode);
        Assert.StartsWith("/my-post?subscribe=sent", Location(ctx));

        var row = Assert.Single(db.BlogSubscribers);
        Assert.Equal("reader@example.com", row.Email);
        Assert.Equal("o1", row.OwnerId);
        Assert.Null(row.ConfirmedAt);
        Assert.NotNull(row.ConfirmToken);
        Assert.NotEmpty(row.UnsubscribeToken);
    }

    [Fact]
    public async Task The_mailed_token_confirms_and_is_single_use()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.BlogSubscribers.Add(new BlogSubscriber
        {
            OwnerId = "o1", Email = "reader@example.com",
            ConfirmToken = "tok-1", UnsubscribeToken = "bye-1",
        });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/subscribe/confirm", db, query: "?token=tok-1");
        await BlogEndpoints.HandleRequest(ctx);

        Assert.StartsWith("/?subscribe=confirmed", Location(ctx));
        var row = Assert.Single(db.BlogSubscribers);
        Assert.NotNull(row.ConfirmedAt);
        Assert.Null(row.ConfirmToken);
    }

    [Fact]
    public async Task Leaving_deletes_the_row()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.BlogSubscribers.Add(new BlogSubscriber
        {
            OwnerId = "o1", Email = "reader@example.com",
            ConfirmedAt = DateTime.UtcNow, UnsubscribeToken = "bye-1",
        });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/subscribe/leave", db, query: "?token=bye-1");
        await BlogEndpoints.HandleRequest(ctx);

        Assert.StartsWith("/?subscribe=left", Location(ctx));
        Assert.Equal(0, db.BlogSubscribers.Count());
    }

    [Fact]
    public async Task A_confirmed_address_asking_again_is_told_so()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.BlogSubscribers.Add(new BlogSubscriber
        {
            OwnerId = "o1", Email = "reader@example.com",
            ConfirmedAt = DateTime.UtcNow, UnsubscribeToken = "bye-1",
        });
        db.SaveChanges();

        var ctx = FormPost(db, "email=reader@example.com&back=/");
        await BlogEndpoints.HandleRequest(ctx);

        Assert.Contains("subscribe=already", Location(ctx));
        Assert.Equal(1, db.BlogSubscribers.Count());
    }

    [Fact]
    public async Task A_non_address_writes_nothing()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var ctx = FormPost(db, "email=not-an-email&back=/");

        await BlogEndpoints.HandleRequest(ctx);

        Assert.Contains("subscribe=invalid", Location(ctx));
        Assert.Equal(0, db.BlogSubscribers.Count());
    }

    [Fact]
    public async Task Another_tenants_token_confirms_nothing_here()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Users.Add(new ApplicationUser { Id = "o2", UserName = "o2" });
        db.BlogSubscribers.Add(new BlogSubscriber
        {
            OwnerId = "o2", Email = "reader@example.com",
            ConfirmToken = "tok-2", UnsubscribeToken = "bye-2",
        });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/subscribe/confirm", db,
            query: "?token=tok-2", tenantOwnerId: "o1");
        await BlogEndpoints.HandleRequest(ctx);

        Assert.Contains("subscribe=expired", Location(ctx));
        Assert.Null(db.BlogSubscribers.Single().ConfirmedAt);
    }

    [Fact]
    public async Task The_back_field_cannot_point_off_site()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var ctx = FormPost(db, "email=reader@example.com&back=https://evil.example");

        await BlogEndpoints.HandleRequest(ctx);

        Assert.StartsWith("/?subscribe=", Location(ctx));
    }
}
