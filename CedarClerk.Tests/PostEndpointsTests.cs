using CedarClerk.Server;

namespace CedarClerk.Tests;

public class PostEndpointsTests
{
    [Fact]
    public void Empty_tags_returns_null()
    {
        Assert.Null(PostEndpoints.BuildHashtagLine(""));
    }

    [Fact]
    public void Whitespace_only_tags_returns_null()
    {
        Assert.Null(PostEndpoints.BuildHashtagLine("  ,  ,  "));
    }

    [Fact]
    public void Single_tag_becomes_one_hashtag()
    {
        Assert.Equal("#travel", PostEndpoints.BuildHashtagLine("travel"));
    }

    [Fact]
    public void Multiple_tags_joined_with_spaces()
    {
        Assert.Equal("#travel #food #2026", PostEndpoints.BuildHashtagLine("travel, food, 2026"));
    }

    [Fact]
    public void Tag_with_internal_spaces_is_collapsed()
    {
        Assert.Equal("#mytag", PostEndpoints.BuildHashtagLine("my tag"));
    }
}
