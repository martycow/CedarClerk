using System.Text.RegularExpressions;

namespace CedarClerk.Cli.Parsing;

public sealed record TestSummary(int Passed, int Failed, int Skipped)
{
    public int Total => Passed + Failed + Skipped;
}

public sealed class TestPhase
{
    public string Name { get; init; } = "";
    public List<TestEvent> Events { get; } = new();
    public TestSummary? Summary { get; set; }
    public bool Reconciled { get; set; }
}

// Follows a test run line by line and keeps a live tally for the grid.
//
// More than a line parser because the runners report at different granularities: dotnet test and
// Playwright name every test, while `ng test` reports nothing until a two-line summary. Counting
// only what is named would show 700 ticks for a suite of 750 and under-report the frontend, so each
// phase is reconciled against that runner's own summary when it closes. Nothing is invented — the
// numbers come from the runner, only late. Whether the run PASSED is the exit code, never this
// tally (ADR-118).
public sealed class TestRunTracker
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex PhaseHeader = new(@"^===\s+(?<name>.+?)\s+===$", RegexOptions.Compiled);

    private static readonly Regex DotnetSummary = new(
        @"Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+)",
        RegexOptions.Compiled);

    // "Tests  18 passed (18)" / "Tests  1 failed | 17 passed (18)" — and never "Test Files".
    private static readonly Regex VitestSummary = new(
        @"^\s*Tests\s+(?<body>.+?)\s*\((?<total>\d+)\)\s*$", RegexOptions.Compiled);

    private static readonly Regex VitestPart = new(
        @"(?<count>\d+)\s+(?<kind>passed|failed|skipped|todo)", RegexOptions.Compiled);

    // Playwright's tail: "  53 passed (45.0s)", "  2 failed".
    private static readonly Regex PlaywrightSummary = new(
        @"^\s*(?<count>\d+)\s+(?<kind>passed|failed|skipped|flaky)\b", RegexOptions.Compiled);

    private readonly List<TestPhase> _phases = new();
    private readonly HashSet<string>? _known;
    private TestPhase? _current;

    // The pipeline is not the only thing printing "=== name ===" — check-contrast.mjs uses the same
    // shape for "light" and "dark", and they appeared as two empty phases. Naming the steps that
    // count keeps those inside the phase that ran them. An unnamed header is ignored rather than
    // dropped: its results land in the phase already open, so a step added later loses its own row
    // but never its tally.
    public TestRunTracker(IEnumerable<string>? knownPhases = null) =>
        _known = knownPhases is null ? null : new HashSet<string>(knownPhases, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TestPhase> Phases => _phases;
    public string LastLine { get; private set; } = "";

    public IEnumerable<TestEvent> AllEvents => _phases.SelectMany(p => p.Events);

    public int Passed => AllEvents.Count(e => e.Outcome == TestOutcome.Passed);
    public int Failed => AllEvents.Count(e => e.Outcome == TestOutcome.Failed);
    public int Skipped => AllEvents.Count(e => e.Outcome == TestOutcome.Skipped);
    public int Total => Passed + Failed + Skipped;

    public IEnumerable<TestEvent> Failures => AllEvents.Where(e => e.Outcome == TestOutcome.Failed);

    public void Feed(string rawLine)
    {
        // npm and vitest colour their output; the escapes would break every anchored pattern here.
        var line = Ansi.Replace(rawLine ?? "", "").TrimEnd('\r');
        if (line.Trim().Length > 0) LastLine = line.Trim();

        var header = PhaseHeader.Match(line.Trim());
        if (header.Success && (_known is null || _known.Contains(header.Groups["name"].Value)
                                             || header.Groups["name"].Value.Equals("Summary", StringComparison.OrdinalIgnoreCase)))
        {
            Reconcile();
            var name = header.Groups["name"].Value;
            // test.ps1 ends with its own "=== Summary ===" block, which is a report and not a phase.
            _current = name.Equals("Summary", StringComparison.OrdinalIgnoreCase)
                ? null
                : new TestPhase { Name = name };
            if (_current is not null) _phases.Add(_current);
            return;
        }

        if (_current is null) return;

        var events = TestEventParser.Parse(line);
        if (events.Count > 0)
        {
            _current.Events.AddRange(events);
            return;
        }

        var summary = ParseSummary(line);
        if (summary is not null) _current.Summary = Merge(_current.Summary, summary);
    }

    // Called when a phase ends and once when the process does.
    public void Reconcile()
    {
        if (_current is null || _current.Reconciled) return;
        _current.Reconciled = true;

        var summary = _current.Summary;
        if (summary is null) return;

        Top(_current, TestOutcome.Passed, summary.Passed);
        Top(_current, TestOutcome.Failed, summary.Failed);
        Top(_current, TestOutcome.Skipped, summary.Skipped);
    }

    public void Finish() => Reconcile();

    private static void Top(TestPhase phase, TestOutcome outcome, int reported)
    {
        var seen = phase.Events.Count(e => e.Outcome == outcome);
        for (var i = seen; i < reported; i++)
            phase.Events.Add(new TestEvent(outcome, $"{phase.Name} #{i + 1}"));
    }

    internal static TestSummary? ParseSummary(string line)
    {
        var dotnet = DotnetSummary.Match(line);
        if (dotnet.Success)
            return new TestSummary(
                int.Parse(dotnet.Groups["passed"].Value),
                int.Parse(dotnet.Groups["failed"].Value),
                int.Parse(dotnet.Groups["skipped"].Value));

        var vitest = VitestSummary.Match(line);
        if (vitest.Success)
        {
            int passed = 0, failed = 0, skipped = 0;
            foreach (Match part in VitestPart.Matches(vitest.Groups["body"].Value))
            {
                var count = int.Parse(part.Groups["count"].Value);
                switch (part.Groups["kind"].Value)
                {
                    case "passed": passed += count; break;
                    case "failed": failed += count; break;
                    default: skipped += count; break;
                }
            }
            return passed + failed + skipped == 0 ? null : new TestSummary(passed, failed, skipped);
        }

        var playwright = PlaywrightSummary.Match(line);
        if (playwright.Success)
        {
            var count = int.Parse(playwright.Groups["count"].Value);
            return playwright.Groups["kind"].Value switch
            {
                "passed" => new TestSummary(count, 0, 0),
                "failed" or "flaky" => new TestSummary(0, count, 0),
                _ => new TestSummary(0, 0, count)
            };
        }

        return null;
    }

    // Playwright reports each outcome on a line of its own ("53 passed", then "2 failed"), and a
    // multi-project dotnet run prints one summary per project — both are additions, not corrections.
    private static TestSummary Merge(TestSummary? existing, TestSummary addition) =>
        existing is null
            ? addition
            : new TestSummary(
                existing.Passed + addition.Passed,
                existing.Failed + addition.Failed,
                existing.Skipped + addition.Skipped);
}
