using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-247. The column is a comma-joined closed vocabulary; what matters is that nothing outside the
// list survives a round trip and that the stored order is the list's, whatever the client sent.
public class ProjectPlatformsTests
{
    [Fact]
    public void Parse_keeps_list_order_and_drops_unknown_and_duplicates()
    {
        var parsed = ProjectPlatforms.Parse("switch,windows,amiga,windows, mac ");

        Assert.Equal([ProjectPlatforms.Windows, ProjectPlatforms.Mac, ProjectPlatforms.Switch], parsed);
    }

    [Fact]
    public void Parse_of_empty_or_null_is_empty()
    {
        Assert.Empty(ProjectPlatforms.Parse(""));
        Assert.Empty(ProjectPlatforms.Parse(null));
        Assert.Empty(ProjectPlatforms.Parse(" , "));
    }

    [Fact]
    public void Join_writes_list_order_without_spaces()
    {
        Assert.Equal("windows,switch", ProjectPlatforms.Join(["switch", "windows", "switch", "c64"]));
        Assert.Equal("", ProjectPlatforms.Join([]));
    }

    [Fact]
    public void Join_and_parse_round_trip()
    {
        var stored = ProjectPlatforms.Join(ProjectPlatforms.All.Reverse());

        Assert.Equal(ProjectPlatforms.All, ProjectPlatforms.Parse(stored));
        Assert.Equal(stored, ProjectPlatforms.Join(ProjectPlatforms.Parse(stored)));
    }

    [Fact]
    public void Engines_are_a_closed_list()
    {
        Assert.True(ProjectEngines.IsKnown(ProjectEngines.Godot));
        Assert.False(ProjectEngines.IsKnown("source2"));
        Assert.False(ProjectEngines.IsKnown(null));
        Assert.Equal(ProjectEngines.All.Count, ProjectEngines.All.Distinct().Count());
    }
}
