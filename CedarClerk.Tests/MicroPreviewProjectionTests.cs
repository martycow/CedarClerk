using CedarClerk.Core;

namespace CedarClerk.Tests;

// ADR-239 clause 10 for the short-post networks: the card is the builder's post and the thread is
// the splitter's parts — the projection composes, it never measures or cuts on its own.
public class MicroPreviewProjectionTests
{
    private const string Doc = """
        {"type":"doc","content":[
          {"type":"paragraph","content":[{"type":"text","text":"Coyote vs ACME opens tonight."}]},
          {"type":"image","attrs":{"src":"/media/coyote.jpg","alt":"Coyote"}},
          {"type":"image","attrs":{"src":"https://elsewhere.test/acme.png"}},
          {"type":"paragraph","content":[{"type":"text","text":"Bring popcorn."}]}
        ]}
        """;

    private static readonly PublishCapabilities X = new()
    {
        Network = PublishNetworks.X, MaxCharacters = XPostBuilder.MaxWeightedChars, MaxMediaItems = 0,
        SupportsThreads = true, DerivesShortPost = true,
    };

    private static readonly PublishCapabilities Bluesky = new()
    {
        Network = PublishNetworks.Bluesky, MaxCharacters = BlueskyPostBuilder.MaxGraphemes, MaxMediaItems = 4,
        SupportsThreads = true, DerivesShortPost = true,
    };

    private static readonly PublishCapabilities Discord = new()
    {
        Network = PublishNetworks.Discord, MaxCharacters = DiscordPostBuilder.MaxChars, MaxMediaItems = 0,
        SupportsThreads = false, DerivesShortPost = true,
    };

    [Fact]
    public void Single_is_the_teaser_plus_the_link_measured_the_networks_way()
    {
        var preview = MicroPreviewProjection.Project(PublishNetworks.X, "en", Doc, null, "https://blog.test/coyote", XPostBuilder.UrlWeight + 2, X);

        var expected = XPostBuilder.Build(null, Doc, "https://blog.test/coyote");
        Assert.Equal(expected.Text, preview.Single.Text);
        Assert.Equal(expected.WeightedLength, preview.Single.Length);
        Assert.Equal("https://blog.test/coyote", preview.Single.LinkUrl);
        Assert.Equal(XPostBuilder.MaxWeightedChars, preview.MaxLength);
        Assert.False(preview.HasAuthorText);
    }

    [Fact]
    public void The_authors_own_text_replaces_the_teaser()
    {
        var preview = MicroPreviewProjection.Project(PublishNetworks.Bluesky, "en", Doc, "Go see it.", null, 0, Bluesky);

        Assert.Equal("Go see it.", preview.Single.Text);
        Assert.True(preview.HasAuthorText);
        Assert.Null(preview.Single.LinkUrl);
    }

    [Fact]
    public void Only_local_images_are_attached_and_only_where_the_network_takes_them()
    {
        var bluesky = MicroPreviewProjection.Project(PublishNetworks.Bluesky, "en", Doc, null, null, 0, Bluesky);
        var x = MicroPreviewProjection.Project(PublishNetworks.X, "en", Doc, null, null, 0, X);

        Assert.Equal(["/media/coyote.jpg"], bluesky.Single.ImageUrls);
        Assert.Empty(x.Single.ImageUrls);
    }

    [Fact]
    public void Thread_parts_are_the_splitters_with_the_link_on_the_last_and_pictures_on_the_first()
    {
        var longDoc = """{"type":"doc","content":[""" + string.Join(",", Enumerable.Range(0, 6).Select(i =>
            $$$"""{"type":"paragraph","content":[{"type":"text","text":"Paragraph {{{i}}} of a story long enough to leave one post behind and become a thread of several."}]}"""))
            + """,{"type":"image","attrs":{"src":"/media/coyote.jpg"}}]}""";
        var url = "https://blog.test/coyote";

        var preview = MicroPreviewProjection.Project(PublishNetworks.Bluesky, "en", longDoc, null, url,
            BlueskyPostBuilder.GraphemeCount(url) + 2, Bluesky);

        var parts = MicroThreadSplitter.Split(longDoc, PublishNetworks.Bluesky, BlueskyPostBuilder.GraphemeCount(url) + 2);
        Assert.True(parts.Count > 1);
        Assert.Equal(parts.Count, preview.Thread.Count);
        Assert.Equal(parts[0], preview.Thread[0].Text);
        Assert.Equal($"{parts[^1]}\n\n{url}", preview.Thread[^1].Text);
        Assert.Equal(url, preview.Thread[^1].LinkUrl);
        Assert.All(preview.Thread, p => Assert.True(p.Length <= BlueskyPostBuilder.MaxGraphemes));
        Assert.Equal(["/media/coyote.jpg"], preview.Thread[0].ImageUrls);
        Assert.Empty(preview.Thread[1].ImageUrls);
    }

    [Fact]
    public void A_network_that_never_threads_gets_no_thread()
    {
        var preview = MicroPreviewProjection.Project(PublishNetworks.Discord, "en", Doc, null, null, 0, Discord);

        Assert.False(preview.SupportsThreads);
        Assert.Empty(preview.Thread);
        Assert.Equal(DiscordPostBuilder.Build(null, Doc, null), preview.Single.Text);
    }
}
