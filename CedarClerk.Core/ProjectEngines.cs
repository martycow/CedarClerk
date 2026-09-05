namespace CedarClerk.Core;

// Stored on Project.Engine and shown by the hub — a closed list, so the value is a key the client
// labels in either language, not a free string the press page already has in PressEngine.
public static class ProjectEngines
{
    public const string Unity = "unity";
    public const string Unreal = "unreal";
    public const string Godot = "godot";
    public const string GameMaker = "gamemaker";
    public const string Construct = "construct";
    public const string RenPy = "renpy";
    public const string RpgMaker = "rpgmaker";
    public const string Love2d = "love2d";
    public const string Defold = "defold";
    public const string MonoGame = "monogame";
    public const string Bevy = "bevy";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All =
        [Unity, Unreal, Godot, GameMaker, Construct, RenPy, RpgMaker, Love2d, Defold, MonoGame, Bevy, Custom];

    public static bool IsKnown(string? engine) => engine is not null && All.Contains(engine);
}
