namespace CedarClerk.Localization;

public static class Languages
{
    public const string Russian = "ru";
    public const string English = "en";
    public const string German = "de";
    public const string French = "fr";
    public const string Spanish = "es";
    public const string Japanese = "ja";
    public const string Ukrainian = "uk";
    public const string Belarusian = "be";
    public const string Georgian = "ka";

    /// <summary>
    /// Every language a post can be written in. There is deliberately only one such list
    /// (ADR-065): "is this a translation?" is a per-draft question — <c>lang != draft.PrimaryLanguage</c>
    /// — and a static list that tried to answer it is what produced both a duplicated "ru" entry
    /// and translation rows shadowing a draft's own primary language.
    /// </summary>
    public static readonly IReadOnlyList<string> ContentLanguages =
        [Russian, English, German, French, Spanish, Japanese, Ukrainian, Belarusian, Georgian];

    public static bool IsContentLanguage(string code) => ContentLanguages.Contains(code);

    // Endonyms: a language name is only useful to someone who reads that language, so these are
    // never translated. Used for tab labels and the "add a translation" list.
    private static readonly IReadOnlyDictionary<string, string> Endonyms = new Dictionary<string, string>
    {
        [Russian] = "Русский",
        [English] = "English",
        [German] = "Deutsch",
        [French] = "Français",
        [Spanish] = "Español",
        [Japanese] = "日本語",
        [Ukrainian] = "Українська",
        [Belarusian] = "Беларуская",
        [Georgian] = "ქართული",
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
