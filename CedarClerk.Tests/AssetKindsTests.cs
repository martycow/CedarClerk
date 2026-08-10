using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-122 (ADR-107). The classifier decides what a scan even records, so the two rules worth pinning
// are: an unknown file is never lost to a wrong guess, and a cache folder never becomes rows.
public class AssetKindsTests
{
    [Theory]
    [InlineData("hero_idle.png", AssetKinds.Image)]
    [InlineData("C:/games/art/Fog_LUT.PSD", AssetKinds.Image)]
    [InlineData("props/terminal.fbx", AssetKinds.Model)]
    [InlineData("audio/rain_loop.wav", AssetKinds.Audio)]
    [InlineData("music/theme.mid", AssetKinds.Audio)]
    [InlineData("cutscene.mp4", AssetKinds.Video)]
    [InlineData("ui/Inter.ttf", AssetKinds.Font)]
    [InlineData("dialogue/act1.ink", AssetKinds.Text)]
    public void Extensions_map_to_kinds(string path, string expected)
    {
        Assert.Equal(expected, AssetKinds.FromPath(path));
    }

    [Theory]
    [InlineData("notes")]                 // no extension at all
    [InlineData("archive.7z")]
    [InlineData("weird.qqq")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_unrecognised_is_other_rather_than_nothing(string? path)
    {
        // Never null: a row that exists has to be showable, and "other" is a true answer where a
        // guess would not be.
        Assert.Equal(AssetKinds.Other, AssetKinds.FromPath(path));
        Assert.True(AssetKinds.IsKnown(AssetKinds.FromPath(path)));
    }

    [Fact]
    public void Case_and_paths_do_not_change_the_answer()
    {
        Assert.Equal(AssetKinds.Model, AssetKinds.FromPath("A.FBX"));
        Assert.Equal("fbx", AssetKinds.ExtensionOf(@"D:\Projects\MyGame\Assets\A.FBX"));
        Assert.Equal("", AssetKinds.ExtensionOf("Makefile"));
        // A dotfile is a name, not an extension — ".gitignore" must not read as kind "gitignore".
        Assert.Equal("gitignore", AssetKinds.ExtensionOf(".gitignore"));
    }

    [Fact]
    public void Only_recognised_files_are_indexed()
    {
        // The filter runs during the walk, which is what stops a Unity project's cache from
        // becoming a hundred thousand rows nobody asked for.
        Assert.True(AssetKinds.ShouldIndex("sprite.png"));
        Assert.False(AssetKinds.ShouldIndex("sprite.png.meta"));
        Assert.False(AssetKinds.ShouldIndex("Library/artifacts/3f/3fa8c"));
        Assert.False(AssetKinds.ShouldIndex("build.exe"));
    }

    [Fact]
    public void Engine_caches_and_version_control_are_skipped_by_name()
    {
        foreach (var directory in new[] { "Library", "Temp", "node_modules", ".git", "Intermediate", ".godot" })
            Assert.Contains(directory, AssetKinds.SkippedDirectories);

        // Case-insensitively, because these names are written differently by different engines.
        Assert.Contains("library", AssetKinds.SkippedDirectories);
        // And a folder that merely resembles one is not skipped — "Libraries" is somebody's art.
        Assert.DoesNotContain("Libraries", AssetKinds.SkippedDirectories);
    }
}
