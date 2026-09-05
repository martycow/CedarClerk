using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// T-246. The hub used to fetch the whole build list to draw one count; the summary row now carries
// the count and the newest shipped version, and "shipped" is the word that matters — a planned
// build has no version anyone can download.
public class ProjectBuildSummaryTests
{
    private static Build Build(string owner, Guid project, string version, DateTime? releasedAt) =>
        new() { OwnerId = owner, ProjectId = project, Version = version, ReleasedAt = releasedAt };

    [Fact]
    public async Task Counts_every_build_and_names_the_newest_released_version()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            seed.Builds.AddRange(
                Build(owner, project, "0.1.0", new DateTime(2026, 7, 1)),
                Build(owner, project, "0.2.0", new DateTime(2026, 8, 1)),
                Build(owner, project, "0.3.0", null));
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var summary = Assert.Single(await ProjectEndpoints.BuildSummariesAsync(db, owner));

        Assert.Equal(project, summary.Key);
        Assert.Equal(3, summary.Value.Count);
        Assert.Equal("0.2.0", summary.Value.LatestVersion);
    }

    [Fact]
    public async Task Planned_only_builds_count_but_carry_no_version()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var project = fx.Project(owner);
        await using (var seed = fx.Platform())
        {
            seed.Builds.Add(Build(owner, project, "Demo 3", null));
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var summary = Assert.Single(await ProjectEndpoints.BuildSummariesAsync(db, owner));

        Assert.Equal(1, summary.Value.Count);
        Assert.Null(summary.Value.LatestVersion);
    }

    [Fact]
    public async Task Another_owners_builds_are_not_counted()
    {
        using var fx = new CanvasFixture();
        var owner = fx.User("owner");
        var stranger = fx.User("stranger");
        var mine = fx.Project(owner);
        var theirs = fx.Project(stranger, "Theirs");
        await using (var seed = fx.Platform())
        {
            seed.Builds.Add(Build(stranger, theirs, "1.0", new DateTime(2026, 8, 1)));
            await seed.SaveChangesAsync();
        }

        await using var db = fx.As(owner);
        var summaries = await ProjectEndpoints.BuildSummariesAsync(db, owner);

        Assert.Empty(summaries);
        Assert.Null(summaries.GetValueOrDefault(mine));
    }
}
