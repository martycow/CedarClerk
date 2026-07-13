using CedarClerk.Server.Ai;
using Xunit;

namespace CedarClerk.Tests;

public class AiEditPromptGeneratorTests
{
    [Fact]
    public void ParseResult_PlainJson_ReturnsTitleAndDoc()
    {
        var result = AiEditPromptGenerator.ParseResult("""{"title":"Fixed title","doc":{"type":"doc","content":[]}}""");

        Assert.Equal("Fixed title", result.Title);
        Assert.Contains("\"type\":\"doc\"", result.CedarJson.Replace(" ", ""));
    }

    [Fact]
    public void ParseResult_FencedJson_StripsFences()
    {
        var fenced = "```json\n{\"title\":\"T\",\"doc\":{\"type\":\"doc\",\"content\":[]}}\n```";

        var result = AiEditPromptGenerator.ParseResult(fenced);

        Assert.Equal("T", result.Title);
    }

    [Fact]
    public void ParseResult_MalformedJson_ThrowsAiEditException()
    {
        Assert.Throws<AiEditException>(() => AiEditPromptGenerator.ParseResult("not json at all"));
    }

    [Fact]
    public void ParseResult_MissingDocProperty_ThrowsAiEditException()
    {
        Assert.Throws<AiEditException>(() => AiEditPromptGenerator.ParseResult("""{"title":"T"}"""));
    }

    [Theory]
    [InlineData(AiEditKind.FixErrors)]
    [InlineData(AiEditKind.Schizo)]
    public void Build_IncludesTitleAndDocument(AiEditKind kind)
    {
        var prompt = AiEditPromptGenerator.Build("My Title", """{"type":"doc","content":[]}""", kind);

        Assert.Contains("My Title", prompt);
        Assert.Contains("\"type\":\"doc\"", prompt);
    }
}
