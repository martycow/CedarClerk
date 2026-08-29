using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// Wave 1 item 1 — the OG card's URL/hash contract and the renderer itself. Rendering here is what
// proves the embedded woff2 fonts actually load: a font problem fails dotnet test, not production.
public class OgImageServiceTests
{
    [Fact]
    public void Hash_is_deterministic_and_eight_hex_chars()
    {
        var first = OgImageEndpoint.Hash8("My post", "My blog");
        Assert.Equal(first, OgImageEndpoint.Hash8("My post", "My blog"));
        Assert.Matches("^[0-9a-f]{8}$", first);
    }

    [Fact]
    public void Hash_changes_with_title_and_site_name()
    {
        var baseline = OgImageEndpoint.Hash8("My post", "My blog");
        Assert.NotEqual(baseline, OgImageEndpoint.Hash8("My post edited", "My blog"));
        Assert.NotEqual(baseline, OgImageEndpoint.Hash8("My post", "Another blog"));
    }

    [Fact]
    public void ImageUrl_is_absolute_on_the_blog_host_with_the_version_hash()
    {
        var site = new BlogSite("o1", "marty.cedarclerk.app");
        var url = OgImageEndpoint.ImageUrl(site, "my-post", "My post", "My blog");
        Assert.Equal($"https://marty.cedarclerk.app/og/my-post.png?v={OgImageEndpoint.Hash8("My post", "My blog")}", url);
    }

    [Theory]
    [InlineData("A short title", "My blog")]
    [InlineData("Заголовок по-русски про маяк и гавань", "Блог Марти")]
    [InlineData("A very long title that has to wrap across several lines and finally run out of room entirely, ending in an ellipsis because four lines is all the card has space for on it", "My blog")]
    public void Card_renders_as_a_png(string title, string siteName)
    {
        var bytes = OgImageEndpoint.RenderCard(title, siteName);

        // PNG signature, and enough bytes that something was actually drawn.
        Assert.True(bytes.Length > 2000, $"Card is suspiciously small: {bytes.Length} bytes");
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);

        using var image = SixLabors.ImageSharp.Image.Load(bytes);
        Assert.Equal(OgImageEndpoint.Width, image.Width);
        Assert.Equal(OgImageEndpoint.Height, image.Height);
    }

    [Fact]
    public void Card_is_deterministic_for_the_same_inputs()
    {
        Assert.Equal(OgImageEndpoint.RenderCard("Same", "Blog"), OgImageEndpoint.RenderCard("Same", "Blog"));
    }

    [Fact]
    public async Task Endpoint_serves_a_png_with_caching_headers_for_a_published_post()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Hello world", BlogSlug = "hello", IsBlogPublished = true });
        db.SaveChanges();

        var ctx = BlogTestHost.Request("GET", "/og/hello.png", db);
        await OgImageEndpoint.HandleAsync(ctx, db, new BlogSite("o1", "tenant.cedarclerk.app"), "hello.png");

        Assert.Equal("image/png", ctx.Response.ContentType);
        Assert.Contains("max-age=86400", ctx.Response.Headers.CacheControl.ToString());
        Assert.False(string.IsNullOrEmpty(ctx.Response.Headers.ETag.ToString()));
        ctx.Response.Body.Position = 0;
        var head = new byte[4];
        Assert.Equal(4, ctx.Response.Body.Read(head));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], head);
    }

    [Fact]
    public async Task Endpoint_404s_for_unknown_unpublished_and_unlisted_private_slugs()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft { OwnerId = "o1", Title = "Draft only", BlogSlug = "someday" });
        db.Drafts.Add(new Draft
        {
            OwnerId = "o1", Title = "Hidden", BlogSlug = "hidden",
            IsBlogPublished = true, IsPrivate = true,
        });
        db.Drafts.Add(new Draft
        {
            OwnerId = "o1", Title = "Listed private", BlogSlug = "listed",
            IsBlogPublished = true, IsPrivate = true, IsListedWhilePrivate = true,
        });
        db.SaveChanges();

        var site = new BlogSite("o1", "tenant.cedarclerk.app");
        foreach (var file in new[] { "nope.png", "someday.png", "hidden.png", "not-even-png" })
        {
            var ctx = BlogTestHost.Request("GET", $"/og/{file}", db);
            await OgImageEndpoint.HandleAsync(ctx, db, site, file);
            Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        }

        // A listed-while-private post already shows its title to crawlers, so it gets a card.
        var listed = BlogTestHost.Request("GET", "/og/listed.png", db);
        await OgImageEndpoint.HandleAsync(listed, db, site, "listed.png");
        Assert.Equal("image/png", listed.Response.ContentType);
    }
}
