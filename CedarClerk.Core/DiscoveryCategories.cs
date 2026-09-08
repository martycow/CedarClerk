namespace CedarClerk.Core;

/// <summary>The stable URL/storage vocabulary for public Project discovery (ADR-243).</summary>
public static class DiscoveryCategories
{
    public const string Games = "games";
    public const string AppsTools = "apps-tools";
    public const string ComicsArt = "comics-art";
    public const string FilmAnimation = "film-animation";
    public const string MusicAudio = "music-audio";
    public const string Hardware = "hardware";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [Games, AppsTools, ComicsArt, FilmAnimation, MusicAudio, Hardware, Other];

    public static bool IsKnown(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    public static string Normalize(string? value) => IsKnown(value) ? value! : Other;

    public static string ForProjectType(string? value) => value switch
    {
        ProjectTypes.FullGame => Games,
        ProjectTypes.Product => AppsTools,
        _ => Other,
    };
}
