using System.Text.RegularExpressions;

namespace CedarClerk.Cli.Parsing;

public enum TestOutcome { Passed, Failed, Skipped }

public sealed record TestEvent(TestOutcome Outcome, string Name);

// Recognises "one test finished" in the output of the three runners Scripts/test.ps1 drives, so the
// grid can fill in live instead of waiting for an exit code (ADR-118 decision 8, Marty's ask).
//
// The contract this leans on is not a contract: these formats belong to dotnet test, vitest and
// Playwright, and they change between versions. So an unrecognised line returns nothing and is
// simply not counted — never an exception, and never a guess. The run's actual verdict comes from
// the process exit code, which is why a parser going blind costs a pretty picture and not a
// correct answer.
public static class TestEventParser
{
    // dotnet test / VSTest: "  Passed Namespace.Class.Method [3 ms]".
    private static readonly Regex VsTest = new(
        @"^\s*(?<outcome>Passed|Failed|Skipped)\s+(?<name>[^\s\[][^\[]*?)(\s*\[.*\])?\s*$",
        RegexOptions.Compiled);

    // vitest: " ✓ src/app/foo.spec.ts (3 tests) 15ms" — one line standing for several tests.
    private static readonly Regex Vitest = new(
        @"^\s*(?<mark>[✓✔✗✘×❯])\s+(?<name>\S+\.(spec|test)\.[tj]sx?)(?<detail>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex VitestCount = new(@"\((?<count>\d+)\s+tests?", RegexOptions.Compiled);

    // Playwright list reporter: "  ✓  12 [chromium] › e2e/01-auth.spec.ts:5:1 › signs in (1.2s)".
    private static readonly Regex Playwright = new(
        @"^\s*(?<mark>[✓✔✗✘×✘-]|−)\s+\d+\s+(?<name>\[.*)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<TestEvent> Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return Array.Empty<TestEvent>();

        var vsTest = VsTest.Match(line);
        if (vsTest.Success)
        {
            var outcome = vsTest.Groups["outcome"].Value switch
            {
                "Passed" => TestOutcome.Passed,
                "Failed" => TestOutcome.Failed,
                _ => TestOutcome.Skipped
            };
            return One(outcome, vsTest.Groups["name"].Value.Trim());
        }

        var playwright = Playwright.Match(line);
        if (playwright.Success)
            return One(Mark(playwright.Groups["mark"].Value), Shorten(playwright.Groups["name"].Value));

        var vitest = Vitest.Match(line);
        if (vitest.Success)
        {
            var outcome = Mark(vitest.Groups["mark"].Value);
            var name = vitest.Groups["name"].Value;
            var count = VitestCount.Match(vitest.Groups["detail"].Value);

            // A file line stands for every test inside it. Counting it once would show eleven ticks
            // for a suite that reports eleven tests across five files — visibly wrong to anyone who
            // knows the number.
            if (count.Success && int.TryParse(count.Groups["count"].Value, out var tests) && tests > 1)
                return Enumerable.Range(1, tests).Select(i => new TestEvent(outcome, $"{name} #{i}")).ToList();

            return One(outcome, name);
        }

        return Array.Empty<TestEvent>();
    }

    private static IReadOnlyList<TestEvent> One(TestOutcome outcome, string name) =>
        new[] { new TestEvent(outcome, name) };

    private static TestOutcome Mark(string mark) => mark switch
    {
        "✓" or "✔" => TestOutcome.Passed,
        "✗" or "✘" or "×" or "❯" => TestOutcome.Failed,
        _ => TestOutcome.Skipped
    };

    // Playwright names carry the browser, the file, the line and the title; the grid has room for
    // the part a person recognises.
    private static string Shorten(string name)
    {
        var parts = name.Split('›', StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length > 0 ? parts[^1] : name).Trim();
    }
}
