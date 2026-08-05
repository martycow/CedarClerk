using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-086. The point of this check is that the author sees the problem before the send rather than
// as a network's error message afterwards — so what is tested is the shape of the answer as much
// as the counting: what blocks, what merely warns, and what says nothing at all.
public class PublishValidatorTests
{
    private static PublishCapabilities Caps(int? maxChars = null, int maxMedia = 10, long? maxImage = null,
        bool video = true, bool audio = true, bool rich = true, bool tables = true, bool math = true,
        bool code = true, bool headings = true, bool lists = true, bool derivesShortPost = false) => new()
    {
        Network = "test",
        MaxCharacters = maxChars,
        MaxMediaItems = maxMedia,
        MaxImageBytes = maxImage,
        SupportsVideo = video,
        SupportsAudio = audio,
        SupportsRichText = rich,
        SupportsTables = tables,
        SupportsMath = math,
        SupportsCodeBlocks = code,
        SupportsHeadings = headings,
        SupportsLists = lists,
        DerivesShortPost = derivesShortPost,
    };

    private static string Doc(string content) => $$"""{"type":"doc","content":[{{content}}]}""";
    private static string Paragraph(string text) => $$"""{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}""";

    [Fact]
    public void A_document_that_fits_produces_no_issues()
    {
        Assert.Empty(PublishValidator.Validate(Doc(Paragraph("Short enough.")), Caps(maxChars: 100)));
    }

    [Fact]
    public void Too_long_is_blocking_and_reports_both_numbers()
    {
        var issues = PublishValidator.Validate(Doc(Paragraph(new string('a', 400))), Caps(maxChars: 300));

        var issue = Assert.Single(issues);
        Assert.Equal(PublishIssueCodes.TooLong, issue.Code);
        Assert.True(issue.Blocking);
        Assert.Equal(400, issue.Actual);
        Assert.Equal(300, issue.Limit);
    }

    // ADR-093 — for a network that derives its own short post (Bluesky, X), the document being
    // bigger than one post is the normal case, not an error: the teaser fits by construction.
    // Blocking here was the latent bug that would have 422'd every real document queued to Bluesky.
    [Fact]
    public void Overflow_merely_informs_when_the_network_derives_a_short_post()
    {
        var doc = Doc(Paragraph(new string('a', 400)));

        var issue = Assert.Single(PublishValidator.Validate(doc, Caps(maxChars: 300, derivesShortPost: true)));
        Assert.Equal(PublishIssueCodes.TooLong, issue.Code);
        Assert.False(issue.Blocking);
    }

    [Fact]
    public void Too_much_media_merely_informs_when_the_network_derives_a_short_post()
    {
        var doc = Doc("""{"type":"image","attrs":{"src":"/media/a.jpg"}},{"type":"image","attrs":{"src":"/media/b.jpg"}}""");

        var issue = Assert.Single(PublishValidator.Validate(doc, Caps(maxMedia: 0, derivesShortPost: true)));
        Assert.Equal(PublishIssueCodes.TooManyMedia, issue.Code);
        Assert.False(issue.Blocking);
    }

    [Fact]
    public void Unsupported_content_warns_but_does_not_block()
    {
        var doc = Doc("{\"type\":\"table\",\"content\":[]}," + Paragraph("x"));

        var issue = Assert.Single(PublishValidator.Validate(doc, Caps(tables: false)));

        Assert.Equal(PublishIssueCodes.NoTables, issue.Code);
        // The network takes the post and drops the table — the author needs to know, not to be stopped.
        Assert.False(issue.Blocking);
    }

    [Fact]
    public void Formatting_is_only_reported_where_the_network_has_none()
    {
        var doc = Doc("""{"type":"paragraph","content":[{"type":"text","text":"bold","marks":[{"type":"bold"}]}]}""");

        Assert.Empty(PublishValidator.Validate(doc, Caps(rich: true)));
        Assert.Contains(PublishValidator.Validate(doc, Caps(rich: false)), i => i.Code == PublishIssueCodes.NoRichText);
    }

    [Fact]
    public void Carousel_children_count_as_media()
    {
        var doc = Doc("""{"type":"carousel","attrs":{"images":["/media/a.jpg","/media/b.jpg","/media/c.jpg"]}}""");

        var issue = Assert.Single(PublishValidator.Validate(doc, Caps(maxMedia: 2)));
        Assert.Equal(PublishIssueCodes.TooManyMedia, issue.Code);
        Assert.Equal(3, issue.Actual);
    }

    [Fact]
    public void An_image_over_the_per_file_limit_blocks()
    {
        var doc = Doc("""{"type":"image","attrs":{"src":"/media/big.jpg"}}""");
        var sizes = new Dictionary<string, long> { ["big.jpg"] = 12L * 1024 * 1024 };

        var issues = PublishValidator.Validate(doc, Caps(maxImage: 10L * 1024 * 1024), sizes);

        Assert.Contains(issues, i => i.Code == PublishIssueCodes.ImageTooLarge && i.Blocking);
    }

    // The case that actually failed in production on 01.08.2026: every file within its own limit,
    // the total large enough that the network's fetch outlived the proxy in front of the origin.
    [Fact]
    public void Media_that_is_within_every_limit_but_heavy_warns_about_the_wait()
    {
        var doc = Doc("""{"type":"audio","attrs":{"src":"/media/track.mp3"}},{"type":"video","attrs":{"src":"/media/clip.mp4"}}""");
        var sizes = new Dictionary<string, long>
        {
            ["track.mp3"] = 15L * 1024 * 1024,
            ["clip.mp4"] = 15L * 1024 * 1024,
        };

        var issues = PublishValidator.Validate(doc, Caps(maxImage: 10L * 1024 * 1024), sizes);

        var slow = Assert.Single(issues, i => i.Code == PublishIssueCodes.SlowMedia);
        Assert.False(slow.Blocking);
        Assert.Equal(30L * 1024 * 1024, slow.Actual);
        // Not an image, so the per-image limit must not fire on it.
        Assert.DoesNotContain(issues, i => i.Code == PublishIssueCodes.ImageTooLarge);
    }

    [Fact]
    public void Malformed_json_is_silent_rather_than_throwing()
    {
        Assert.Empty(PublishValidator.Validate("{not json", Caps()));
    }
}
