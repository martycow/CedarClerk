using System.Text.Json;
using CedarClerk.Server.Publishing;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CedarClerk.Tests;

// Draft.CtaButtonsJson -> InputRichBlockButtons happens at the wire level only (the stored
// document never carries the buttons), so the mapping is what these tests pin down: what counts
// as a valid button, what drops silently, and that the result survives the client's own
// serializer the way TelegramWireMappingTests demands of every block.
public class TelegramCtaButtonsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("not json at all")]
    [InlineData("{\"text\":\"a\",\"url\":\"https://example.com\"}")]
    [InlineData("42")]
    public void Nothing_usable_yields_no_block(string? json)
    {
        Assert.Null(TelegramPublishTarget.BuildCtaButtons(json));
    }

    [Fact]
    public void Valid_buttons_map_text_and_url()
    {
        var json = """[{"text":"Read more","url":"https://example.com/post"},{"text":"Join","url":"http://example.com/join"}]""";

        var block = TelegramPublishTarget.BuildCtaButtons(json);

        var buttons = Assert.IsType<InputRichBlockButtons>(block).Buttons.ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal("Read more", Assert.IsType<RichTextText>(buttons[0].Text).Text);
        Assert.Equal("https://example.com/post", buttons[0].Url);
        Assert.Equal("http://example.com/join", buttons[1].Url);
    }

    [Fact]
    public void More_than_three_buttons_keep_only_the_first_three()
    {
        var json = """
                   [{"text":"1","url":"https://example.com/1"},
                    {"text":"2","url":"https://example.com/2"},
                    {"text":"3","url":"https://example.com/3"},
                    {"text":"4","url":"https://example.com/4"}]
                   """;

        var buttons = TelegramPublishTarget.ParseCtaButtons(json);

        Assert.Equal(3, buttons.Count);
        Assert.Equal("https://example.com/3", buttons[^1].Url);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("tg://user?id=1")]
    [InlineData("/relative/path")]
    [InlineData("example.com/no-scheme")]
    [InlineData("")]
    public void Non_http_urls_drop_silently(string url)
    {
        var json = JsonSerializer.Serialize(new[] { new { text = "Open", url } });

        Assert.Empty(TelegramPublishTarget.ParseCtaButtons(json));
    }

    [Fact]
    public void Blank_or_overlong_text_drops_the_button_but_keeps_its_neighbours()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { text = "", url = "https://example.com/a" },
            new { text = "   ", url = "https://example.com/b" },
            new { text = new string('x', 33), url = "https://example.com/c" },
            new { text = "Fits", url = "https://example.com/d" },
        });

        var buttons = TelegramPublishTarget.ParseCtaButtons(json);

        var button = Assert.Single(buttons);
        Assert.Equal("Fits", button.Text);
        Assert.Equal("https://example.com/d", button.Url);
    }

    [Fact]
    public void Button_text_is_trimmed_and_a_32_char_text_still_fits()
    {
        var exactly32 = new string('y', 32);
        var json = JsonSerializer.Serialize(new[] { new { text = $"  {exactly32}  ", url = "https://example.com" } });

        var button = Assert.Single(TelegramPublishTarget.ParseCtaButtons(json));
        Assert.Equal(exactly32, button.Text);
    }

    [Fact]
    public void Entries_missing_text_or_url_drop_silently()
    {
        var json = """[{"url":"https://example.com"},{"text":"No url"},null,{"text":"Ok","url":"https://example.com/ok"}]""";

        var button = Assert.Single(TelegramPublishTarget.ParseCtaButtons(json));
        Assert.Equal("Ok", button.Text);
    }

    [Fact]
    public void Button_block_survives_the_client_serializer()
    {
        var block = TelegramPublishTarget.BuildCtaButtons("""[{"text":"Read","url":"https://example.com"}]""");
        var message = new InputRichMessage { Blocks = [block!] };

        var json = JsonSerializer.Serialize(message, JsonBotAPI.Options);

        Assert.Contains("https://example.com", json);
        Assert.DoesNotContain("\"align\":0", json);
    }
}
