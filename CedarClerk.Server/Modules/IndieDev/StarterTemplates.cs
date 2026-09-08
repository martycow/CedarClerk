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
            (DocumentTypes.Design, _) => Sections(DocumentTexts.DesignSections(ru)),
            (DocumentTypes.Changelog, _) => Sections(DocumentTexts.ChangelogSections(ru)),
            // A note promises nothing, so it carries no skeleton either.
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
