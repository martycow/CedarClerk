namespace CedarClerk.Localization;

public static partial class BlogTexts
{
    public sealed record ReadingChrome(
        string Menu, string Theme, string Day, string Night, string System, string Size, string Face);

    // Day/Night matches the lamp metaphor used by the application.
    // Ukrainian, Belarusian and Georgian wording still needs native-speaker review.
    public static readonly IReadOnlyDictionary<string, ReadingChrome> ReadingLabels =
        new Dictionary<string, ReadingChrome>
        {
            ["ru"] = new("Чтение", "Тема", "День", "Ночь", "Система", "Размер текста", "Гарнитура"),
            ["en"] = new("Reading", "Theme", "Day", "Night", "System", "Text size", "Face"),
            ["de"] = new("Lesen", "Design", "Tag", "Nacht", "System", "Textgröße", "Schriftart"),
            ["fr"] = new("Lecture", "Thème", "Jour", "Nuit", "Système", "Taille du texte", "Police"),
            ["es"] = new("Lectura", "Tema", "Día", "Noche", "Sistema", "Tamaño del texto", "Tipografía"),
            ["ja"] = new("表示", "テーマ", "昼", "夜", "システム", "文字サイズ", "書体"),
            ["uk"] = new("Читання", "Тема", "День", "Ніч", "Системна", "Розмір тексту", "Гарнітура"),
            ["be"] = new("Чытанне", "Тэма", "Дзень", "Ноч", "Сістэмная", "Памер тэксту", "Гарнітура"),
            ["ka"] = new("კითხვა", "თემა", "დღე", "ღამე", "სისტემური", "ტექსტის ზომა", "შრიფტი"),
        };

}
