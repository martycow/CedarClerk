namespace CedarClerk.Core;

// ADR-293 — a project holds the set of modules it has switched on; its type is only the preset it
// was created from. The keys live in SQLite (ProjectModule.ModuleKey) and the API — byte-exact
// zone — and are strings rather than a schema enum so that a new module is a code change, not a
// migration. A key is a section of the workbench; the UI keeps its own routes for each.
public static class ProjectModules
{
    public const string Documents = "documents";
    public const string Assets = "assets";
    public const string Site = "site";
    public const string Posts = "posts";
    public const string Calendar = "calendar";
    public const string Metrics = "metrics";
    public const string Tasks = "tasks";
    public const string Planner = "planner";
    public const string Builds = "builds";
    public const string Canvas = "canvas";
    public const string Dialogues = "dialogues";

    /// <summary>Every module there is, in the order the toggles list them.</summary>
    public static readonly IReadOnlyList<string> All =
        [Documents, Assets, Site, Posts, Calendar, Metrics, Tasks, Planner, Builds, Canvas, Dialogues];

    public static bool IsKnown(string? key) => key is not null && All.Contains(key);

    /// <summary>Position in <see cref="All"/>; unknown keys sort last.</summary>
    public static int Order(string key) => All.Contains(key) ? All.ToList().IndexOf(key) : All.Count;

    /// <summary>Documents can never be switched off: a project without them has no content at all.</summary>
    public static bool IsRequired(string key) => key == Documents;

    private static readonly IReadOnlyDictionary<string, string[]> Presets = new Dictionary<string, string[]>
    {
        [ProjectTypes.Empty] = [Documents],
        [ProjectTypes.Blog] = [Documents, Assets, Site, Posts, Calendar, Metrics],
        [ProjectTypes.Work] = [Documents, Site, Posts, Metrics, Canvas],
        [ProjectTypes.Product] = [Documents, Site, Posts, Metrics, Tasks, Planner, Builds],
        [ProjectTypes.FullGame] = [Documents, Assets, Posts, Metrics, Tasks, Planner, Builds, Canvas, Dialogues],
        [ProjectTypes.Vault] = [Documents],
    };

    /// <summary>
    /// The full row set a project born from <paramref name="preset"/> starts with — every key,
    /// switched on or off, so the toggles can list them all. Null for a preset outside the table:
    /// the caller stops rather than guessing, which is what the backfill rule asks for.
    /// </summary>
    public static IReadOnlyDictionary<string, bool>? ForPreset(string? preset)
    {
        if (preset is null || !Presets.TryGetValue(preset, out var enabled)) return null;
        return All.ToDictionary(key => key, key => enabled.Contains(key));
    }

    public enum Refusal { UnknownKey, RequiredOff }

    /// <summary>
    /// Why a requested set of switches cannot be written, or null when it can. The rules are the
    /// two invariants the schema does not carry: only known keys, and documents never off.
    /// </summary>
    public static (Refusal Reason, string Key)? Refuse(IEnumerable<KeyValuePair<string, bool>> changes)
    {
        foreach (var (key, enabled) in changes)
        {
            if (!IsKnown(key)) return (Refusal.UnknownKey, key);
            if (IsRequired(key) && !enabled) return (Refusal.RequiredOff, key);
        }
        return null;
    }
}
