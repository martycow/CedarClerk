namespace CedarClerk.Localization;

public static class Languages
{
    public const string Primary = "ru";
    public const string English = "en";
    public const string German = "de";
    public const string French = "fr";
    public const string Spanish = "es";
    public const string Japanese = "ja";

    // Content languages a post can be translated into (NF2). Primary is deliberately not here:
    // it's the original, stored on Draft itself rather than as a DraftTranslation row.
    public static readonly IReadOnlyList<string> TranslationLanguages =
        [English, German, French, Spanish, Japanese];

    public static bool IsTranslationLanguage(string code) => TranslationLanguages.Contains(code);

    /// <summary>
    /// Every language a post can exist in, primary first — the order the editor shows tabs in.
    /// </summary>
    public static readonly IReadOnlyList<string> ContentLanguages =
        [Primary, .. TranslationLanguages];

    // Endonyms: a language name is only useful to someone who reads that language, so these are
    // never translated. Used for tab labels and the "add a translation" list.
    private static readonly IReadOnlyDictionary<string, string> Endonyms = new Dictionary<string, string>
    {
        [Primary] = "Русский",
        [English] = "English",
        [German] = "Deutsch",
        [French] = "Français",
        [Spanish] = "Español",
        [Japanese] = "日本語",
    };

    public static string EndonymOf(string code) => Endonyms.GetValueOrDefault(code, code.ToUpperInvariant());

    // Interface languages (B26, ADR-044) — a different axis from the content languages above:
    // which language the app's chrome is shown in, not which language a post is written in.
    //
    // NF2 asked for the slots without the translations, so a locale with no dictionary falls back
    // to English rather than shipping ~650 untranslated keys per language. Adding a real
    // translation later is dropping in a file, not editing this list.
    public static readonly IReadOnlyList<string> UiLanguages = ContentLanguages;

    public static bool IsUiLanguage(string code) => UiLanguages.Contains(code);
}
