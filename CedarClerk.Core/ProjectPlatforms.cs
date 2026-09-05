namespace CedarClerk.Core;

// Stored on Project.TargetPlatforms as comma-joined keys in this list's order. Parse and Join are
// the only two ways in and out of that column, so an unknown or repeated key never reaches it.
public static class ProjectPlatforms
{
    public const string Windows = "windows";
    public const string Mac = "mac";
    public const string Linux = "linux";
    public const string Web = "web";
    public const string Ios = "ios";
    public const string Android = "android";
    public const string Switch = "switch";
    public const string PlayStation = "playstation";
    public const string Xbox = "xbox";
    public const string Quest = "quest";

    public static readonly IReadOnlyList<string> All =
        [Windows, Mac, Linux, Web, Ios, Android, Switch, PlayStation, Xbox, Quest];

    public static bool IsKnown(string? platform) => platform is not null && All.Contains(platform);

    public static IReadOnlyList<string> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];
        var present = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return All.Where(present.Contains).ToList();
    }

    public static string Join(IEnumerable<string> platforms)
    {
        var present = platforms.ToHashSet(StringComparer.Ordinal);
        return string.Join(',', All.Where(present.Contains));
    }
}
