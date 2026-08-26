namespace CedarClerk.Core;

/// <summary>What a <c>ShowcaseStatDaily</c> row counts (T-296, ADR-216).</summary>
public static class ShowcaseStatKinds
{
    public const string View = "view";
    public const string LinkClick = "link-click";

    public static bool IsKnown(string? kind) => kind is View or LinkClick;
}

/// <summary>
/// The showcase gallery: uploaded media paths, one per line (T-295, ADR-216). A text column rather
/// than a table because an ordered list of paths with no per-row attributes is a list, and a table
/// of it would need a join to put the order back.
/// </summary>
public static class ShowcaseGallery
{
    /// <summary>
    /// The paths a gallery actually renders. Only this server's own <c>/media/</c> paths survive:
    /// an arbitrary URL here would let the page pull an image from anywhere, which is a different
    /// decision than the one ADR-216 made, and one no owner asked for.
    /// </summary>
    public static List<string> Parse(string? raw)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return paths;

        foreach (var line in raw.Split('\n'))
        {
            var path = line.Trim();
            if (path.Length == 0 || paths.Contains(path)) continue;
            if (!path.StartsWith(Consts.Showcase.MediaPrefix, StringComparison.Ordinal)) continue;
            if (path.Contains("..", StringComparison.Ordinal)) continue;
            paths.Add(path);
            if (paths.Count == Consts.Showcase.GalleryMaxImages) break;
        }

        return paths;
    }
}
