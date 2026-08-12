using CedarClerk.Cli.Parsing;

namespace CedarClerk.Cli.Tests;

public class ParserTests
{
    [Fact]
    public void Df_reads_the_data_row_and_skips_the_header()
    {
        var disk = SystemFacts.ParseDf(Fixtures.Df);

        Assert.Equal(49691512L * 1024, disk.TotalBytes);
        Assert.Equal(44796252L * 1024, disk.FreeBytes);
        Assert.InRange(disk.UsedFraction, 0.09, 0.11);
    }

    [Fact]
    public void Free_prefers_available_over_free_for_the_pressure_reading()
    {
        var memory = SystemFacts.ParseFree(Fixtures.Free);

        Assert.Equal(2063581184, memory.TotalBytes);
        Assert.Equal(1467576320, memory.AvailableBytes);
        // 596 MB of 2 GB is "used", but 1.47 GB is available — the second number is the true one,
        // because buff/cache is reclaimable.
        Assert.InRange(memory.UsedFraction, 0.28, 0.30);
    }

    [Fact]
    public void Free_reports_a_machine_with_no_swap_as_having_none()
    {
        Assert.Equal(0, SystemFacts.ParseFree(Fixtures.Free).SwapTotalBytes);
    }

    [Fact]
    public void Systemctl_show_reads_state_and_the_boot_behaviour_that_T143_is_about()
    {
        var service = SystemFacts.ParseSystemctlShow(Fixtures.SystemctlShow);

        Assert.True(service.IsActive);
        Assert.Equal("running", service.SubState);
        Assert.Equal(49780, service.MainPid);
        Assert.Equal(186736640, service.MemoryBytes);
        Assert.False(service.SurvivesReboot);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 8, 6, 57, TimeSpan.Zero), service.SinceUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("ActiveState=")]
    public void Parsers_return_an_unknown_value_rather_than_throwing(string garbage)
    {
        // A status screen that crashes because one field moved is worse than one that renders and
        // says it could not read that field.
        Assert.Equal(DiskUsage.Unknown, SystemFacts.ParseDf(garbage));
        Assert.Equal(MemoryUsage.Unknown, SystemFacts.ParseFree(garbage));
        Assert.NotNull(SystemFacts.ParseSystemctlShow(garbage));
    }

    [Fact]
    public void Sar_reads_the_named_column_and_drops_the_banner_and_the_average()
    {
        var busy = SarParser.ParseCpuBusy(Fixtures.SarCpu);

        Assert.Equal(5, busy.Count);
        Assert.Equal(100 - 91.90, busy[0].Value, 3);
        Assert.Equal(100 - 87.01, busy[^1].Value, 3);
    }

    [Fact]
    public void Sar_memory_reads_percent_used_by_name_not_by_position()
    {
        var used = SarParser.Parse(Fixtures.SarMemory, "%memused");

        Assert.Equal(2, used.Count);
        Assert.Equal(10.24, used[0].Value, 3);
        Assert.Equal(9.40, used[1].Value, 3);
    }

    [Fact]
    public void Sar_survives_a_column_that_is_not_there()
    {
        Assert.Empty(SarParser.Parse(Fixtures.SarCpu, "%nonexistent"));
    }

    [Fact]
    public void Sar_handles_a_twelve_hour_locale()
    {
        var text = string.Join('\n',
            "12:00:01 AM        CPU     %user     %idle",
            "01:10:01 PM        all      5.00     95.00");

        var busy = SarParser.ParseCpuBusy(text);

        Assert.Single(busy);
        Assert.Equal(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1), busy[0].Time);
    }

    [Fact]
    public void Du_keeps_the_path_as_the_key()
    {
        var sizes = SystemFacts.ParseDu(Fixtures.Du);

        Assert.Equal(980371270, sizes["/home/martycow/cedarclerk/data/media"]);
        Assert.Equal(3, sizes.Count);
    }

    [Fact]
    public void Health_reads_the_version_that_is_actually_serving()
    {
        var health = HealthParser.Parse(Fixtures.Health);

        Assert.True(health.Answered);
        Assert.Equal("0.11.0", health.Version);
        Assert.Equal("Production", health.Environment);
        Assert.False(health.OpenRegistration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{")]
    public void Health_treats_anything_unparseable_as_down(string body)
    {
        Assert.False(HealthParser.Parse(body).Answered);
    }

    [Fact]
    public void Journal_drops_the_hint_block_that_looks_like_a_permission_error()
    {
        var lines = JournalParser.Parse(Fixtures.Journal);

        Assert.DoesNotContain(lines, line => line.Text.StartsWith("You are currently not seeing"));
        Assert.DoesNotContain(lines, line => line.Text.Contains("systemd-journal"));
    }

    [Fact]
    public void Journal_marks_indented_lines_as_continuations_of_the_line_above()
    {
        var lines = JournalParser.Parse(Fixtures.Journal);

        var executed = lines.First(l => l.Text.StartsWith("Executed DbCommand"));
        Assert.Equal(LogLevel.Continuation, executed.Level);

        // The colour must follow the line that introduced it, or a stack trace under a failure
        // turns grey and stops looking like part of the failure.
        var index = lines.ToList().IndexOf(executed);
        Assert.Equal(LogLevel.Info, JournalParser.EffectiveLevel(lines, index));
    }

    [Fact]
    public void Journal_reads_the_level_prefix_and_the_timestamp()
    {
        var lines = JournalParser.Parse(Fixtures.Journal);
        var warning = lines.First(l => l.Level == LogLevel.Warn);

        Assert.Equal("Aug 12 08:17:45", warning.Timestamp);
        Assert.Contains("CedarClerk.Server.Bot", warning.Text);
        Assert.True(warning.IsProblem);
    }
}
