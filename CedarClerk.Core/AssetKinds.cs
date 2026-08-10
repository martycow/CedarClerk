namespace CedarClerk.Core;

/// <summary>
/// What kind of thing an indexed file is (T-122, ADR-107), decided from its extension alone.
///
/// Extension-only on purpose: the index walks folders that can hold tens of thousands of files, and
/// opening each one to be sure would turn a scan into an afternoon. The cost is that the answer is
/// a guess — a good one for `.fbx`, a coin-flip for anything renamed — and the UI never treats a
/// kind as more than a filter.
/// </summary>
public static class AssetKinds
{
    public const string Image = "image";
    public const string Model = "model";
    public const string Audio = "audio";
    public const string Video = "video";
    public const string Font = "font";
    public const string Text = "text";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Image, Model, Audio, Video, Font, Text, Other];

    // Extensions are matched lowercase, without the dot. Deliberately generous on images (a game
    // project's art folder holds sources as well as exports) and deliberately short elsewhere:
    // an extension nobody recognises lands in Other, which is a true statement, while a wrong
    // guess is not.
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // Raster, vector and the source formats art actually lives in.
        ["png"] = Image, ["jpg"] = Image, ["jpeg"] = Image, ["gif"] = Image, ["bmp"] = Image,
        ["webp"] = Image, ["tga"] = Image, ["tif"] = Image, ["tiff"] = Image, ["psd"] = Image,
        ["svg"] = Image, ["exr"] = Image, ["hdr"] = Image, ["dds"] = Image, ["ase"] = Image,
        ["aseprite"] = Image, ["xcf"] = Image, ["kra"] = Image,

        ["fbx"] = Model, ["obj"] = Model, ["blend"] = Model, ["gltf"] = Model, ["glb"] = Model,
        ["dae"] = Model, ["3ds"] = Model, ["ply"] = Model, ["stl"] = Model, ["max"] = Model,
        ["ma"] = Model, ["mb"] = Model, ["usd"] = Model, ["usda"] = Model, ["usdc"] = Model,

        ["wav"] = Audio, ["mp3"] = Audio, ["ogg"] = Audio, ["flac"] = Audio, ["aiff"] = Audio,
        ["aif"] = Audio, ["m4a"] = Audio, ["opus"] = Audio, ["wma"] = Audio,
        // Tracker and sequencer formats. They are music by construction, but they are also audio,
        // and this does not pretend to a Music/SFX split — see the note on the class.
        ["mid"] = Audio, ["midi"] = Audio, ["mod"] = Audio, ["xm"] = Audio, ["it"] = Audio,

        ["mp4"] = Video, ["mov"] = Video, ["avi"] = Video, ["mkv"] = Video, ["webm"] = Video,
        ["wmv"] = Video, ["m4v"] = Video,

        ["ttf"] = Font, ["otf"] = Font, ["woff"] = Font, ["woff2"] = Font, ["fnt"] = Font,

        ["txt"] = Text, ["md"] = Text, ["json"] = Text, ["xml"] = Text, ["yaml"] = Text,
        ["yml"] = Text, ["csv"] = Text, ["tsv"] = Text, ["ink"] = Text, ["yarn"] = Text,
    };

    /// <summary>
    /// The kind for a file name or path. Anything unrecognised is <see cref="Other"/> — never null,
    /// because a file that got indexed has to be showable somewhere.
    /// </summary>
    public static string FromPath(string? path)
    {
        var extension = ExtensionOf(path);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out var kind) ? kind : Other;
    }

    /// <summary>Lowercase, no leading dot. Empty for a file that has no extension.</summary>
    public static string ExtensionOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var extension = Path.GetExtension(path);
        return extension.Length <= 1 ? "" : extension[1..].ToLowerInvariant();
    }

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);

    /// <summary>
    /// Whether a file is worth having a row at all. Filtering here rather than after the walk is
    /// what keeps a Unity project's `Library/` — hundreds of thousands of cache artefacts — from
    /// becoming hundreds of thousands of database rows nobody will ever look at.
    /// </summary>
    public static bool ShouldIndex(string? path) => ByExtension.ContainsKey(ExtensionOf(path));

    /// <summary>
    /// Directory names skipped wholesale during a scan: build output, engine caches and version
    /// control. Every one of them can hold more files than the project itself, and none of them
    /// holds an asset anyone authored.
    /// </summary>
    public static readonly IReadOnlySet<string> SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "obj", "bin",
        // Unity
        "Library", "Temp", "Logs", "obj.meta",
        // Unreal
        "Intermediate", "Saved", "DerivedDataCache", "Binaries",
        // Godot
        ".import", ".godot",
    };
}
