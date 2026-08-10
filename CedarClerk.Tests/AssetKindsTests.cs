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

    [Theory]
    // Marty, 10.08.2026 — the formats he actually works in have to be recognised, not just the
    // ones a web app would think of. One per family, so a deleted line is a visible loss.
    [InlineData("scenes/ferry.blend", AssetKinds.Model)]
    [InlineData("sculpt/collector.ztl", AssetKinds.Model)]
    [InlineData("mats/rust.sbsar", AssetKinds.Model)]
    [InlineData("art/tiles.aseprite", AssetKinds.Image)]
    [InlineData("art/poster.kra", AssetKinds.Image)]
    [InlineData("art/sheet.psd", AssetKinds.Image)]
    [InlineData("music/theme.flp", AssetKinds.Audio)]
    [InlineData("audio/session.rpp", AssetKinds.Audio)]
    [InlineData("audio/Master.bank", AssetKinds.Audio)]
    [InlineData("trailer/cut.prproj", AssetKinds.Video)]
    [InlineData("ui/pixel.bdf", AssetKinds.Font)]
    [InlineData("dialogue/act1.fountain", AssetKinds.Text)]
    [InlineData("shaders/water.gdshader", AssetKinds.Text)]
    [InlineData("Scripts/Player.cs", AssetKinds.Text)]
    // Engine files are indexed but stay Other: a Unity scene is neither an image nor a document,
    // and filing it under Text to have somewhere to put it would be a lie of convenience.
    [InlineData("Scenes/Level01.unity", AssetKinds.Other)]
    [InlineData("Content/Hero.uasset", AssetKinds.Other)]
    [InlineData("scenes/main.tscn", AssetKinds.Other)]
    public void The_formats_a_game_project_actually_holds_are_recognised(string path, string expected)
    {
        Assert.Equal(expected, AssetKinds.FromPath(path));
        Assert.True(AssetKinds.ShouldIndex(path));
    }

    [Fact]
    public void Only_the_files_a_thumbnail_can_be_made_from_claim_one()
    {
        Assert.True(AssetKinds.CanPreview("a.png"));
        Assert.True(AssetKinds.CanPreview("a.tga"));
        Assert.True(AssetKinds.CanPreview("a.blend"));
        // Images the decoder here cannot open. Claiming a thumbnail for these is how a grid full
        // of broken-image icons happens.
        Assert.False(AssetKinds.CanPreview("a.psd"));
        Assert.False(AssetKinds.CanPreview("a.exr"));
        Assert.False(AssetKinds.CanPreview("a.aseprite"));
        Assert.False(AssetKinds.CanPreview("a.fbx"));
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
