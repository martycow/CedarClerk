using CedarClerk.Server.Translation;
using Xunit;

namespace CedarClerk.Tests;

public class TranslationChunkPromptGeneratorTests
{
    [Fact]
    public void Build_IncludesTargetLanguageAndInputStrings()
    {
        var prompt = TranslationChunkPromptGenerator.Build(["Hello", "world"], "ru");

        Assert.Contains("\"ru\"", prompt);
        Assert.Contains("Hello", prompt);
        Assert.Contains("world", prompt);
        Assert.Contains("exactly 2 elements", prompt);
    }

    [Fact]
    public void ParseResult_PlainJsonArray_ReturnsStrings()
    {
        var result = TranslationChunkPromptGenerator.ParseResult("""["Привет", "мир"]""", 2);
        Assert.Equal(new[] { "Привет", "мир" }, result);
    }

    [Fact]
    public void ParseResult_FencedJson_StripsFences()
    {
        var fenced = "```json\n[\"a\", \"b\"]\n```";
        Assert.Equal(new[] { "a", "b" }, TranslationChunkPromptGenerator.ParseResult(fenced, 2));
    }

    [Fact]
    public void ParseResult_MalformedJson_ThrowsTranslationException()
    {
        Assert.Throws<TranslationException>(() => TranslationChunkPromptGenerator.ParseResult("not json at all", 1));
    }

    [Fact]
    public void ParseResult_WrongCount_ThrowsTranslationException()
    {
        Assert.Throws<TranslationException>(() => TranslationChunkPromptGenerator.ParseResult("""["only one"]""", 2));
    }
}
