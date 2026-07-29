using System.Text.Json;

namespace CedarClerk.Server.Translation;

// ADR-059 (docs/DECISIONS.md) — the chunked counterpart to TranslationPromptGenerator: translates a
// flat list of plain strings (extracted via TipTapTextNodes) instead of a whole TipTap JSON
// document. Used by AnthropicTranslationProvider only; OpenAiTranslationProvider still uses the
// original whole-document contract.
public static class TranslationChunkPromptGenerator
{
    public static string Build(IReadOnlyList<string> texts, string targetLanguage) =>
        $$"""
          Translate each string in this JSON array into the language with ISO code "{{targetLanguage}}".
          Rules:
          - Return exactly one translation per input string, at the same index, in the same order.
          - The output array must have exactly {{texts.Count}} elements — same length as the input.
          - Do NOT translate URLs, file paths, code, or LaTeX formulas — return those unchanged.
          - Return ONLY a JSON array of strings, with no markdown fences and no commentary.

          Input: {{JsonSerializer.Serialize(texts)}}
          """;

    public static List<string> ParseResult(string modelOutput, int expectedCount)
    {
        var trimmed = modelOutput.Trim();

        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        List<string> result;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            result = doc.RootElement.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new TranslationException("Model returned malformed translation output — try again", ex);
        }

        if (result.Count != expectedCount)
            throw new TranslationException($"Model returned {result.Count} translations, expected {expectedCount} — try again");

        return result;
    }
}
