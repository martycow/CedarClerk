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

    // Extensions are matched lowercase, without the dot. The list is deliberately long: this is a
    // game project's folder, and the formats an author actually opens are engine and DCC formats,
    // not the handful a web app would think of. An extension nobody recognises lands in Other,
    // which is a true statement — a wrong guess is not.
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- Images: raster, vector, and the source formats art is actually authored in.
        ["png"] = Image, ["jpg"] = Image, ["jpeg"] = Image, ["gif"] = Image, ["bmp"] = Image,
        ["webp"] = Image, ["tga"] = Image, ["tif"] = Image, ["tiff"] = Image, ["psd"] = Image,
        ["psb"] = Image, ["svg"] = Image, ["exr"] = Image, ["hdr"] = Image, ["dds"] = Image,
        ["ktx"] = Image, ["ktx2"] = Image, ["basis"] = Image, ["qoi"] = Image, ["pbm"] = Image,
        // Pixel-art tools.
        ["ase"] = Image, ["aseprite"] = Image, ["pyxel"] = Image, ["piskel"] = Image,
        // Painting suites.
        ["xcf"] = Image, ["kra"] = Image, ["clip"] = Image, ["procreate"] = Image,
        // Vector/UI authoring.
        ["ai"] = Image, ["afdesign"] = Image, ["afphoto"] = Image, ["fig"] = Image,

        // --- 3D. `.blend` first, because it is the one that matters most here and the only one
        // that carries its own preview (see BlendThumbnail).
        ["blend"] = Model, ["blend1"] = Model, ["blend2"] = Model,
        ["fbx"] = Model, ["obj"] = Model, ["gltf"] = Model, ["glb"] = Model, ["dae"] = Model,
        ["3ds"] = Model, ["ply"] = Model, ["stl"] = Model, ["abc"] = Model, ["usd"] = Model,
        ["usda"] = Model, ["usdc"] = Model, ["usdz"] = Model,
        ["max"] = Model, ["ma"] = Model, ["mb"] = Model, ["c4d"] = Model, ["zpr"] = Model,
        ["ztl"] = Model, ["spp"] = Model, ["sbs"] = Model, ["sbsar"] = Model, ["mset"] = Model,
        ["vox"] = Model, ["qb"] = Model,

        // --- Audio. No Music/SFX split: nothing in an extension distinguishes a score from an
        // ambience loop, and a filter built on a guess is worse than no filter.
        ["wav"] = Audio, ["mp3"] = Audio, ["ogg"] = Audio, ["flac"] = Audio, ["aiff"] = Audio,
        ["aif"] = Audio, ["m4a"] = Audio, ["opus"] = Audio, ["wma"] = Audio,
        ["mid"] = Audio, ["midi"] = Audio, ["mod"] = Audio, ["xm"] = Audio, ["it"] = Audio,
        ["s3m"] = Audio,
        // DAW and middleware project files — the sources a composer actually edits.
        ["rpp"] = Audio, ["flp"] = Audio, ["als"] = Audio, ["logicx"] = Audio, ["ptx"] = Audio,
        ["bank"] = Audio, ["fspro"] = Audio, ["wproj"] = Audio,

        ["mp4"] = Video, ["mov"] = Video, ["avi"] = Video, ["mkv"] = Video, ["webm"] = Video,
        ["wmv"] = Video, ["m4v"] = Video, ["prproj"] = Video, ["aep"] = Video, ["kdenlive"] = Video,

        ["ttf"] = Font, ["otf"] = Font, ["woff"] = Font, ["woff2"] = Font, ["fnt"] = Font,
        ["fon"] = Font, ["bdf"] = Font,

        // --- Text: docs, data and the things engines keep as text. Shaders and scripts live here
        // rather than in a category of their own — an author looking for "the text files" wants
        // both, and a Scripts filter would need a code index to be worth anything.
        ["txt"] = Text, ["md"] = Text, ["json"] = Text, ["xml"] = Text, ["yaml"] = Text,
        ["yml"] = Text, ["csv"] = Text, ["tsv"] = Text, ["toml"] = Text, ["ini"] = Text,
        ["ink"] = Text, ["yarn"] = Text, ["twee"] = Text, ["fountain"] = Text, ["rpy"] = Text,
        ["cs"] = Text, ["gd"] = Text, ["lua"] = Text, ["py"] = Text, ["cpp"] = Text, ["h"] = Text,
        ["hlsl"] = Text, ["glsl"] = Text, ["shader"] = Text, ["gdshader"] = Text, ["cginc"] = Text,
        ["usf"] = Text, ["ush"] = Text,

        // --- Engine files. Kept as Other rather than invented into a new kind: they are neither
        // an image nor a document, and grouping a Unity scene with a text file would be a lie of
        // convenience. They matter enough to be indexed, which is the point.
        ["unity"] = Other, ["prefab"] = Other, ["asset"] = Other, ["mat"] = Other,
        ["anim"] = Other, ["controller"] = Other, ["unitypackage"] = Other, ["shadergraph"] = Other,
        ["uasset"] = Other, ["umap"] = Other,
        ["tscn"] = Other, ["tres"] = Other, ["escn"] = Other, ["godot"] = Other,
        ["tmx"] = Other, ["tsx"] = Other, ["ldtk"] = Other, ["tiled-project"] = Other,
    };

    /// <summary>
    /// Extensions a thumbnail can actually be produced for. Two groups: what the image library
    /// decodes, and <c>.blend</c>, which carries its own preview (see <see cref="BlendThumbnail"/>).
    ///
    /// Being precise here is what keeps the grid honest — the alternative is asking for a thumbnail
    /// of every image and letting a broken-image icon appear for the ones (PSD, EXR, Aseprite) that
    /// no decoder here can open.
    /// </summary>
    private static readonly HashSet<string> Previewable = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "webp", "tga", "tif", "tiff", "pbm", "qoi",
        "blend", "blend1", "blend2",
    };

    /// <summary>Whether a thumbnail can be produced for this file at all.</summary>
    public static bool CanPreview(string? path) => Previewable.Contains(ExtensionOf(path));

    /// <summary>True for the files whose preview comes from inside the file rather than a decoder.</summary>
    public static bool HasEmbeddedPreview(string? path) =>
        ExtensionOf(path) is "blend" or "blend1" or "blend2";

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
