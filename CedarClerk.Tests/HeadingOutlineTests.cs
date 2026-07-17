using CedarClerk.Core;

namespace CedarClerk.Tests;

public class HeadingOutlineTests
{
    [Fact]
    public void StartsWithHeading_true_when_first_block_is_a_heading()
    {
        var json = """{"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Hi"}]},{"type":"paragraph"}]}""";
        Assert.True(HeadingOutline.StartsWithHeading(json));
    }

    [Fact]
    public void StartsWithHeading_false_when_first_block_is_a_paragraph()
    {
        var json = """{"type":"doc","content":[{"type":"paragraph"},{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Hi"}]}]}""";
        Assert.False(HeadingOutline.StartsWithHeading(json));
    }

    [Fact]
    public void StartsWithHeading_false_for_empty_document()
    {
        var json = """{"type":"doc","content":[]}""";
        Assert.False(HeadingOutline.StartsWithHeading(json));
    }
}
