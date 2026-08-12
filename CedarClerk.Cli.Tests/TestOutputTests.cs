using CedarClerk.Cli.Commands;
using CedarClerk.Cli.Configuration;
using CedarClerk.Cli.Parsing;
using CedarClerk.Cli.Rendering;
using Spectre.Console.Testing;

namespace CedarClerk.Cli.Tests;

// The grid of ticks is decoration; these tests are about the promise underneath it — that it counts
// what the runners actually reported, and that it goes quiet rather than lying when it cannot.
public class TestOutputTests
{
    [Fact]
    public void Reads_a_dotnet_test_line()
    {
        var events = TestEventParser.Parse("  Passed CedarClerk.Tests.InviteCodeRulesTests.Null_max_uses_means_unlimited [< 1 ms]");

        var single = Assert.Single(events);
        Assert.Equal(TestOutcome.Passed, single.Outcome);
        Assert.Equal("CedarClerk.Tests.InviteCodeRulesTests.Null_max_uses_means_unlimited", single.Name);
    }

    [Fact]
    public void Reads_a_theory_case_with_its_arguments()
    {
        var events = TestEventParser.Parse(
            "  Passed CedarClerk.Tests.InviteCodeRulesTests.Use_cap_admits_exactly_max_uses_accounts(uses: 5, expected: False) [1 ms]");

        Assert.Contains("uses: 5", Assert.Single(events).Name);
    }

    [Fact]
    public void Reads_failures_and_skips()
    {
        Assert.Equal(TestOutcome.Failed, TestEventParser.Parse("  Failed Some.Test.Name [12 ms]").Single().Outcome);
        Assert.Equal(TestOutcome.Skipped, TestEventParser.Parse("  Skipped Some.Test.Name").Single().Outcome);
    }

    [Fact]
    public void The_dotnet_summary_line_is_not_mistaken_for_a_test()
    {
        // "     Passed: 10" is a count, not a result, and counting it would add a phantom tick.
        Assert.Empty(TestEventParser.Parse("     Passed: 10"));
        Assert.Empty(TestEventParser.Parse("Total tests: 10"));
    }

    [Fact]
    public void A_vitest_file_line_stands_for_every_test_inside_it()
    {
        var events = TestEventParser.Parse(" ✓ src/app/core/display-time.spec.ts (7 tests) 12ms");

        Assert.Equal(7, events.Count);
        Assert.All(events, e => Assert.Equal(TestOutcome.Passed, e.Outcome));
    }

    [Fact]
    public void A_vitest_verbose_line_is_one_test()
    {
        var events = TestEventParser.Parse(" ✓ src/app/core/display-time.spec.ts > display-time > shows a summer instant in PDT 7ms");
        Assert.Single(events);
    }

