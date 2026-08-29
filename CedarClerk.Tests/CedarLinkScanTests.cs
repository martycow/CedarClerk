using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-238. What gets probed before a publish is exactly what this scan returns — so the tests pin
// down what counts as an outbound link (http/https only, each once) and what stays out of it.
public class CedarLinkScanTests
{
    private static string Doc(string content) => $$"""{"type":"doc","content":[{{content}}]}""";

    private static string LinkedText(string href) =>
        $$$"""{"type":"paragraph","content":[{"type":"text","text":"see","marks":[{"type":"link","attrs":{"href":"{{{href}}}"}}]}]}""";

    [Fact]
    public void Collects_the_href_of_a_link_mark()
    {
        var links = CedarLinkScan.CollectLinks(Doc(LinkedText("https://example.com/page")));

        Assert.Equal(["https://example.com/page"], links);
    }

    [Fact]
    public void Collects_an_external_media_src()
    {
        var doc = Doc("""{"type":"image","attrs":{"src":"https://img.example.com/a.png"}}""");

        Assert.Equal(["https://img.example.com/a.png"], CedarLinkScan.CollectLinks(doc));
    }

    [Fact]
    public void Skips_own_media_paths()
    {
        var doc = Doc("""{"type":"image","attrs":{"src":"/media/a.jpg"}}""");

        Assert.Empty(CedarLinkScan.CollectLinks(doc));
    }

    [Fact]
    public void Skips_non_http_schemes()
    {
        var doc = Doc(LinkedText("mailto:someone@example.com") + "," + LinkedText("tg://resolve?domain=x"));

        Assert.Empty(CedarLinkScan.CollectLinks(doc));
    }

    [Fact]
    public void Deduplicates_while_preserving_order()
    {
        var doc = Doc(string.Join(",",
            LinkedText("https://a.example"), LinkedText("https://b.example"), LinkedText("https://a.example")));

        Assert.Equal(["https://a.example", "https://b.example"], CedarLinkScan.CollectLinks(doc));
    }

    [Fact]
    public void Finds_links_nested_inside_other_blocks()
    {
        var doc = Doc($$"""{"type":"blockquote","content":[{{LinkedText("http://quoted.example")}}]}""");

        Assert.Equal(["http://quoted.example"], CedarLinkScan.CollectLinks(doc));
    }

    [Fact]
    public void Malformed_json_yields_an_empty_list_rather_than_throwing()
    {
        Assert.Empty(CedarLinkScan.CollectLinks("{not json"));
    }
}
