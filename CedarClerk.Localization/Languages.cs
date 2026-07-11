namespace CedarClerk.Localization;

public static class Languages
{
    public const string Primary = "ru";
    public const string English = "en";

    public static readonly IReadOnlyList<string> TranslationLanguages = [English];

    public static bool IsTranslationLanguage(string code) => TranslationLanguages.Contains(code);
}
