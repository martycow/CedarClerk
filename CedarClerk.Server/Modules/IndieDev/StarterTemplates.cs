using System.Text.Json.Nodes;
using CedarClerk.Core;
using CedarClerk.Localization;

namespace CedarClerk.Server.Modules.IndieDev;

// T-160 (ADR-133) — the body a project's starter document is born with. Section headings with
// empty paragraphs under them: the skeleton says what goes here, it never pretends to be content.
// Headings follow the document's language (ru/en, en otherwise) — the ADR-132 rule.
public static class StarterTemplates
{
    public static string For(string documentType, string projectType, string language)
    {
        var ru = language == Languages.Russian;
        return (documentType, projectType) switch
        {
            (DocumentTypes.Design, ProjectTypes.Jam) => Sections(DocumentTexts.JamSections(ru)),
            (DocumentTypes.Design, _) => Sections(DocumentTexts.DesignSections(ru)),
            // An Empty project promises nothing, so its first note carries no skeleton either.
            (DocumentTypes.Note, ProjectTypes.Empty) => DocJson.Doc([DocJson.Paragraph("")]),
            (DocumentTypes.Note, _) => Sections(DocumentTexts.PrototypeSections(ru)),
            (DocumentTypes.Changelog, _) => Sections(DocumentTexts.ChangelogSections(ru)),
            _ => DocJson.Doc([DocJson.Paragraph("")]),
        };
    }

    private static string Sections(string[] headings)
    {
        var content = new JsonArray();
        foreach (var heading in headings)
        {
            content.Add(DocJson.Heading(heading));
            content.Add(DocJson.Paragraph(""));
        }
        return DocJson.Doc(content);
    }
}
