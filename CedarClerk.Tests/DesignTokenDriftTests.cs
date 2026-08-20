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

    // Every emitted token, not a hand-picked ten: three series inks were re-derived against graph
    // paper and a hand-listed guard could not see them, so the blog drew the pre-derivation colours
    // while the app drew the corrected ones — the exact drift this class exists to catch.
    [Fact]
    public void The_generated_tokens_still_match_the_stylesheet()
    {
        var css = StylesScss();
        var drifted = new List<string>();

        foreach (var (token, generated) in DesignTokens.Light)
        {
            // The first occurrence is the light `:root` — the dark theme's overrides come after it.
            var match = System.Text.RegularExpressions.Regex.Match(css, $@"--{token}:\s*([^;]+);");
            if (!match.Success) { drifted.Add($"--{token} is gone from styles.scss"); continue; }

            var declared = match.Groups[1].Value.Trim();
            if (declared != generated)
                drifted.Add($"--{token}: styles.scss says {declared}, DesignTokens says {generated}");
        }

        Assert.True(drifted.Count == 0,
            "run `npm run tokens:generate` — the blog and the landing page are painting stale values:"
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
