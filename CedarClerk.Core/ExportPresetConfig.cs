using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// T-331 — where a post goes, saved under a name. The Export modal's step 2 is a set of checkboxes
// an author re-ticks the same way on every post; a preset is that set, stored once.
//
// Deliberately destinations and languages only, not channel ids: a channel can be disconnected and
// reconnected with a new row, and a preset that silently names a channel nobody has any more would
// fail at send time rather than at pick time. Which Telegram channel receives the post stays step
// 3's question, asked against what is actually connected right now.
public static class ExportDestinations
{
    public const string Blog = "blog";

    public static readonly IReadOnlyList<string> All =
        [Blog, .. PublishNetworks.All];

    public static bool IsKnown(string? destination) =>
        destination is not null && All.Contains(destination);
}

public sealed record ExportPresetConfig(
    IReadOnlyList<string> Destinations, IReadOnlyList<string> Languages)
{
    public static readonly ExportPresetConfig Default = new([], []);

    public static ExportPresetConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            var node = JsonNode.Parse(json);
            return new ExportPresetConfig(
                Read(node?["destinations"], ExportDestinations.IsKnown, ExportDestinations.All.Count),
                // Fully qualified: the record's own Languages property shadows the static class.
                Read(node?["languages"],
                    l => l is not null && Localization.Languages.IsContentLanguage(l),
                    Localization.Languages.ContentLanguages.Count));
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public string ToJson() => new JsonObject
    {
        ["destinations"] = new JsonArray(Destinations.Select(d => JsonValue.Create(d)).ToArray()),
        ["languages"] = new JsonArray(Languages.Select(l => JsonValue.Create(l)).ToArray()),
    }.ToJsonString();

    /// <summary>Known values only, deduplicated, in the order the caller listed them.</summary>
    private static List<string> Read(JsonNode? node, Func<string?, bool> known, int max) =>
        node?.AsArray()
            .Select(v => (v?.GetValue<string>() ?? "").Trim().ToLowerInvariant())
            .Where(v => known(v))
            .Distinct()
            .Take(max)
            .ToList() ?? [];
}
