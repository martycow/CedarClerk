using System.Text.RegularExpressions;

namespace CedarClerk.Tests;

// ADR-238 clause 8 states a rule, not a list: any endpoint that changes a draft's stored text calls
// GlossaryUsage.SyncForDraftAsync before it returns. The clause first shipped as a census and the
// census was already two sites short on the day it was written, which is the reason this exists —
// the same argument SchemaDriftGuardTests makes about migrations.
//
// It is file-level, like UiInventoryDriftTests: it catches a *new* file that stores documents and
// never calls the sync. It cannot prove that every write inside an already-compliant file is
// covered, and it does not claim to.
public class GlossaryUsageInvariantTests
{
    // Files that assign CedarJson without owning a document's stored text. Each needs a reason,
    // because "it does not call the sync" is what the defect looks like too.
    private static readonly Dictionary<string, string> NotDocumentWrites = new()
    {
        ["DraftRevisionService.cs"] = "Writes a DraftRevision — history of a document, not the document.",
        ["PostEndpoints.cs"] = "Fills a PublishRequest from a document it only reads; nothing is stored.",
    };

    [Fact]
    public void Every_file_that_stores_a_documents_text_syncs_glossary_usage()
    {
        var serverDir = ServerDirectory();
        var assignment = new Regex(@"\bCedarJson\s*=[^=]");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(serverDir, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (NotDocumentWrites.ContainsKey(name)) continue;
            if (Path.GetFileName(Path.GetDirectoryName(file)) == "Migrations") continue;

            var text = File.ReadAllText(file);
            if (!assignment.IsMatch(text)) continue;
            if (text.Contains("GlossaryUsage.SyncForDraftAsync", StringComparison.Ordinal)) continue;

            offenders.Add(name);
        }

        Assert.True(offenders.Count == 0,
            "These store a document's text without rescanning its glossary usage, which leaves "
            + "\"used in N\" describing text that has moved (ADR-238 clause 8): "
            + string.Join(", ", offenders)
            + ". Call GlossaryUsage.SyncForDraftAsync after the save, or add the file to "
            + "GlossaryUsageInvariantTests.NotDocumentWrites with the reason it stores no document.");
    }

    [Fact]
    public void The_allow_list_names_only_files_that_exist_and_still_assign_CedarJson()
    {
        var serverDir = ServerDirectory();
        var assignment = new Regex(@"\bCedarJson\s*=[^=]");

        foreach (var (name, reason) in NotDocumentWrites)
        {
            var matches = Directory.EnumerateFiles(serverDir, name, SearchOption.AllDirectories).ToList();
            Assert.True(matches.Count > 0, $"{name} is allow-listed but no longer exists.");
            Assert.True(assignment.IsMatch(File.ReadAllText(matches[0])),
                $"{name} no longer assigns CedarJson — drop it from the allow list.");
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }

    private static string ServerDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "CedarClerk.Server")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "CedarClerk.Server");
    }
}
