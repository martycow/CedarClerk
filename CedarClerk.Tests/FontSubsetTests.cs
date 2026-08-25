using System.Text.RegularExpressions;

namespace CedarClerk.Tests;

// T-275. @fontsource ships two shapes of stylesheet per weight: the per-weight one, whose rules
// each carry a unicode-range, and the per-subset ones (latin-600.css, cyrillic-600.css), which
// carry none. Importing two of the latter for one weight declares two @font-face rules with the
// same family, weight and style covering all of U+0-10FFFF, so the later one wins outright and the
// glyphs it does not have fall through to the next family in the stack — silently, and on every
// screen. The app drew Latin text in Georgia and system-ui for as long as that was true.
public class FontSubsetTests
{
    private static readonly Regex FontsourceUse =
        new(@"@use\s+'@fontsource/(?<package>[^/']+)/(?<sheet>[^']+)\.css'", RegexOptions.Compiled);

    // Everything @fontsource names a subset. A sheet whose stem is one of these, with or without a
    // weight, is a sheet with no range in it.
    private static readonly string[] Subsets =
    [
        "latin", "latin-ext", "cyrillic", "cyrillic-ext", "greek", "greek-ext",
        "vietnamese", "hebrew", "arabic", "devanagari", "thai", "korean", "japanese", "chinese",
    ];

    private static string StylesScss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "cedarclerk-web", "src", "styles.scss")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "cedarclerk-web", "src", "styles.scss"));
    }

    [Fact]
    public void No_face_is_imported_through_a_per_subset_stylesheet()
    {
        var offenders = FontSubsetTests.FontsourceUse.Matches(StylesScss())
            .Select(m => new { Package = m.Groups["package"].Value, Sheet = m.Groups["sheet"].Value })
            .Where(u => Subsets.Any(s =>
                u.Sheet.Equals(s, StringComparison.Ordinal) ||
                u.Sheet.StartsWith(s + "-", StringComparison.Ordinal)))
            .Select(u => $"@fontsource/{u.Package}/{u.Sheet}.css")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These carry no unicode-range, so two of them for one weight shadow each other and the "
            + "text falls to the next family in the stack. Import the per-weight sheet instead "
            + $"(e.g. '@fontsource/vollkorn/600.css'): {string.Join(", ", offenders)}");
    }

    // The other half of the same fact: what is imported must be a weight, so that the sheet behind
    // it is the one carrying ranges.
    [Fact]
    public void Every_fontsource_import_names_a_weight()
    {
        var wrong = FontsourceUse.Matches(StylesScss())
            .Select(m => m.Groups["sheet"].Value)
            .Where(sheet => !Regex.IsMatch(sheet, @"^\d{3}(-italic)?$"))
            .ToList();

        Assert.True(wrong.Count == 0,
            $"Expected sheets named for a weight, like '600.css' or '400-italic.css': {string.Join(", ", wrong)}");
    }
}
