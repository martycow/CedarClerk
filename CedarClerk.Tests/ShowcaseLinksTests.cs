using CedarClerk.Server;

namespace CedarClerk.Tests;

public class ShowcaseLinksTests
{
    [Fact]
    public void Parses_label_url_lines()
    {
        var links = BlogEndpoints.ParseShowcaseLinks(
            "Wishlist on Steam|https://store.steampowered.com/app/123\nitch.io|https://mooexe.itch.io/game");

        Assert.Equal(2, links.Count);
        Assert.Equal(("Wishlist on Steam", "https://store.steampowered.com/app/123"), links[0]);
        Assert.Equal(("itch.io", "https://mooexe.itch.io/game"), links[1]);
    }

    [Fact]
    public void Skips_lines_that_are_not_label_pipe_url()
    {
        var links = BlogEndpoints.ParseShowcaseLinks(
            "just text\n|https://no-label.example\nNo url|\nJs|javascript:alert(1)\nOk|https://ok.example\r\n");

        Assert.Single(links);
        Assert.Equal("Ok", links[0].Label);
    }

    [Fact]
    public void Empty_input_yields_no_links()
    {
        Assert.Empty(BlogEndpoints.ParseShowcaseLinks(""));
    }
}
