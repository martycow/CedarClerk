using CedarClerk.Core;

namespace CedarClerk.Tests;

// ADR-216 — the three pure pieces the showcase's new fields rest on: a trailer is a YouTube link or
// nothing, a gallery is this server's own media paths, and a custom domain is a bare host.
public class YouTubeLinkTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?t=30&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Reads_the_id_out_of_every_shape_people_paste(string url, string expected) =>
        Assert.Equal(expected, YouTubeLink.VideoId(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("javascript:alert(1)")]
    public void Refuses_anything_that_is_not_a_youtube_video(string? url) =>
        Assert.Null(YouTubeLink.VideoId(url));

    // The id lands in an iframe's src, so a value that could close the attribute must not survive.
    [Theory]
    [InlineData("https://youtu.be/abc\"onerror=x")]
    [InlineData("https://www.youtube.com/watch?v=a/../b")]
    public void Refuses_an_id_that_is_not_id_shaped(string url) => Assert.Null(YouTubeLink.VideoId(url));

    [Fact]
    public void Embeds_through_the_nocookie_host_the_renderer_uses() =>
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ",
            YouTubeLink.EmbedUrl("https://youtu.be/dQw4w9WgXcQ"));
}

public class ShowcaseGalleryTests
{
    [Fact]
    public void Keeps_media_paths_in_order()
    {
        var images = ShowcaseGallery.Parse("/media/a.png\n/media/b.png");

        Assert.Equal(["/media/a.png", "/media/b.png"], images);
    }

    [Fact]
    public void Drops_anything_that_is_not_our_own_media()
    {
        var images = ShowcaseGallery.Parse("https://elsewhere.example/a.png\n../secrets\n/media/ok.png\n");

        Assert.Equal(["/media/ok.png"], images);
    }

    [Fact]
    public void Drops_a_traversal_dressed_as_a_media_path() =>
        Assert.Empty(ShowcaseGallery.Parse("/media/../../etc/passwd"));

    [Fact]
    public void Keeps_one_copy_of_a_repeated_path() =>
        Assert.Single(ShowcaseGallery.Parse("/media/a.png\n/media/a.png"));

    [Fact]
    public void Stops_at_the_ceiling()
    {
        var many = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"/media/{i}.png"));

        Assert.Equal(Consts.Showcase.GalleryMaxImages, ShowcaseGallery.Parse(many).Count);
    }
}

public class ShowcaseDomainTests
{
    [Theory]
    [InlineData("mygame.com", "mygame.com")]
    [InlineData("  MyGame.com  ", "mygame.com")]
    [InlineData("https://mygame.com/", "mygame.com")]
    [InlineData("http://www.mygame.com/page", "mygame.com")]
    [InlineData("mygame.com.", "mygame.com")]
    [InlineData("play.mygame.co.uk", "play.mygame.co.uk")]
    public void Reduces_what_people_paste_to_the_host_header(string raw, string expected)
    {
        var result = ShowcaseDomain.Normalize(raw);

        Assert.False(result.Rejected);
        Assert.Equal(expected, result.Host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_domain_is_not_a_rejection(string? raw)
    {
        var result = ShowcaseDomain.Normalize(raw);

        Assert.False(result.Rejected);
        Assert.Null(result.Host);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("my game.com")]
    [InlineData("-mygame.com")]
    [InlineData("mygame-.com")]
    [InlineData("my_game.com")]
    [InlineData("mygame..com")]
    public void Rejects_what_could_never_be_a_host(string raw) =>
        Assert.True(ShowcaseDomain.Normalize(raw).Rejected);
}

public class ShowcaseLayoutTests
{
    [Fact]
    public void Empty_layout_uses_the_compatible_default_order()
    {
        var layout = ShowcaseLayouts.Parse("");

        Assert.Equal(ShowcaseBlockKinds.Hero, layout.Blocks[0].Kind);
        Assert.Contains(layout.Blocks, block => block.Kind == ShowcaseBlockKinds.Devlog);
        Assert.DoesNotContain(layout.Blocks, block => block.Kind == ShowcaseBlockKinds.About);
    }

    [Fact]
    public void Normalization_drops_unknown_and_duplicate_blocks_and_restores_hero()
    {
        var layout = ShowcaseLayouts.Parse("""
            {"version":91,"blocks":[
              {"id":"x","kind":"about","visible":true,"title":" About ","body":"Copy"},
              {"id":"x2","kind":"about","visible":false},
              {"id":"evil","kind":"script","visible":true,"body":"<script>"}
            ]}
            """);

        Assert.Equal([ShowcaseBlockKinds.Hero, ShowcaseBlockKinds.About], layout.Blocks.Select(block => block.Kind));
        Assert.Equal("About", layout.Blocks[1].Title);
        Assert.Equal(ShowcaseLayouts.CurrentVersion, layout.Version);
    }

    [Fact]
    public void Hero_cannot_be_hidden_and_authored_text_is_bounded()
    {
        var value = new ShowcaseLayoutDocument(1,
        [
            new("wrong", ShowcaseBlockKinds.Hero, false, new string('t', 200), new string('b', 2100)),
        ]);

        var layout = ShowcaseLayouts.Normalize(value);

        Assert.True(layout.Blocks[0].Visible);
        Assert.Equal(ShowcaseLayouts.TitleMaxLength, layout.Blocks[0].Title!.Length);
        Assert.Equal(ShowcaseLayouts.BodyMaxLength, layout.Blocks[0].Body!.Length);
    }
}
