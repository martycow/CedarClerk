using CedarClerk.Server.Translation;
using Xunit;

namespace CedarClerk.Tests;

public class TranslationChunkPromptGeneratorTests
{
    [Fact]
    public void Build_IncludesTargetLanguageAndInputStringsKeyedByIndex()
    {
        var prompt = TranslationChunkPromptGenerator.Build(["Hello", "world"], "ru");

        Assert.Contains("\"ru\"", prompt);
        Assert.Contains("\"0\":\"Hello\"", prompt);
        Assert.Contains("\"1\":\"world\"", prompt);
    }

    [Fact]
    public void ParseResult_KeyedJsonObject_ReturnsStringsInIndexOrder()
    {
        var result = TranslationChunkPromptGenerator.ParseResult("""{"0": "Привет", "1": "мир"}""", 2);
        Assert.Equal(new[] { "Привет", "мир" }, result);
    }

    [Fact]
    public void ParseResult_ExtraKeysBeyondExpectedCount_AreIgnored()
    {
        // 30.07.2026 real failure: model returned 74 keys for a 72-item chunk. Extra keys the
        // model invents must not fail the chunk as long as every expected index is present.
        var result = TranslationChunkPromptGenerator.ParseResult("""{"0": "a", "1": "b", "2": "unexpected extra"}""", 2);
        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void ParseResult_FencedJson_StripsFences()
    {
        var fenced = "```json\n{\"0\": \"a\", \"1\": \"b\"}\n```";
        Assert.Equal(new[] { "a", "b" }, TranslationChunkPromptGenerator.ParseResult(fenced, 2));
    }

    [Fact]
    public void ParseResult_MalformedJson_ThrowsTranslationException()
    {
        Assert.Throws<TranslationException>(() => TranslationChunkPromptGenerator.ParseResult("not json at all", 1));
    }

    [Fact]
    public void ParseResult_MissingKey_ThrowsTranslationExceptionNamingTheIndex()
    {
        var ex = Assert.Throws<TranslationException>(() => TranslationChunkPromptGenerator.ParseResult("""{"0": "only one"}""", 2));
        Assert.Contains("item 1", ex.Message);
    }
}
