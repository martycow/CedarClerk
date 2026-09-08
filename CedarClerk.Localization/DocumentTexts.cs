namespace CedarClerk.Localization;

public static class DocumentTexts
{
    public static string[] DesignSections(bool russian) => russian
        ? ["Концепт", "Core loop", "Системы", "Контент", "Открытые вопросы"]
        : ["Concept", "Core loop", "Systems", "Content", "Open questions"];

    public static string[] ChangelogSections(bool russian) => russian
        ? ["Не выпущено", "Известные проблемы"]
        : ["Unreleased", "Known issues"];

    public static string SprintCompleted(bool russian) => russian ? "Что сделано" : "What got done";
    public static string SprintReleased(bool russian) => russian ? "Релизы" : "Released";
    public static string SprintNext(bool russian) => russian ? "Что дальше" : "What's next";
}
