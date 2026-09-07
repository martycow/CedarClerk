namespace CedarClerk.Localization;

public static class DocumentTexts
{
    public static string[] JamSections(bool russian) => russian
        ? ["Идея", "Скоуп: что выйдет", "Расписание", "Чеклист сдачи"]
        : ["The idea", "Scope: what ships", "Schedule", "Submission checklist"];

    public static string[] DesignSections(bool russian) => russian
        ? ["Концепт", "Core loop", "Системы", "Контент", "Открытые вопросы"]
        : ["Concept", "Core loop", "Systems", "Content", "Open questions"];

    public static string[] PrototypeSections(bool russian) => russian
        ? ["Вопрос, на который отвечает прототип", "Как поймём, что ответ «да»"]
        : ["The question this prototype answers", "How we'll know the answer is yes"];

    public static string[] ChangelogSections(bool russian) => russian
        ? ["Не выпущено", "Известные проблемы"]
        : ["Unreleased", "Known issues"];

    public static string SprintCompleted(bool russian) => russian ? "Что сделано" : "What got done";
    public static string SprintReleased(bool russian) => russian ? "Релизы" : "Released";
    public static string SprintNext(bool russian) => russian ? "Что дальше" : "What's next";
}
