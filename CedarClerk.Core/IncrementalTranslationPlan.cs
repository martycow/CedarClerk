using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

/// <summary>
/// T-015 — re-translating a document that changed by one paragraph used to cost a translation of
/// the whole document: slow, expensive, and it threw away every manual correction the author had
/// made to the other paragraphs. This works out which top-level blocks actually moved, so only
/// those are sent to the provider and the rest are copied from the existing translation verbatim.
/// </summary>
public static class IncrementalTranslationPlan
{
    /// <summary>
    /// One block of the document being produced: either copied from the existing translation, or
    /// taken from the new source and handed to the provider.
    /// </summary>
    public record Step(bool Reuse, int Index);

    public record Plan(IReadOnlyList<Step> Steps, IReadOnlyList<string> BlocksToTranslate)
    {
        public int ReusedCount => Steps.Count(s => s.Reuse);
    }

    /// <summary>
    /// Null means "translate the whole document instead" — no snapshot, unparseable content, or
    /// an existing translation whose block count no longer lines up with the snapshot it was made
    /// from (a manual restructuring of the translation, which makes position meaningless).
    /// </summary>
    public static Plan? Build(string? sourceSnapshotJson, string newSourceJson, string existingTranslationJson)
    {
        if (string.IsNullOrWhiteSpace(sourceSnapshotJson)) return null;

        var oldSource = Blocks(sourceSnapshotJson);
        var newSource = Blocks(newSourceJson);
        var translation = Blocks(existingTranslationJson);
        if (oldSource.Count == 0 || newSource.Count == 0 || translation.Count == 0) return null;

        // The whole mechanism rests on "block i of the snapshot became block i of the
        // translation". A count mismatch means that stopped being true — most often because the
        // translation was edited by hand — and splicing by position would shuffle paragraphs.
        if (oldSource.Count != translation.Count) return null;

        var steps = new List<Step>();
        var toTranslate = new List<string>();
        foreach (var (matchedOldIndex, newIndex) in Align(oldSource, newSource))
        {
            if (matchedOldIndex >= 0)
            {
                steps.Add(new Step(true, matchedOldIndex));
            }
            else
            {
                steps.Add(new Step(false, toTranslate.Count));
                toTranslate.Add(newSource[newIndex]);
            }
        }

        // Everything changed anyway: the splice would add a round of bookkeeping and save nothing.
        return toTranslate.Count == 0 || toTranslate.Count < newSource.Count
            ? new Plan(steps, toTranslate)
            : null;
    }

    /// <summary>
    /// The finished translation document: reused blocks from <paramref name="existingTranslationJson"/>,
    /// new ones from the provider's answer, in the new source's order.
    /// </summary>
    public static string Assemble(Plan plan, string existingTranslationJson, string translatedBlocksJson)
    {
        var reusable = Blocks(existingTranslationJson);
        var fresh = Blocks(translatedBlocksJson);
        if (fresh.Count != plan.BlocksToTranslate.Count)
            throw new ArgumentException("Translated document has a different block count than was sent");

        var content = new JsonArray();
        foreach (var step in plan.Steps)
        {
            var json = step.Reuse ? reusable[step.Index] : fresh[step.Index];
            content.Add(JsonNode.Parse(json)!);
        }
        return new JsonObject { ["type"] = "doc", ["content"] = content }.ToJsonString();
    }

    /// <summary>
    /// A document holding only the blocks that need translating — a real TipTap doc, so it goes
    /// through the ordinary provider path (which preserves structure and only replaces text).
    /// </summary>
    public static string PartialDocument(Plan plan)
    {
        var content = new JsonArray();
        foreach (var block in plan.BlocksToTranslate) content.Add(JsonNode.Parse(block)!);
        return new JsonObject { ["type"] = "doc", ["content"] = content }.ToJsonString();
    }

    /// <summary>
    /// Longest-common-subsequence alignment of the two block lists, in new-source order. Each
    /// entry is (index in the old source, or -1 when the block is new; index in the new source).
    /// Same algorithm the publish-diff uses, kept separate because this one needs the pairing
    /// rather than the counts.
    /// </summary>
    private static IEnumerable<(int OldIndex, int NewIndex)> Align(List<string> before, List<string> after)
    {
        var n = before.Count; var m = after.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            dp[i, j] = before[i] == after[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var x = 0; var y = 0;
        while (y < m)
        {
            if (x < n && before[x] == after[y]) { yield return (x, y); x++; y++; }
            else if (x < n && dp[x + 1, y] >= dp[x, y + 1]) { x++; } // a block was removed
            else { yield return (-1, y); y++; }                     // a block is new or changed
        }
    }

    private static List<string> Blocks(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray().Select(x => x.GetRawText()).ToList()
                : [];
        }
        catch (JsonException) { return []; }
    }
}
