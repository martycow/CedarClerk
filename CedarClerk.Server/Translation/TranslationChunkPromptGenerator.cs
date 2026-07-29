using System.Text.Json;

namespace CedarClerk.Server.Translation;

// ADR-059 (docs/DECISIONS.md) — the chunked counterpart to TranslationPromptGenerator: translates a
// flat list of plain strings (extracted via TipTapTextNodes) instead of a whole TipTap JSON
// document. Used by AnthropicTranslationProvider only; OpenAiTranslationProvider still uses the
// original whole-document contract.
//
// 30.07.2026 — a real chunk came back with 74 translations for 72 inputs ("Model returned 74
// translations, expected 72"): a plain positional JSON array has no error-correction, so a model
// splitting even one string into two shifts every index after it and fails the entire chunk.
// Switched to a keyed JSON object ("0", "1", ... per input index) instead: extra keys the model
// invents are simply ignored, and a genuinely missing key names the exact index that failed rather
// than a bare count mismatch.
public static class TranslationChunkPromptGenerator
{
    public static string Build(IReadOnlyList<string> texts, string targetLanguage)
    {
        var input = new Dictionary<string, string>(texts.Count);
        for (var i = 0; i < texts.Count; i++)
            input[i.ToString()] = texts[i];

        return $$"""
              Translate each value in this JSON object into the language with ISO code "{{targetLanguage}}".
              Rules:
              - Return a JSON object with exactly the same keys as the input, one translation per key.
              - Never add, remove, merge, or split keys — even if a value looks like it contains several sentences, it is still ONE value and must come back as ONE translated value under its original key.
              - Do NOT translate URLs, file paths, code, or LaTeX formulas — return those unchanged.
              - Return ONLY the JSON object, with no markdown fences and no commentary.

              Input: {{JsonSerializer.Serialize(input)}}
              """;
    }

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

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(trimmed);
        }
        catch (JsonException ex)
        {
            throw new TranslationException("Model returned malformed translation output — try again", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var result = new List<string>(expectedCount);
            for (var i = 0; i < expectedCount; i++)
            {
                if (!root.TryGetProperty(i.ToString(), out var value))
                    throw new TranslationException($"Model omitted translation for item {i} of {expectedCount} — try again");
                result.Add(value.GetString() ?? "");
            }
            return result;
        }
    }
}
