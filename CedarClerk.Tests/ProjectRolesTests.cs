using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-301 / ADR-217. The one thing worth pinning: "owner" is never a role. A role column that could
// hold it would be one row edit away from handing the project over.
public class ProjectRolesTests
{
    [Fact]
    public void Only_the_two_handed_out_roles_are_known()
    {
        Assert.Equal(["editor", "viewer"], ProjectRoles.All);
        Assert.All(ProjectRoles.All, role => Assert.True(ProjectRoles.IsKnown(role)));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("Editor")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_unknown(string? role)
    {
        Assert.False(ProjectRoles.IsKnown(role));
    }

    [Fact]
    public void Only_an_editor_writes()
    {
        Assert.True(ProjectRoles.CanWrite(ProjectRoles.Editor));
        Assert.False(ProjectRoles.CanWrite(ProjectRoles.Viewer));
        Assert.False(ProjectRoles.CanWrite("owner"));
        Assert.False(ProjectRoles.CanWrite(null));
    }
}

// The kinds a board can draw. `All` is alphabetical rather than in tool-strip order — the strip
// decides what it shows, and a list used for validation should not move when a button does.
public class CanvasItemKindsTests
{
    [Fact]
    public void The_four_kinds_are_known_and_ordered()
    {
        Assert.Equal(["frame", "image", "link", "note"], CanvasItemKinds.All);
        Assert.All(CanvasItemKinds.All, kind => Assert.True(CanvasItemKinds.IsKnown(kind)));
    }

    [Theory]
    [InlineData("sticker")]
    [InlineData("Note")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? kind)
    {
        Assert.False(CanvasItemKinds.IsKnown(kind));
    }

    [Fact]
    public void The_default_kind_is_one_of_them()
    {
        Assert.Contains(CanvasItemKinds.Note, CanvasItemKinds.All);
    }
}

public class CanvasBackgroundsTests
{
    [Fact]
    public void The_three_grounds_are_known_and_grid_is_the_default()
    {
        Assert.Equal(["blank", "dots", "grid"], CanvasBackgrounds.All);
        Assert.All(CanvasBackgrounds.All, value => Assert.True(CanvasBackgrounds.IsKnown(value)));
        Assert.Contains(CanvasBackgrounds.Grid, CanvasBackgrounds.All);
    }

    [Theory]
    [InlineData("lined")]
    [InlineData("Grid")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? value)
    {
        Assert.False(CanvasBackgrounds.IsKnown(value));
    }
}
