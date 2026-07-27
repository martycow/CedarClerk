using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// FI4.1 — a private post can carry its registration form in more than one language: the primary
// one in Draft.RegistrationFormJson, the rest in Draft.RegistrationFormTranslationsJson as a
// JSON object keyed by language code.
//
// Deliberately not one map holding every language: the single-language post is the common case
// and keeps working untouched, and the primary form stays exactly where every existing row,
// endpoint and test already expects it.
//
// Like RegistrationFormDefinition, nothing here throws on a malformed blob — a corrupt
// translations object degrades to "no translations" rather than taking a published post down.
public static class RegistrationFormSet
{
    // Core stays free of a project reference to CedarClerk.Localization (same rule as
    // CedarToBlogHtmlRenderer.Render), so the primary language code is spelled out here.
    private const string PrimaryLanguage = "ru";

    /// <summary>
    /// The form to show a reader who asked for <paramref name="lang"/>, falling back to the
    /// primary-language form when that language has none. Null when the post has no form at all.
    /// </summary>
    public static RegistrationFormDefinition? Pick(string? primaryJson, string? translationsJson, string lang)
    {
        if (!string.IsNullOrWhiteSpace(lang) && lang != PrimaryLanguage
            && ReadTranslations(translationsJson).TryGetValue(lang, out var json)
            && RegistrationFormDefinition.Parse(json) is { } translated)
        {
            return translated;
        }

        return RegistrationFormDefinition.Parse(primaryJson);
    }

    /// <summary>
    /// Language codes this post actually has a form for, primary first. Used by the editor to
    /// show which languages a reader would be greeted in.
    /// </summary>
    public static IReadOnlyList<string> LanguagesWithForm(string? primaryJson, string? translationsJson)
    {
        var languages = new List<string>();
        if (RegistrationFormDefinition.Parse(primaryJson) is not null)
            languages.Add(PrimaryLanguage);

        foreach (var (lang, json) in ReadTranslations(translationsJson))
        {
            if (lang != PrimaryLanguage && RegistrationFormDefinition.Parse(json) is not null)
                languages.Add(lang);
        }

        return languages;
    }

    /// <summary>
    /// Writes one language's form into the translations object, returning the new JSON — or null
    /// when nothing is left, so an emptied map is stored as NULL rather than as "{}".
    /// </summary>
    public static string? SetTranslation(string? translationsJson, string lang, string? formJson)
    {
        var map = new Dictionary<string, string>(ReadTranslations(translationsJson));
        if (string.IsNullOrWhiteSpace(formJson)) map.Remove(lang);
        else map[lang] = formJson;

        if (map.Count == 0) return null;

        var obj = new JsonObject();
        foreach (var (key, value) in map)
        {
            // Stored as a nested object rather than as an escaped string, so the column stays
            // readable and a second round-trip can't double-encode it.
            obj[key] = JsonNode.Parse(value);
        }
        return obj.ToJsonString();
    }

    private static IReadOnlyDictionary<string, string> ReadTranslations(string? translationsJson)
    {
        if (string.IsNullOrWhiteSpace(translationsJson))
            return new Dictionary<string, string>();

        try
        {
            if (JsonNode.Parse(translationsJson) is not JsonObject obj)
                return new Dictionary<string, string>();

            var map = new Dictionary<string, string>();
            foreach (var (key, value) in obj)
            {
                if (value is null) continue;
                // Accept both a nested object and a JSON string holding one — an older or
                // hand-written blob may carry either.
                map[key] = value is JsonValue v && v.TryGetValue<string>(out var raw) ? raw : value.ToJsonString();
            }
            return map;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
