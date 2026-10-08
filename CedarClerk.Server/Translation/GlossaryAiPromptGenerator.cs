using System.Text.Json;

namespace CedarClerk.Server.Translation;

// ADR-320 — the prompts behind IGlossaryAiProvider. Keyed JSON in and out for the same reason
// TranslationChunkPromptGenerator uses it: a positional array has no way to notice a dropped item.
public static class GlossaryAiPromptGenerator
{
    public const int MaxSpellings = 12;

    public static string BuildTranslate(IReadOnlyList<GlossaryTermSource> terms, string targetLanguage)
    {
        var input = new Dictionary<string, object>(terms.Count);
        for (var i = 0; i < terms.Count; i++)
            input[i.ToString()] = new { name = terms[i].Name, description = terms[i].Description };

        return $$"""
              Each value in this JSON object is a glossary term: a "name" and a "description". Translate each into the language with ISO code "{{targetLanguage}}".
              Rules:
              - Return a JSON object with exactly the same keys as the input.
              - Each value is an object with three fields: "name", "spellings" and "description".
              - "name" is the term as a writer in the target language writes it. Keep a product name, an acronym or a proper noun unchanged when the target language keeps it.
              - "spellings" is an array of the other word forms of that translated name as they occur in running text in the target language: its grammatical inflections (case, number, gender) and its common alternative spellings. These are forms of the translated name itself, never translations of other words and never synonyms. Do not repeat the name. Use an empty array when the target language does not inflect the term. At most {{MaxSpellings}} forms.
              - "description" is the description translated. Use an empty string when the input description is empty.
              - Return ONLY the JSON object, with no markdown fences and no commentary.

              Input: {{JsonSerializer.Serialize(input)}}
              """;
    }

    public static List<GlossaryTermTranslation> ParseTranslate(string modelOutput, int expectedCount)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(StripFence(modelOutput));
        }
        catch (JsonException ex)
        {
            throw new TranslationException("Model returned malformed translation output — try again", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new TranslationException("Model returned malformed translation output — try again");

            var result = new List<GlossaryTermTranslation>(expectedCount);
            for (var i = 0; i < expectedCount; i++)
            {
                if (!root.TryGetProperty(i.ToString(), out var value) || value.ValueKind != JsonValueKind.Object)
                    throw new TranslationException($"Model omitted translation for item {i} of {expectedCount} — try again");

                var spellings = value.TryGetProperty("spellings", out var forms) && forms.ValueKind == JsonValueKind.Array
                    ? forms.EnumerateArray()
                        .Where(f => f.ValueKind == JsonValueKind.String)
                        .Select(f => f.GetString()!.Trim())
                        .Where(f => f.Length > 0)
                        .Take(MaxSpellings)
                        .ToList()
                    : [];
                result.Add(new GlossaryTermTranslation(Text(value, "name"), spellings, Text(value, "description")));
            }
            return result;
        }
    }

    public static string BuildDescribe(string term, string language, bool hasImage)
    {
        var subject = (term.Trim().Length > 0, hasImage) switch
        {
            (true, true) => $"Write a glossary description of the term {JsonSerializer.Serialize(term.Trim())}. The attached image illustrates the term: use what it shows.",
            (true, false) => $"Write a glossary description of the term {JsonSerializer.Serialize(term.Trim())}.",
            _ => "Write a glossary description of what the attached image shows.",
        };

        return $$"""
              {{subject}}
              Rules:
              - Write in the language with ISO code "{{language}}".
              - It is shown in a small tooltip when a blog reader meets the term: one to three plain sentences, 300 characters at most.
              - Say what the thing is. Do not begin by repeating the term, and do not address the reader.
              - Plain text only: no markdown, no lists, no surrounding quotes.
              - Return ONLY the description.
              """;
    }

    public static string ParseDescribe(string modelOutput)
    {
        var text = StripFence(modelOutput).Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1].Trim();
        return text;
    }

    private static string Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()!.Trim()
            : "";

    private static string StripFence(string modelOutput)
    {
        var trimmed = modelOutput.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }
}
