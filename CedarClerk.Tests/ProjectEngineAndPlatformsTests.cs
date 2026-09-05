using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-247. The update request's two new fields mean three things each — null leaves the column
// alone, empty clears it, a value replaces it — and a key outside the Core list is refused rather
// than quietly dropped.
public class ProjectEngineAndPlatformsTests
{
    private static Project Seeded() => new() { OwnerId = "o", Name = "Q", Engine = ProjectEngines.Godot, TargetPlatforms = "windows,switch" };

    [Fact]
    public void Null_leaves_both_fields_as_they_were()
    {
        var project = Seeded();

        Assert.Null(ProjectEndpoints.ApplyEngineAndPlatforms(project, null, null));

        Assert.Equal(ProjectEngines.Godot, project.Engine);
        Assert.Equal("windows,switch", project.TargetPlatforms);
    }

    [Fact]
    public void Empty_clears_and_a_value_replaces()
    {
        var project = Seeded();

        Assert.Null(ProjectEndpoints.ApplyEngineAndPlatforms(project, "", []));
        Assert.Equal("", project.Engine);
        Assert.Equal("", project.TargetPlatforms);

        Assert.Null(ProjectEndpoints.ApplyEngineAndPlatforms(project, ProjectEngines.Unity, [ProjectPlatforms.Switch, ProjectPlatforms.Windows, ProjectPlatforms.Switch]));
        Assert.Equal(ProjectEngines.Unity, project.Engine);
        // Stored in the list's order, deduplicated, whatever order the client sent.
        Assert.Equal("windows,switch", project.TargetPlatforms);
        Assert.Equal([ProjectPlatforms.Windows, ProjectPlatforms.Switch], ProjectPlatforms.Parse(project.TargetPlatforms));
    }

    [Fact]
    public void An_unknown_engine_is_refused_and_nothing_moves()
    {
        var project = Seeded();

        var error = ProjectEndpoints.ApplyEngineAndPlatforms(project, "amiga-basic", [ProjectPlatforms.Web]);

        Assert.Contains("amiga-basic", error);
        Assert.Equal(ProjectEngines.Godot, project.Engine);
        Assert.Equal("windows,switch", project.TargetPlatforms);
    }

    [Fact]
    public void An_unknown_platform_is_refused_by_name()
    {
        var project = Seeded();

        var error = ProjectEndpoints.ApplyEngineAndPlatforms(project, null, [ProjectPlatforms.Web, "c64"]);

        Assert.Contains("c64", error);
        Assert.Equal("windows,switch", project.TargetPlatforms);
    }
}
