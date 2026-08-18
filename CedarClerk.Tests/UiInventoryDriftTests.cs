using System.Text.RegularExpressions;

namespace CedarClerk.Tests;

// X and Bluesky were first built inside the Export modal and rebuilt into Settings → Integrations by
// ADR-095, because nobody checked where the Telegram connection already lived. `docs/design/UI-INVENTORY.md`
// records where every element belongs; this fails when the front end grows something it does not.
public class UiInventoryDriftTests
{
    private const string Fix = "add a row to docs/design/UI-INVENTORY.md in the same commit (see .claude/rules/ui-changes.md)";

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "design", "UI-INVENTORY.md")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string Inventory(DirectoryInfo root) =>
        File.ReadAllText(Path.Combine(root.FullName, "docs", "design", "UI-INVENTORY.md"));

    private static DirectoryInfo AppDir(DirectoryInfo root) =>
        new(Path.Combine(root.FullName, "cedarclerk-web", "src", "app"));

    [Fact]
    public void Every_page_component_is_in_the_inventory()
    {
        var root = RepoRoot();
        var inventory = Inventory(root);

        var missing = new DirectoryInfo(Path.Combine(AppDir(root).FullName, "pages"))
            .GetFiles("*.component.ts")
            .Select(file => file.Name[..^3])
            .Where(name => !inventory.Contains(name))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(missing.Length == 0, $"pages nothing in the inventory mentions: {string.Join(", ", missing)} — {Fix}");
    }

    [Fact]
    public void Every_settings_section_is_in_the_inventory()
    {
        // Sections are what a new control lands in, and the id outlives the heading's wording.
        var root = RepoRoot();
        var inventory = Inventory(root);

        var missing = AppDir(root)
            .GetFiles("*.html", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file.FullName), @"id=""(sec-[a-z0-9-]+)"""))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Where(id => !inventory.Contains(id))
            .OrderBy(id => id)
            .ToArray();

        Assert.True(missing.Length == 0, $"settings sections nothing in the inventory mentions: {string.Join(", ", missing)} — {Fix}");
    }
}
