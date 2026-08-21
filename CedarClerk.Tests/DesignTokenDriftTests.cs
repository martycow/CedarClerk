using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-101 / ADR-071 principle 6. The blog kept its own copy of the palette, so the contrast pass
// (ADR-074) fixed the app and left the blog a shade behind — nobody noticed because nothing could.
// The generated DesignTokens is the single source now, and this is what notices.
public class DesignTokenDriftTests
{
    private static string StylesScss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "cedarclerk-web", "src", "styles.scss")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "cedarclerk-web", "src", "styles.scss"));
    }

    // The declarations of one rule, read the way generate-design-tokens.mjs reads them: brace
    // matching from the selector, comments out first — nearly every token in that file is
    // documented above its value, and a `/* ... */` between a name and its value would otherwise
    // be read as part of the value.
    private static Dictionary<string, string> Declared(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"selector not found in styles.scss: {selector}");
        var open = css.IndexOf('{', start);
        int depth = 0, i = open;
        for (; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) break;
        }

        var body = System.Text.RegularExpressions.Regex.Replace(
            css[(open + 1)..i], @"/\*[\s\S]*?\*/", string.Empty);
        var vars = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(body, @"(--[\w-]+)\s*:\s*([^;]+);"))
            vars[m.Groups[1].Value[2..]] = m.Groups[2].Value.Trim();
        return vars;
    }

    // Every emitted token, in both themes: three series inks were re-derived against graph paper
    // and a hand-listed guard could not see them, so the blog drew the pre-derivation colours while
    // the app drew the corrected ones. Dark is emitted and painted exactly as Light is, so a guard
    // over one theme leaves the other free to drift the same way.
    [Theory]
    [InlineData("light", ":root")]
    [InlineData("dark", ":root[data-theme=\"dark\"]")]
    public void The_generated_tokens_still_match_the_stylesheet(string theme, string selector)
    {
        // The light `:root` is the first occurrence, so IndexOf finds it before the dark override.
        var declared = Declared(StylesScss(), selector);
        var generated = theme == "dark" ? DesignTokens.Dark : DesignTokens.Light;
        var drifted = new List<string>();

        // Without this an empty dictionary would walk no tokens and pass, which is the shape of
        // guard this test was widened to stop being.
        Assert.NotEmpty(generated);

        foreach (var (token, value) in generated)
        {
            if (!declared.TryGetValue(token, out var css))
            {
                drifted.Add($"--{token} is gone from {selector} in styles.scss");
                continue;
            }

            if (css != value)
                drifted.Add($"--{token}: styles.scss says {css}, DesignTokens says {value}");
        }

        Assert.True(drifted.Count == 0,
            $"run `npm run tokens:generate` — the blog and the landing page are painting stale {theme} values:"
            + Environment.NewLine + string.Join(Environment.NewLine, drifted));
    }

    [Fact]
    public void The_dark_theme_only_carries_what_it_overrides()
    {
        // Anything the dark theme does not list is inherited, and listing it here would be a
        // second place to change a value that has one.
        Assert.DoesNotContain("radius-md", DesignTokens.Dark.Keys);
        Assert.Contains("text", DesignTokens.Dark.Keys);
    }

    [Fact]
    public void Declarations_render_as_css()
    {
        var css = DesignTokens.Declarations(DesignTokens.Light);

        Assert.Contains("--accent: " + DesignTokens.Light["accent"] + ";", css);
        Assert.DoesNotContain("{", css);
    }
}
