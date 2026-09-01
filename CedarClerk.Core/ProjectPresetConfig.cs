using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// T-331 — the shape of a project preset's ConfigJson. A project preset is the New-project dialog's
// four built-ins made editable: it names the built-in type the project publishes as (so the type
// contract stays ProjectTypes, never a new stored string), which document the project is born with
// and what that document is called. Everything but the type is optional — an empty preset is still
// a valid one, it just falls back to what the type already implies.
public sealed record ProjectPresetConfig(
    string ProjectType, string? DocumentType, string? DocumentTitle, string Description)
{
    public const int DescriptionMaxChars = 2000;
    public const int TitleMaxChars = 80;

    public static readonly ProjectPresetConfig Default =
        new(ProjectTypes.FullGame, null, null, "");

    public static ProjectPresetConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            var node = JsonNode.Parse(json);
            var projectType = node?["projectType"]?.GetValue<string>();
            var documentType = node?["documentType"]?.GetValue<string>();
            var documentTitle = Trim(node?["documentTitle"]?.GetValue<string>(), TitleMaxChars);
            var description = Trim(node?["description"]?.GetValue<string>(), DescriptionMaxChars) ?? "";

            return new ProjectPresetConfig(
                ProjectTypes.IsKnown(projectType) ? projectType! : Default.ProjectType,
                DocumentTypes.IsKnown(documentType) ? documentType : null,
                documentTitle,
                description);
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    /// <summary>The document type this preset starts with — its own, or the one its type implies.</summary>
    public string EffectiveDocumentType => DocumentType ?? ProjectTypes.StarterDocumentType(ProjectType);

    public string ToJson() => new JsonObject
    {
        ["projectType"] = ProjectType,
        ["documentType"] = DocumentType,
        ["documentTitle"] = DocumentTitle,
        ["description"] = Description,
    }.ToJsonString();

    private static string? Trim(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
