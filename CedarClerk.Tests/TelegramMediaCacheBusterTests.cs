using CedarClerk.Server.Publishing;

namespace CedarClerk.Tests;

// ADR-087/ADR-088 — media on this server's own disk is uploaded to Telegram as bytes; anything
// Telegram still fetches by URL carries a per-send `?v=` stamp so its negative cache (a *failed*
// fetch cached per URL — two incidents, 16.07 and 01.08.2026) cannot outlive an incident. These
// tests pin the two pure pieces: which URLs count as "our media on disk", and how a stamp lands.
public class TelegramMediaCacheBusterTests
{
    [Theory]
    [InlineData("https://cedarclerk.mooexe.dev/media/asset_a.jpg", "asset_a.jpg")]
    [InlineData("cedarclerk.mooexe.dev/media/asset_a.jpg", "asset_a.jpg")]
    [InlineData("/media/asset_a.jpg", "asset_a.jpg")]
    [InlineData("https://cedarclerk.mooexe.dev/media/asset_a.jpg?v=123", "asset_a.jpg")]
    public void A_url_into_our_media_directory_yields_its_filename(string url, string expected)
    {
        Assert.True(TelegramPublishTarget.TryLocalMediaFileName(url, out var fileName));
        Assert.Equal(expected, fileName);
    }

    [Theory]
    [InlineData("https://img.youtube.com/vi/abc/hqdefault.jpg")]     // foreign host, no /media/
    [InlineData("https://example.com/media/")]                        // empty name
    [InlineData("https://example.com/media/../secrets.txt")]          // traversal
    [InlineData("https://example.com/media/sub/dir.jpg")]             // nested path — not ours
    public void A_foreign_or_malformed_url_is_left_alone(string url)
    {
        Assert.False(TelegramPublishTarget.TryLocalMediaFileName(url, out _));
    }

    [Fact]
    public void A_stamp_is_appended_as_query_or_ampersand()
    {
        Assert.Equal("https://x/a.jpg?v=s", TelegramPublishTarget.StampUrl("https://x/a.jpg", "s"));
        Assert.Equal("https://x/a.jpg?w=1&v=s", TelegramPublishTarget.StampUrl("https://x/a.jpg?w=1", "s"));
    }
}
