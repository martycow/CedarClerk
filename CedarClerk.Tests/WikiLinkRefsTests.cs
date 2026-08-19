using CedarClerk.Core;

namespace CedarClerk.Tests;

public class WikiLinkRefsTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Collects_nested_links_in_order_and_deduplicates()
    {
        var json = """
        {"type":"doc","content":[
            {"type":"paragraph","content":[
                {"type":"wikilink","attrs":{"draftId":"__A__","label":"Combat"}},
                {"type":"text","text":" and "},
                {"type":"wikilink","attrs":{"draftId":"__B__","label":"Weapons"}}
            ]},
            {"type":"blockquote","content":[{"type":"paragraph","content":[
                {"type":"wikilink","attrs":{"draftId":"__A__","label":"Combat again"}}
            ]}]}
        ]}
        """.Replace("__A__", A.ToString()).Replace("__B__", B.ToString());

        Assert.Equal([A, B], WikiLinkRefs.Collect(json));
    }

    [Fact]
    public void Corrupt_json_and_bad_ids_contribute_nothing()
    {
        Assert.Empty(WikiLinkRefs.Collect("{not json"));
        Assert.Empty(WikiLinkRefs.Collect("""{"type":"wikilink","attrs":{"draftId":"not-a-guid","label":"x"}}"""));
        Assert.Empty(WikiLinkRefs.Collect("""{"type":"wikilink","attrs":{}}"""));
    }
}
