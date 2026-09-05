using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Execution;
using CedarClerk.Cli.Parsing;
using CedarClerk.Cli.Server;

namespace CedarClerk.Cli.Tests;

// Nothing here touches the network: every remote answer comes from FakeCommandRunner. A test suite
// that needs fra1 to be reachable is a test suite that fails on a train.
public class ProbeTests
{
    private static ServerProbe Probe(ICommandRunner runner, HealthReport? health = null) =>
        new(runner, new OfflineHealthProbe(health), new CliConfig());

    [Fact]
    public void A_whole_snapshot_comes_out_of_one_round_trip()
    {
        var runner = new FakeCommandRunner().Answer("=== service ===", Fixtures.Probe());
        var snapshot = Probe(runner).Interpret(
            new CommandResult(0, Fixtures.Probe(), "", TimeSpan.Zero),
            HealthParser.Parse(Fixtures.Health),
            24);

        Assert.True(snapshot.Reachable);
        Assert.True(snapshot.Service.IsActive);
        Assert.True(snapshot.TunnelActive);
        Assert.Equal("0.11.0", snapshot.Health.Version);
        Assert.Equal(15011840, snapshot.DatabaseBytes);
        Assert.Equal(2896392, snapshot.WalBytes);
        Assert.Equal(980371270, snapshot.DataSizes["media"]);
        Assert.Equal(5, snapshot.CpuHistory.Count);
        Assert.Equal(2, snapshot.MemoryHistory.Count);
    }

    [Fact]
    public async Task One_ssh_call_carries_the_whole_probe()
    {
        // Latency to fra1 dominates; ten logins would make `watch` feel broken.
        var runner = new FakeCommandRunner().Answer("=== service ===", Fixtures.Probe());
        await Probe(runner).TakeAsync(24, CancellationToken.None);

        Assert.Single(runner.RemoteCalls);
    }

    [Fact]
    public void A_failed_login_reads_as_unreachable_and_not_as_an_idle_machine()
    {
        // Zeroes everywhere look exactly like a healthy quiet server, which is the worst possible
        // way to render "I could not connect".
        var snapshot = Probe(new FakeCommandRunner()).Interpret(
            new CommandResult(255, "", "Permission denied (publickey).", TimeSpan.Zero),
            HealthReport.Down,
            24);

        Assert.False(snapshot.Reachable);
        Assert.Contains("publickey", snapshot.Error);
    }

    [Fact]
    public void Missing_sections_do_not_take_the_snapshot_down()
    {
        // An older droplet without sysstat still has to produce a status screen.
        var partial = string.Join('\n', "=== service ===", Fixtures.SystemctlShow, "=== end ===");
        var snapshot = Probe(new FakeCommandRunner()).Interpret(
            new CommandResult(0, partial, "", TimeSpan.Zero), HealthReport.Down, 24);

        Assert.True(snapshot.Reachable);
        Assert.True(snapshot.Service.IsActive);
        Assert.Empty(snapshot.CpuHistory);
        Assert.Equal(DiskUsage.Unknown, snapshot.Disk);
    }

    [Fact]
    public void The_history_window_is_measured_in_ten_minute_samples()
    {
        var snapshot = Probe(new FakeCommandRunner()).Interpret(
            new CommandResult(0, Fixtures.Probe(), "", TimeSpan.Zero), HealthReport.Down, 24);

        var shorter = Probe(new FakeCommandRunner()).Interpret(
            new CommandResult(0, Fixtures.Probe(), "", TimeSpan.Zero), HealthReport.Down, 0);

        Assert.Equal(5, snapshot.CpuHistory.Count);
        // Six samples an hour, so even a nonsense window keeps at least one point.
        Assert.True(shorter.CpuHistory.Count >= 1);
    }

    [Fact]
    public void The_live_version_is_compared_against_this_working_copy()
    {
        var matching = Probe(new FakeCommandRunner()).Interpret(
            new CommandResult(0, Fixtures.Probe(), "", TimeSpan.Zero),
            new HealthReport(CedarClerk.Core.Consts.CurrentVersion, "Production", false, null), 24);

        var drifted = Probe(new FakeCommandRunner()).Interpret(
            new CommandResult(0, Fixtures.Probe(), "", TimeSpan.Zero),
            new HealthReport("0.0.1", "Production", false, null), 24);

        Assert.True(matching.VersionMatches);
        Assert.False(drifted.VersionMatches);
    }

    [Fact]
    public void The_probe_script_never_uses_a_double_quote()
    {
        // It travels as one argv through Windows argument quoting into ssh; a double quote in there
        // would have to survive two layers of escaping to arrive intact.
        Assert.DoesNotContain('"', Probe(new FakeCommandRunner()).BuildScript());
    }

    [Fact]
    public void The_status_screen_reads_the_backups_through_the_one_shared_glob()
    {
        // `status`, `backup verify` and the deploy preflight look in the same place, by name -
        // backup.log lives there too and is touched after every run, so a bare glob would report
        // the log as the newest copy.
        var script = Probe(new FakeCommandRunner()).BuildScript();

        Assert.Contains(ServerProbe.BackupCopies(new CliConfig().RemoteDataDir), script);
        Assert.EndsWith("/backups/cedar-*.db.gz", ServerProbe.BackupCopies("/data"));
        Assert.DoesNotContain("backups/*", script);
    }

    [Fact]
    public void Sections_split_on_their_markers_and_keep_their_bodies()
    {
        var sections = ServerProbe.Split("=== a ===\none\ntwo\n=== b ===\nthree\n=== end ===");

        Assert.Equal("one\ntwo", sections["a"]);
        Assert.Equal("three", sections["b"]);
        Assert.True(sections.ContainsKey("end"));
    }
}
