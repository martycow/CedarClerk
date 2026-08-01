using CedarClerk.Core;
using CedarClerk.Server.Publishing;

namespace CedarClerk.Tests;

// ADR-087 — Telegram caches a *failed* media fetch per URL and keeps refusing that exact URL
// after the origin recovers (second incident 01.08.2026: one poisoned URL killed part 3 of a
// thread and held back parts 4–12). Every media URL sent to Telegram therefore carries a
// per-send `?v=` stamp, so each publish is a URL Telegram has never fetched. These tests pin the
// two properties that matter: every media URL in the tree gets the stamp, and nothing that is
// not a media URL is touched.
public class TelegramMediaCacheBusterTests
{
    private static RichRun Text(string text) => new RichRunText(text);

    [Fact]
    public void Every_flat_media_block_gets_the_stamp()
    {
        var photo = (RichPhotoBlock)TelegramPublishTarget.WithMediaCacheBuster(new RichPhotoBlock("https://x/a.jpg", null), "s");
        var video = (RichVideoBlock)TelegramPublishTarget.WithMediaCacheBuster(new RichVideoBlock("https://x/a.mp4", null), "s");
        var audio = (RichAudioBlock)TelegramPublishTarget.WithMediaCacheBuster(new RichAudioBlock("https://x/a.mp3", null), "s");

        Assert.Equal("https://x/a.jpg?v=s", photo.Url);
        Assert.Equal("https://x/a.mp4?v=s", video.Url);
        Assert.Equal("https://x/a.mp3?v=s", audio.Url);
    }

    [Fact]
    public void A_url_that_already_has_a_query_gets_an_ampersand()
    {
        var stamped = (RichPhotoBlock)TelegramPublishTarget.WithMediaCacheBuster(
            new RichPhotoBlock("https://x/a.jpg?w=1", null), "s");

        Assert.Equal("https://x/a.jpg?w=1&v=s", stamped.Url);
    }

    [Fact]
    public void Media_groups_stamp_every_image()
    {
        var stamped = (RichSlideshowBlock)TelegramPublishTarget.WithMediaCacheBuster(
            new RichSlideshowBlock(["https://x/a.jpg", "https://x/b.jpg"]), "s");

        Assert.Equal(["https://x/a.jpg?v=s", "https://x/b.jpg?v=s"], stamped.Urls);
    }

    [Fact]
    public void Media_nested_in_quotes_details_and_lists_is_reached()
    {
        var quote = (RichQuoteBlock)TelegramPublishTarget.WithMediaCacheBuster(
            new RichQuoteBlock([new RichPhotoBlock("https://x/q.jpg", null)]), "s");
        var details = (RichDetailsBlock)TelegramPublishTarget.WithMediaCacheBuster(
            new RichDetailsBlock(Text("more"), [new RichPhotoBlock("https://x/d.jpg", null)], false), "s");
        var list = (RichListBlock)TelegramPublishTarget.WithMediaCacheBuster(
            new RichListBlock([new RichListItem([new RichPhotoBlock("https://x/l.jpg", null)], false, false, null)]), "s");

        Assert.Equal("https://x/q.jpg?v=s", ((RichPhotoBlock)quote.Blocks[0]).Url);
        Assert.Equal("https://x/d.jpg?v=s", ((RichPhotoBlock)details.Blocks[0]).Url);
        Assert.Equal("https://x/l.jpg?v=s", ((RichPhotoBlock)list.Items[0].Blocks[0]).Url);
    }

    [Fact]
    public void Text_blocks_pass_through_untouched()
    {
        var para = new RichParagraphBlock(new RichRunLink(Text("a link"), "https://x/page"));

        var result = TelegramPublishTarget.WithMediaCacheBuster(para, "s");

        // Same instance: a link inside text is a reader's link, not a media fetch — stamping it
        // would corrupt what the reader opens.
        Assert.Same(para, result);
    }
}
