using System.Text.Json;

namespace CedarClerk.Server.Ai;

public static class AiEditPromptGenerator
{
    public static string Build(string title, string cedarJson, AiEditKind kind) =>
        $$"""
          You are editing a blog post stored as a TipTap (ProseMirror) JSON document.

          {{InstructionFor(kind)}}

          Rules:
          - Keep the JSON structure EXACTLY as is: same nodes, same order, same "type"/"attrs"/"marks" values.
          - Only change the string values of "text" properties inside text nodes.
          - Do NOT touch URLs, file paths, code inside code blocks, or LaTeX formulas.
          - Return ONLY a JSON object of the form {"title": "<edited title>", "doc": <edited TipTap document>} with no markdown fences and no commentary.

          Title: {{JsonSerializer.Serialize(title)}}
          Document:
          {{cedarJson}}
          """;

    public static AiEditResult ParseResult(string modelOutput)
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
            var editedDoc = root.GetProperty("doc").GetRawText();
            return new AiEditResult(title, editedDoc);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AiEditException("Model returned malformed edit output — try again", ex);
        }
    }
    
    private static string InstructionFor(AiEditKind kind) => kind switch
    {
        AiEditKind.FixErrors =>
            "Fix spelling, grammar, and punctuation mistakes only. Do not change wording, tone, " +
            "structure, or meaning beyond what's needed to correct an actual error. Keep the original language.",
        AiEditKind.Schizo =>
            "Rewrite this in an unhinged \"schizoposting\" internet style: frantic stream-of-consciousness, " +
            "RANDOM CAPS on key words, paranoid conspiratorial tangents, excessive exclamation points and ellipses..., " +
            "numbered \"revelations\", but keep the original topic and core facts recognizable underneath the chaos. " +
            "Keep the original language.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
