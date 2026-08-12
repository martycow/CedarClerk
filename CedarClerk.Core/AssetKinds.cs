namespace CedarClerk.Core;

// Kind of an indexed file, from its extension alone (T-122, ADR-107). Opening tens of thousands of
// files to be sure would turn a scan into an afternoon, so the answer is a guess and the UI never
// treats a kind as more than a filter.
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

    // Long on purpose: this is a game project's folder, so the formats that matter are engine and DCC
    // formats, not the handful a web app would think of. Unrecognised lands in Other, which is true —
    // a wrong guess is not.
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = Image, ["jpg"] = Image, ["jpeg"] = Image, ["gif"] = Image, ["bmp"] = Image,
        ["webp"] = Image, ["tga"] = Image, ["tif"] = Image, ["tiff"] = Image, ["psd"] = Image,
        ["psb"] = Image, ["svg"] = Image, ["exr"] = Image, ["hdr"] = Image, ["dds"] = Image,
        ["ktx"] = Image, ["ktx2"] = Image, ["basis"] = Image, ["qoi"] = Image, ["pbm"] = Image,
        ["ase"] = Image, ["aseprite"] = Image, ["pyxel"] = Image, ["piskel"] = Image,
        ["xcf"] = Image, ["kra"] = Image, ["clip"] = Image, ["procreate"] = Image,
        ["ai"] = Image, ["afdesign"] = Image, ["afphoto"] = Image, ["fig"] = Image,

        ["blend"] = Model, ["blend1"] = Model, ["blend2"] = Model,
        ["fbx"] = Model, ["obj"] = Model, ["gltf"] = Model, ["glb"] = Model, ["dae"] = Model,
        ["3ds"] = Model, ["ply"] = Model, ["stl"] = Model, ["abc"] = Model, ["usd"] = Model,
        ["usda"] = Model, ["usdc"] = Model, ["usdz"] = Model,
        ["max"] = Model, ["ma"] = Model, ["mb"] = Model, ["c4d"] = Model, ["zpr"] = Model,
        ["ztl"] = Model, ["spp"] = Model, ["sbs"] = Model, ["sbsar"] = Model, ["mset"] = Model,
        ["vox"] = Model, ["qb"] = Model,

        // No Music/SFX split: nothing in an extension distinguishes a score from an ambience loop.
        ["wav"] = Audio, ["mp3"] = Audio, ["ogg"] = Audio, ["flac"] = Audio, ["aiff"] = Audio,
        ["aif"] = Audio, ["m4a"] = Audio, ["opus"] = Audio, ["wma"] = Audio,
        ["mid"] = Audio, ["midi"] = Audio, ["mod"] = Audio, ["xm"] = Audio, ["it"] = Audio,
        ["s3m"] = Audio,
        ["rpp"] = Audio, ["flp"] = Audio, ["als"] = Audio, ["logicx"] = Audio, ["ptx"] = Audio,
        ["bank"] = Audio, ["fspro"] = Audio, ["wproj"] = Audio,

        ["mp4"] = Video, ["mov"] = Video, ["avi"] = Video, ["mkv"] = Video, ["webm"] = Video,
        ["wmv"] = Video, ["m4v"] = Video, ["prproj"] = Video, ["aep"] = Video, ["kdenlive"] = Video,

        ["ttf"] = Font, ["otf"] = Font, ["woff"] = Font, ["woff2"] = Font, ["fnt"] = Font,
        ["fon"] = Font, ["bdf"] = Font,

        // Shaders and scripts are Text rather than a kind of their own: an author looking for "the
        // text files" wants both, and a Scripts filter would need a code index to be worth anything.
        ["txt"] = Text, ["md"] = Text, ["json"] = Text, ["xml"] = Text, ["yaml"] = Text,
        ["yml"] = Text, ["csv"] = Text, ["tsv"] = Text, ["toml"] = Text, ["ini"] = Text,
        ["ink"] = Text, ["yarn"] = Text, ["twee"] = Text, ["fountain"] = Text, ["rpy"] = Text,
        ["cs"] = Text, ["gd"] = Text, ["lua"] = Text, ["py"] = Text, ["cpp"] = Text, ["h"] = Text,
        ["hlsl"] = Text, ["glsl"] = Text, ["shader"] = Text, ["gdshader"] = Text, ["cginc"] = Text,
        ["usf"] = Text, ["ush"] = Text,

        // Engine files stay Other rather than becoming a new kind: grouping a Unity scene with a text
        // file would be a lie of convenience. They are indexed, which is the point.
        ["unity"] = Other, ["prefab"] = Other, ["asset"] = Other, ["mat"] = Other,
        ["anim"] = Other, ["controller"] = Other, ["unitypackage"] = Other, ["shadergraph"] = Other,
        ["uasset"] = Other, ["umap"] = Other,
        ["tscn"] = Other, ["tres"] = Other, ["escn"] = Other, ["godot"] = Other,
        ["tmx"] = Other, ["tsx"] = Other, ["ldtk"] = Other, ["tiled-project"] = Other,
    };

    // What the image library decodes, plus .blend, which carries its own preview. Asking for a
    // thumbnail of every image instead would show a broken-image icon for PSD, EXR and Aseprite.
    private static readonly HashSet<string> Previewable = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "webp", "tga", "tif", "tiff", "pbm", "qoi",
        "blend", "blend1", "blend2",
    };

    public static bool CanPreview(string? path) => Previewable.Contains(ExtensionOf(path));

    public static bool HasEmbeddedPreview(string? path) =>
        ExtensionOf(path) is "blend" or "blend1" or "blend2";

    // Never null: a file that got indexed has to be showable somewhere.
    public static string FromPath(string? path)
    {
        var extension = ExtensionOf(path);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out var kind) ? kind : Other;
    }

    public static string ExtensionOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var extension = Path.GetExtension(path);
        return extension.Length <= 1 ? "" : extension[1..].ToLowerInvariant();
    }

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);

    // Filtering here rather than after the walk keeps a Unity project's Library/ — hundreds of
    // thousands of cache artefacts — from becoming as many database rows.
    public static bool ShouldIndex(string? path) => ByExtension.ContainsKey(ExtensionOf(path));

    // Build output, engine caches and version control: each can hold more files than the project
    // itself, and none holds an asset anyone authored.
    public static readonly IReadOnlySet<string> SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "obj", "bin",
        "Library", "Temp", "Logs", "obj.meta",
        "Intermediate", "Saved", "DerivedDataCache", "Binaries",
        ".import", ".godot",
    };
}
