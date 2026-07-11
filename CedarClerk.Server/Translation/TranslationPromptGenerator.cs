using System.Text.Json;

namespace CedarClerk.Server.Translation;

public static class TranslationPromptGenerator
{
    public static string Build(string title, string cedarJson, string targetLanguage) =>
        $$"""
          You are translating a blog post stored as a TipTap (ProseMirror) JSON document.

          Translate the title and every human-visible text into the language with ISO code "{{targetLanguage}}".
          Rules:
          - Keep the JSON structure EXACTLY as is: same nodes, same order, same "type"/"attrs"/"marks" values.
          - Only change the string values of "text" properties inside text nodes, translating them naturally.
          - Do NOT translate URLs, file paths, code inside code blocks, or LaTeX formulas.
          - Return ONLY a JSON object of the form {"title": "<translated title>", "doc": <translated TipTap document>} with no markdown fences and no commentary.

          Title: {{JsonSerializer.Serialize(title)}}
          Document:
          {{cedarJson}}
          """;

    public static TranslationResult ParseResult(string modelOutput)
    {
        var trimmed = modelOutput.Trim();
        
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            var title = root.GetProperty("title").GetString() ?? "";
            var translatedDoc = root.GetProperty("doc").GetRawText();
            return new TranslationResult(title, translatedDoc);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TranslationException("Model returned malformed translation output — try again", ex);
        }
    }
}