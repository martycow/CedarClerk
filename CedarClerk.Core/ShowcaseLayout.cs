using System.Text.Json;

namespace CedarClerk.Core;

public static class ShowcaseBlockKinds
{
    public const string Hero = "hero";
    public const string About = "about";
    public const string Links = "links";
    public const string Trailer = "trailer";
    public const string Gallery = "gallery";
    public const string Downloads = "downloads";
    public const string Devlog = "devlog";
    public const string Follow = "follow";
    public const string Roadmap = "roadmap";

    public static readonly IReadOnlyList<string> All =
        [Hero, About, Links, Trailer, Gallery, Downloads, Devlog, Follow, Roadmap];
}

public sealed record ShowcaseBlock(
    string Id,
    string Kind,
    bool Visible = true,
    string? Title = null,
    string? Body = null);

public sealed record ShowcaseLayoutDocument(int Version, IReadOnlyList<ShowcaseBlock> Blocks);

public static class ShowcaseLayouts
{
    public const int CurrentVersion = 1;
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ShowcaseLayoutDocument Default() => new(CurrentVersion,
    [
        new(ShowcaseBlockKinds.Hero, ShowcaseBlockKinds.Hero),
        new(ShowcaseBlockKinds.Links, ShowcaseBlockKinds.Links),
        new(ShowcaseBlockKinds.Trailer, ShowcaseBlockKinds.Trailer),
        new(ShowcaseBlockKinds.Gallery, ShowcaseBlockKinds.Gallery),
        new(ShowcaseBlockKinds.Downloads, ShowcaseBlockKinds.Downloads),
        new(ShowcaseBlockKinds.Devlog, ShowcaseBlockKinds.Devlog),
        new(ShowcaseBlockKinds.Follow, ShowcaseBlockKinds.Follow),
        new(ShowcaseBlockKinds.Roadmap, ShowcaseBlockKinds.Roadmap),
    ]);

    public static ShowcaseLayoutDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default();

        try
        {
            var value = JsonSerializer.Deserialize<ShowcaseLayoutDocument>(json, JsonOptions);
            return value is null ? Default() : Normalize(value);
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    public static ShowcaseLayoutDocument Normalize(ShowcaseLayoutDocument value)
    {
        var known = ShowcaseBlockKinds.All.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var blocks = new List<ShowcaseBlock>();

        foreach (var block in value.Blocks ?? [])
        {
            var kind = (block.Kind ?? "").Trim().ToLowerInvariant();
            if (!known.Contains(kind) || !seen.Add(kind)) continue;

            blocks.Add(new ShowcaseBlock(
                kind,
                kind,
                kind == ShowcaseBlockKinds.Hero || block.Visible,
                Bounded(block.Title, TitleMaxLength),
                Bounded(block.Body, BodyMaxLength)));
        }

        if (blocks.Count == 0) return Default();
        if (blocks.All(b => b.Kind != ShowcaseBlockKinds.Hero))
            blocks.Insert(0, new(ShowcaseBlockKinds.Hero, ShowcaseBlockKinds.Hero));

        return new ShowcaseLayoutDocument(CurrentVersion, blocks);
    }

    public static string Serialize(ShowcaseLayoutDocument value) =>
        JsonSerializer.Serialize(Normalize(value), JsonOptions);

    private static string? Bounded(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