    [Fact]
    public void A_playwright_line_keeps_the_readable_half_of_the_name()
    {
        var events = TestEventParser.Parse("  ✓  12 [chromium] › e2e/01-auth.spec.ts:5:1 › signs in (1.2s)");

        var single = Assert.Single(events);
        Assert.Equal(TestOutcome.Passed, single.Outcome);
        Assert.Contains("signs in", single.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Determining projects to restore...")]
    [InlineData("  CedarClerk.Core -> D:\\bin\\CedarClerk.Core.dll")]
    [InlineData("random noise that Passed through")]
    public void An_unrecognised_line_produces_nothing_rather_than_a_guess(string line)
    {
        Assert.Empty(TestEventParser.Parse(line));
    }

    [Fact]
    public void The_tracker_groups_results_under_the_phase_that_printed_them()
    {
        var tracker = new TestRunTracker();
        Feed(tracker,
            "=== Backend (dotnet test) ===",
            "  Passed A.B.C [1 ms]",
            "  Passed A.B.D [1 ms]",
            "=== Frontend units (vitest) ===",
            " ✓ src/a.spec.ts (2 tests) 5ms");
        tracker.Finish();

        Assert.Equal(2, tracker.Phases.Count);
        Assert.Equal("Backend (dotnet test)", tracker.Phases[0].Name);
        Assert.Equal(2, tracker.Phases[0].Events.Count);
        Assert.Equal(2, tracker.Phases[1].Events.Count);
    }

    [Fact]
    public void A_runner_that_names_nothing_is_still_counted_from_its_own_summary()
    {
        // This is the frontend's real behaviour: `ng test` will not forward a reporter flag, so
        // vitest prints only a summary. Counting only named tests would under-report it silently.
        var tracker = new TestRunTracker();
        Feed(tracker,
            "=== Frontend units (vitest) ===",
            " Test Files  4 passed (4)",
            " Tests  18 passed (18)");
        tracker.Finish();

        Assert.Equal(18, tracker.Passed);
    }

    [Fact]
    public void Reconciliation_tops_up_rather_than_double_counting()
    {
        var tracker = new TestRunTracker();
        Feed(tracker,
            "=== Backend (dotnet test) ===",
            "  Passed A.B.C [1 ms]",
            "  Passed A.B.D [1 ms]",
            "Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5");
        tracker.Finish();

        Assert.Equal(5, tracker.Passed);
    }

    [Fact]
    public void Test_files_is_not_read_as_a_test_count()
    {
        Assert.Null(TestRunTracker.ParseSummary(" Test Files  4 passed (4)"));
    }

    [Fact]
    public void Colour_codes_do_not_stop_the_summary_being_read()
    {
        // npm and vitest colour their output; without stripping, every anchored pattern misses.
        var tracker = new TestRunTracker();
        Feed(tracker,
            "=== Frontend units (vitest) ===",
            "\u001b[2m Tests \u001b[22m \u001b[1m\u001b[32m18 passed\u001b[39m\u001b[22m\u001b[90m (18)\u001b[39m");
        tracker.Finish();

        Assert.Equal(18, tracker.Passed);
    }

    [Fact]
    public void Playwright_reports_each_outcome_on_its_own_line_and_they_add_up()
    {
        var tracker = new TestRunTracker();
        Feed(tracker,
            "=== Smoke (Playwright, isolated database) ===",
            "  51 passed (45.0s)",
            "  2 failed");
        tracker.Finish();

        Assert.Equal(51, tracker.Passed);
        Assert.Equal(2, tracker.Failed);
    }

    [Fact]
    public void The_scripts_own_summary_block_is_not_a_phase()
    {
        var tracker = new TestRunTracker();
        Feed(tracker,
            "=== Backend (dotnet test) ===",
            "  Passed A.B.C [1 ms]",
            "=== Summary ===",
            "  Backend (dotnet test)                    ok");
        tracker.Finish();

        Assert.Single(tracker.Phases);
        Assert.Equal(1, tracker.Passed);
    }

    [Fact]
    public void A_header_from_another_tool_does_not_become_a_phase()
    {
        // check-contrast.mjs prints "=== light ===" and "=== dark ===" inside the contrast step.
        var tracker = new TestRunTracker(new[] { "Contrast contract" });
        Feed(tracker,
            "=== Contrast contract ===",
            "=== light ===",
            "=== dark ===",
            "0 failing pairs");
        tracker.Finish();

        var phase = Assert.Single(tracker.Phases);
        Assert.Equal("Contrast contract", phase.Name);
    }

    [Fact]
    public void An_unknown_step_keeps_its_results_even_though_it_loses_its_row()
    {
        var tracker = new TestRunTracker(new[] { "Backend (dotnet test)" });
        Feed(tracker,
            "=== Backend (dotnet test) ===",
            "  Passed A.B.C [1 ms]",
            "=== Some Step Added Later ===",
            "  Passed A.B.D [1 ms]");
        tracker.Finish();

        Assert.Single(tracker.Phases);
        Assert.Equal(2, tracker.Passed);
    }

    [Fact]
    public void Output_before_the_first_phase_header_is_ignored()
    {
        var tracker = new TestRunTracker();
        Feed(tracker, "  Passed Stray.Test.Name [1 ms]");
        tracker.Finish();

        Assert.Equal(0, tracker.Total);
    }

    // The grid of cells (Marty, 12.08.2026): a field sized from the last run, filling in as results
    // land. The field only exists mid-run, which is the state no screenshot can be taken of.

    [Fact]
    public void The_field_is_drawn_to_the_remembered_size_and_fills_in()
    {
        var console = new TestConsole();
        console.Profile.Width = 100;
        var tracker = new TestRunTracker();
        Feed(tracker, "=== Backend (dotnet test) ===", "  Passed A.B.C [1 ms]", "  Passed A.B.D [1 ms]");

        var frame = TestCommand.Frame(
            Session.From(new TestSettings { NoLogo = true }, console, new CliConfig()),
            tracker, TimeSpan.FromSeconds(1), running: true, expected: 10);
        console.Write(frame);

        // The counters line repeats the cell glyphs as a key, so the assertion is about the grid row
        // itself rather than the whole panel.
        var grid = console.Lines.First(line => line.Contains('□'));
        Assert.Equal(2, grid.Count(c => c == '▣'));
        Assert.Equal(8, grid.Count(c => c == '□'));
    }

    [Fact]
    public void A_finished_run_shows_the_results_and_no_leftover_placeholders()
    {
        // A remembered count that turned out too high must not leave empty cells implying tests that
        // were never going to run.
        var console = new TestConsole();
        console.Profile.Width = 100;
        var tracker = new TestRunTracker();
        Feed(tracker, "=== Backend (dotnet test) ===", "  Passed A.B.C [1 ms]");
        tracker.Finish();

        console.Write(TestCommand.Frame(
            Session.From(new TestSettings { NoLogo = true }, console, new CliConfig()),
            tracker, TimeSpan.FromSeconds(1), running: false, expected: 500));

        Assert.DoesNotContain('□', console.Output);
    }

    [Fact]
    public void Every_cell_glyph_is_one_column_wide_in_both_modes()
    {
        // The grid wraps by counting results, not characters, so a two-character glyph would shear
        // every row to double width - which is what "OK"/"XX" did in ASCII mode.
        foreach (var glyphs in new[] { Glyphs.Unicode, Glyphs.Plain })
            foreach (var cell in new[] { glyphs.CellPending, glyphs.CellPassed, glyphs.CellFailed, glyphs.CellSkipped })
                Assert.Equal(1, cell.Length);
    }

    [Fact]
    public void No_cell_glyph_is_an_emoji_that_would_paint_itself()
    {
        // U+2714 is in the emoji set, so Windows renders it from Segoe UI Emoji in that font's own
        // colour and ignores the ANSI colour entirely - which is how a wall of "green" ticks came
        // out violet. Geometric Shapes and Mathematical Operators have no emoji presentation.
        foreach (var cell in new[]
                 { Glyphs.Unicode.CellPending, Glyphs.Unicode.CellPassed, Glyphs.Unicode.CellFailed, Glyphs.Unicode.CellSkipped })
            Assert.InRange(cell[0], (char)0x2200, (char)0x25FF);
    }

    private static void Feed(TestRunTracker tracker, params string[] lines)
    {
        foreach (var line in lines) tracker.Feed(line);
    }
}
