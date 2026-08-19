using CedarClerk.Core;

namespace CedarClerk.Tests;

public class DiscordPostBuilderTests
{
    private static string Doc(params string[] paragraphs) =>
        """{"type":"doc","content":[""" +
        string.Join(",", paragraphs.Select(p =>
            $$$"""{"type":"paragraph","content":[{"type":"text","text":"{{{p}}}"}]}""")) +
        "]}";

    [Fact]
    public void Author_text_wins_over_the_teaser()
    {
        var text = DiscordPostBuilder.Build("My announcement", Doc("Body paragraph"), "https://blog/post");
        Assert.StartsWith("My announcement", text);
        Assert.DoesNotContain("Body paragraph", text);
    }

    [Fact]
    public void Blog_link_lands_on_its_own_line()
    {
        var text = DiscordPostBuilder.Build(null, Doc("First paragraph"), "https://blog/post");
        Assert.Equal("First paragraph\n\nhttps://blog/post", text);
    }

    [Fact]
    public void No_link_means_just_the_teaser()
    {
        Assert.Equal("First paragraph", DiscordPostBuilder.Build(null, Doc("First paragraph"), null));
    }

    [Fact]
    public void Teaser_accumulates_whole_paragraphs()
    {
        var teaser = DiscordPostBuilder.Teaser(Doc("One", "Two", "Three"));
        Assert.Equal("One\n\nTwo\n\nThree", teaser);
    }

    [Fact]
    public void Body_is_trimmed_to_keep_the_link()
    {
        var longText = new string('x', 2100);
        var url = "https://blog/post";
        var text = DiscordPostBuilder.Build(longText, Doc("p"), url);

        Assert.True(text.Length <= DiscordPostBuilder.MaxChars);
        Assert.EndsWith(url, text);
        Assert.Contains("…", text);
    }

    [Fact]
    public void Truncate_marks_the_cut_and_respects_the_limit()
    {
        Assert.Equal("", DiscordPostBuilder.Truncate("anything", 0));
        Assert.Equal("…", DiscordPostBuilder.Truncate("anything", 1));
        Assert.Equal("abc", DiscordPostBuilder.Truncate("abc", 3));
        var cut = DiscordPostBuilder.Truncate("abcdef", 4);
        Assert.Equal(4, cut.Length);
        Assert.EndsWith("…", cut);
    }

    [Fact]
    public void Empty_document_with_a_link_is_just_the_link()
    {
        Assert.Equal("https://blog/post", DiscordPostBuilder.Build(null, Doc(), "https://blog/post"));
    }
}

public class DiscordWebhookUrlShapeTests
{
    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789/abcDEF-123_xyz")]
    [InlineData("https://discordapp.com/api/webhooks/123456789/token")]
    [InlineData("https://discord.com/api/v10/webhooks/123456789/token")]
    public void Real_webhook_urls_match(string url) =>
        Assert.Matches(CedarClerk.Server.Publishing.DiscordPublishTarget.WebhookUrlShape(), url);

    [Theory]
    [InlineData("http://discord.com/api/webhooks/1/t")]
    [InlineData("https://evil.com/api/webhooks/1/t")]
    [InlineData("https://discord.com.evil.com/api/webhooks/1/t")]
    [InlineData("https://discord.com/api/webhooks/notanumber/t")]
    [InlineData("https://discord.com/api/webhooks/1/t?thread_id=1")]
    public void Anything_else_does_not(string url) =>
        Assert.DoesNotMatch(CedarClerk.Server.Publishing.DiscordPublishTarget.WebhookUrlShape(), url);
}
