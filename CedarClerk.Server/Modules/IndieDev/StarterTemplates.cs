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
            (DocumentTypes.Design, ProjectTypes.Jam) => Sections(ru
                ? ["Идея", "Скоуп: что выйдет", "Расписание", "Чеклист сдачи"]
                : ["The idea", "Scope: what ships", "Schedule", "Submission checklist"]),
            (DocumentTypes.Design, _) => Sections(ru
                ? ["Концепт", "Core loop", "Системы", "Контент", "Открытые вопросы"]
                : ["Concept", "Core loop", "Systems", "Content", "Open questions"]),
            (DocumentTypes.Note, _) => Sections(ru
                ? ["Вопрос, на который отвечает прототип", "Как поймём, что ответ «да»"]
                : ["The question this prototype answers", "How we'll know the answer is yes"]),
            (DocumentTypes.Changelog, _) => Sections(ru
                ? ["Не выпущено", "Известные проблемы"]
                : ["Unreleased", "Known issues"]),
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
