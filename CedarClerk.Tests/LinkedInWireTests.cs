using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Server.Publishing;

namespace CedarClerk.Tests;

// The request bodies LinkedIn's versioned Posts API accepts, pinned without a network: a single
// picture is `media`, several are `multiImage`, a link with no picture is an `article` card, and
// the four fixed fields every post must carry are present.
public class LinkedInWireTests
{
    [Fact]
    public void A_post_carries_the_fixed_fields_the_api_requires()
    {
        var payload = LinkedInPublishTarget.BuildPayload("urn:li:person:abc", "Hello", null);

        Assert.Equal("urn:li:person:abc", (string?)payload["author"]);
        Assert.Equal("Hello", (string?)payload["commentary"]);
        Assert.Equal("PUBLIC", (string?)payload["visibility"]);
        Assert.Equal("MAIN_FEED", (string?)payload["distribution"]?["feedDistribution"]);
        Assert.Equal("PUBLISHED", (string?)payload["lifecycleState"]);
        Assert.False((bool?)payload["isReshareDisabledByAuthor"]);
        Assert.Null(payload["content"]);
    }

    [Fact]
    public void One_picture_is_media_and_several_are_multiImage_with_their_alt_text()
    {
        var single = LinkedInPublishTarget.ImageContent([("urn:li:image:1", "A sprite")]);
        Assert.Equal("urn:li:image:1", (string?)single["media"]?["id"]);
        Assert.Equal("A sprite", (string?)single["media"]?["altText"]);

        var several = LinkedInPublishTarget.ImageContent([("urn:li:image:1", null), ("urn:li:image:2", "Two")]);
        var images = Assert.IsType<JsonArray>(several["multiImage"]?["images"]);
        Assert.Equal(2, images.Count);
        Assert.Null(images[0]?["altText"]);
        Assert.Equal("Two", (string?)images[1]?["altText"]);
    }

    [Fact]
    public void An_article_card_names_the_page_and_takes_the_thumbnail_only_when_there_is_one()
    {
        var with = LinkedInPublishTarget.ArticleContent("https://blog/p", "Title", "Desc", "urn:li:image:9");
        Assert.Equal("https://blog/p", (string?)with["article"]?["source"]);
        Assert.Equal("urn:li:image:9", (string?)with["article"]?["thumbnail"]);

        var without = LinkedInPublishTarget.ArticleContent("https://blog/p", "Title", null, null);
        Assert.Null(without["article"]?["thumbnail"]);
        Assert.Null(without["article"]?["description"]);
    }

    [Fact]
    public void The_public_url_is_the_feed_update_page_for_either_urn_kind()
    {
        Assert.Equal("https://www.linkedin.com/feed/update/urn:li:share:1/", LinkedInPublishTarget.PublicUrl("urn:li:share:1"));
        Assert.Equal("https://www.linkedin.com/feed/update/urn:li:ugcPost:2/", LinkedInPublishTarget.PublicUrl("urn:li:ugcPost:2"));
    }

    [Fact]
    public void The_capabilities_say_one_post_no_threads_twenty_pictures()
    {
        var caps = new LinkedInPublishTarget(null!, null!, null!, null!, null!, null!).Capabilities;

        Assert.Equal(PublishNetworks.LinkedIn, caps.Network);
        Assert.Equal(LinkedInPostBuilder.MaxChars, caps.MaxCharacters);
        Assert.Equal(20, caps.MaxMediaItems);
        Assert.False(caps.SupportsThreads);
        Assert.True(caps.DerivesShortPost);
        Assert.True(caps.SupportsAltText);
    }
}
