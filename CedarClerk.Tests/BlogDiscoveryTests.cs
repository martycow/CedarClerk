using CedarClerk.Server;

namespace CedarClerk.Tests;

public class BlogDiscoveryTests
{
    private const string Doc = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Private body marker"}]},{"type":"image","attrs":{"src":"/media/private-cover.png"}}]}""";

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public async Task Only_public_cards_expose_content_previews(bool isPrivate, bool isListed, bool previewVisible)
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft
        {
            OwnerId = "o1", Title = "A post", BlogSlug = "a-post", CedarJson = Doc,
            IsBlogPublished = true, IsPrivate = isPrivate, IsListedWhilePrivate = isListed,
            BlogPublishedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        var ctx = BlogTestHost.Request("GET", "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        var html = BlogTestHost.Body(ctx);
        Assert.Equal(previewVisible, html.Contains("src=\"/media/private-cover.png\""));
        Assert.Equal(previewVisible, html.Contains("Private body marker"));
        Assert.Equal(!isPrivate || isListed, html.Contains("href=\"/a-post?lang=en\""));
    }

    [Fact]
    public async Task The_index_does_not_load_external_images_as_thumbnails()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Drafts.Add(new Draft
        {
            OwnerId = "o1", Title = "External image", BlogSlug = "external", IsBlogPublished = true,
            CedarJson = Doc.Replace("/media/private-cover.png", "https://outside.example/tracker.png"),
        });
        db.SaveChanges();
        var ctx = BlogTestHost.Request("GET", "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        Assert.DoesNotContain("https://outside.example/tracker.png", BlogTestHost.Body(ctx));
    }

    [Fact]
    public async Task A_translated_card_opens_the_language_it_previews()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        var draft = new Draft
        {
            OwnerId = "o1", Title = "Original title", BlogSlug = "translated", CedarJson = Doc,
            PrimaryLanguage = "ru", IsBlogPublished = true,
        };
        db.Drafts.Add(draft);
        db.DraftTranslations.Add(new DraftTranslation
        {
            OwnerId = "o1", DraftId = draft.Id, Language = "en", Title = "Translated title", CedarJson = Doc.Replace("Private body marker", "English preview"),
        });
        db.SaveChanges();
        var ctx = BlogTestHost.Request("GET", "/", db);

        await BlogEndpoints.HandleRequest(ctx);

        var html = BlogTestHost.Body(ctx);
        Assert.Contains("Translated title", html);
        Assert.Contains("English preview", html);
        Assert.Contains("href=\"/translated?lang=en\"", html);
        Assert.Contains("name=\"lang\" value=\"en\"", html);
    }
}
