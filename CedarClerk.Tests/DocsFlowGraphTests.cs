using System.Text.RegularExpressions;

namespace CedarClerk.Tests;

// docs/DOCS-FLOW.md claims to map every living doc. That held on discipline alone until 18.08.2026,
// when an audit found four reference docs the scheme never knew about (STACK, BUSINESS,
// MULTITENANCY, integrations-setup). Same pattern as SchemaDriftGuardTests/UiInventoryDriftTests:
// trust a red test, not memory. Substring checks only — the test cannot judge whether a mention is
// still *accurate*, only that it is not missing entirely.
public class DocsFlowGraphTests
{
    private const string Fix = "add the node/row to docs/DOCS-FLOW.md in the same commit (§Размещение)";

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "DOCS-FLOW.md")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string Flow(DirectoryInfo root) =>
        File.ReadAllText(Path.Combine(root.FullName, "docs", "DOCS-FLOW.md"));

    [Fact]
    public void Every_live_doc_is_on_the_map()
    {
        var root = RepoRoot();
        var flow = Flow(root);

        var missing = Directory.GetFiles(Path.Combine(root.FullName, "docs"), "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root.FullName, p).Replace('\\', '/'))
            // adr/ and archive/ are append-only record sets, mapped as folders, not per file.
            .Where(rel => !rel.StartsWith("docs/adr/") && !rel.StartsWith("docs/archive/"))
            // The map does not map itself; INPUT_PROMPT.md is Marty's gitignored inbox — present on
            // his machine, absent on a fresh clone, so enumerating it would make the test
            // machine-dependent (its mention in the map is asserted by the path-exists test's input).
            .Where(rel => rel != "docs/DOCS-FLOW.md" && rel != "docs/INPUT_PROMPT.md")
            .Where(rel => !flow.Contains(rel))
            .OrderBy(rel => rel)
            .ToArray();

        Assert.True(missing.Length == 0, $"docs nothing in DOCS-FLOW mentions: {string.Join(", ", missing)} — {Fix}");
    }

    [Fact]
    public void Every_mapped_path_exists()
    {
        var root = RepoRoot();

        var broken = Regex.Matches(Flow(root), @"docs/[A-Za-z0-9_\-./]+?\.md")
            .Select(match => match.Value)
            .Distinct()
            // Gitignored by Marty's ruling — legitimately absent on a fresh clone.
            .Where(rel => rel != "docs/INPUT_PROMPT.md")
            .Where(rel => !File.Exists(Path.Combine(root.FullName, rel.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(rel => rel)
            .ToArray();

        Assert.True(broken.Length == 0, $"DOCS-FLOW names paths that do not exist on disk: {string.Join(", ", broken)}");
    }
}
