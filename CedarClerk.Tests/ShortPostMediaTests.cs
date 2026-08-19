using CedarClerk.Core;

namespace CedarClerk.Tests;

// Marty, 10.08.2026: "images did not go out to Bluesky and X, and the YouTube link did not either."
// Both were silent — nothing failed, the content simply was not there. These pin the two halves.
public class ShortPostMediaTests
{
    private const string YouTubeId = "dQw4w9WgXcQ";

    private static string Doc(string inner) => $"{{\"type\":\"doc\",\"content\":[{inner}]}}";
    private static string Para(string text) => $"{{\"type\":\"paragraph\",\"content\":[{{\"type\":\"text\",\"text\":\"{text}\"}}]}}";
    private static string YouTube(string? caption = null) =>
        caption is null
            ? $"{{\"type\":\"youtube\",\"attrs\":{{\"videoId\":\"{YouTubeId}\"}}}}"
            : $"{{\"type\":\"youtube\",\"attrs\":{{\"videoId\":\"{YouTubeId}\",\"caption\":\"{caption}\"}}}}";

    // ADR-128 — a wikilink's label is a word of the sentence; dropping it silently ate a word
    // from teasers and og:description.
    [Fact]
    public void A_wikilink_label_stays_in_the_plain_text_paragraph()
    {
        var doc = Doc("""{"type":"paragraph","content":[{"type":"text","text":"См. "},{"type":"wikilink","attrs":{"draftId":"11111111-1111-1111-1111-111111111111","label":"Боёвка"}}]}""");

        Assert.Equal("См. Боёвка", Assert.Single(CedarPlainText.Paragraphs(doc)));
    }

    [Fact]
    public void A_youtube_video_reaches_the_short_networks_as_a_link()
    {
        // It used to contribute nothing at all: the blog embeds an iframe and Telegram sends a
        // thumbnail plus a watch link, while Bluesky and X got a post with the video missing.
        var paragraphs = CedarPlainText.Paragraphs(Doc($"{Para("Devlog 14")},{YouTube()}"));

        Assert.Equal(2, paragraphs.Count);
        Assert.Contains($"https://www.youtube.com/watch?v={YouTubeId}", paragraphs[1]);
    }

    [Fact]
    public void The_caption_comes_before_the_link_so_it_reads_as_a_sentence()
    {
        var paragraphs = CedarPlainText.Paragraphs(Doc(YouTube("Ferry terminal timelapse")));
        Assert.Equal($"Ferry terminal timelapse https://www.youtube.com/watch?v={YouTubeId}", paragraphs[0]);
    }

    [Fact]
    public void A_youtube_node_with_no_id_adds_nothing_rather_than_a_broken_link()
    {
        var paragraphs = CedarPlainText.Paragraphs(Doc("{\"type\":\"youtube\",\"attrs\":{}}"));
        Assert.Empty(paragraphs);
    }

    [Fact]
    public void Images_are_collected_in_reading_order_with_their_alt_text()
    {
        var doc = Doc(string.Join(",",
            Para("intro"),
            "{\"type\":\"image\",\"attrs\":{\"src\":\"/media/a.png\",\"alt\":\"A sprite\"}}",
            "{\"type\":\"image\",\"attrs\":{\"src\":\"/media/b.jpg\"}}"));

        var images = CedarImageRefs.Collect(doc);

        Assert.Equal(2, images.Count);
        // Order matters: a network that takes four pictures takes the first four, and "first" has
        // to mean what the reader sees first.
        Assert.Equal("/media/a.png", images[0].Src);
        Assert.Equal("A sprite", images[0].Alt);
        Assert.Null(images[1].Alt);
    }

    [Fact]
    public void A_carousel_contributes_every_picture_inside_it()
    {
        // Its children live in an attrs array rather than as document nodes, so walking the tree
        // alone finds none of them — which is how a gallery post would have gone out empty.
        var doc = Doc("{\"type\":\"carousel\",\"attrs\":{\"images\":["
                      + "{\"src\":\"/media/1.png\",\"alt\":\"one\"},"
                      + "{\"src\":\"/media/2.png\"}]}}");

        var images = CedarImageRefs.Collect(doc);

        Assert.Equal(2, images.Count);
        Assert.Equal("one", images[0].Alt);
    }

    // What the editor actually writes: plain URL strings. Indexing ["src"] into one used to throw
    // and took the whole post page down once OG meta ran the collector on every render (18.08.2026).
    [Fact]
    public void A_collage_of_plain_url_strings_does_not_throw_and_contributes_them_all()
    {
        var doc = Doc("{\"type\":\"collage\",\"attrs\":{\"images\":[\"/media/1.png\",\"/media/2.png\"]}}");

        var images = CedarImageRefs.Collect(doc);

        Assert.Equal(2, images.Count);
        Assert.Equal("/media/1.png", images[0].Src);
        Assert.Null(images[0].Alt);
    }

    [Theory]
    [InlineData("/media/photo.jpg", "photo.jpg")]
    [InlineData("/media/photo.jpg?v=123", "photo.jpg")]
    [InlineData("/MEDIA/photo.jpg", "photo.jpg")]
    public void A_local_reference_resolves_to_its_file_name(string src, string expected) =>
        Assert.Equal(expected, CedarImageRefs.LocalFileName(src));

    [Theory]
    // Nothing this server can upload from disk. A post referencing bytes never sent is a broken
    // picture on somebody's feed, which is worse than no picture.
    [InlineData("https://example.com/photo.jpg")]
    [InlineData("/media/")]
    // And the string becomes a path, so a traversal must not survive it.
    [InlineData("/media/../../secrets.txt")]
    [InlineData("/media/sub/photo.jpg")]
    public void Anything_not_a_plain_local_file_is_refused(string src) =>
        Assert.Null(CedarImageRefs.LocalFileName(src));

    [Fact]
    public void A_network_that_takes_no_media_says_so_rather_than_counting()
    {
        // X declares zero and the validator still reports it, so the export window can say the
        // pictures are not going — which is the part that was missing, not the check.
        var doc = Doc("{\"type\":\"image\",\"attrs\":{\"src\":\"/media/a.png\"}}");
        var x = new PublishCapabilities { Network = PublishNetworks.X, MaxMediaItems = 0, DerivesShortPost = true };

        var issues = PublishValidator.Validate(doc, x);

        var media = Assert.Single(issues, i => i.Code == PublishIssueCodes.TooManyMedia);
        Assert.Equal(1, media.Actual);
        Assert.Equal(0, media.Limit);
    }
}
