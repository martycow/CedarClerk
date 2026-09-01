namespace CedarClerk.Core;

// T-331 — what a stored Preset is a preset of. Strings, like ProjectRoles: a fourth kind should be
// a constant, a config record and a UI, never a migration. The kind fixes which config record
// reads ConfigJson, so a row can never be read through the wrong shape.
public static class PresetKinds
{
    public const string Document = "document";
    public const string Project = "project";
    public const string Export = "export";

    public static readonly IReadOnlyList<string> All = [Document, Project, Export];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}
