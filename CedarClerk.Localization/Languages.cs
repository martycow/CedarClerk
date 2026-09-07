using System.Text.Json;

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

    private static readonly Catalog Data = Load();
    public static IReadOnlyList<string> ContentLanguages { get; } =
        Array.AsReadOnly(Data.Languages.Select(language => language.Code).ToArray());
    public static IReadOnlyList<string> UiLanguages => ContentLanguages;
    public static IReadOnlyList<string> InterfaceDictionaries { get; } =
        Array.AsReadOnly(Data.InterfaceDictionaries);

    public static bool IsContentLanguage(string code) => ContentLanguages.Contains(code);
    public static bool IsUiLanguage(string code) => UiLanguages.Contains(code);
    public static string EndonymOf(string code) =>
        Data.Languages.FirstOrDefault(language => language.Code == code)?.Endonym ?? code.ToUpperInvariant();

    private static Catalog Load()
    {
        using var stream = typeof(Languages).Assembly.GetManifestResourceStream("CedarClerk.Localization.languages.json")
            ?? throw new InvalidOperationException("The language catalog is missing.");
        return JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The language catalog is empty.");
    }

    private sealed record Language(string Code, string Endonym);
    private sealed record Catalog(Language[] Languages, string[] InterfaceDictionaries);
}
