namespace CedarClerk.Localization;

public static class Languages
{
    public const string Primary = "ru";
    public const string English = "en";

    public static readonly IReadOnlyList<string> TranslationLanguages = [English];

    public static bool IsTranslationLanguage(string code) => TranslationLanguages.Contains(code);

    // Interface languages (B26, ADR-044) — a different axis from the content languages above:
    // which language the app's own chrome is shown in, not which language a post is written in.
    public static readonly IReadOnlyList<string> UiLanguages = [English, Primary];

    public static bool IsUiLanguage(string code) => UiLanguages.Contains(code);
}
