using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Localization;

// One short author-authored string that differs per content language — the cross-link labels today
// (I15), and the shape any future per-language label should reuse. Primary value in its own column
// plus a JSON object of the others, like RegistrationFormSet: the single-language author is the
// common case and no existing row needs migrating. A malformed blob degrades to "no translations".
public static class LocalizedTextMap
{
    private const string PrimaryLanguage = Languages.Russian;

    // Blank counts as absent: an author who cleared a field means "use the default", not "show an
    // empty label".
    public static string? Pick(string? primaryValue, string? translationsJson, string lang)
    {
        if (!string.IsNullOrWhiteSpace(lang) && lang != PrimaryLanguage
            && Read(translationsJson).TryGetValue(lang, out var translated)
            && !string.IsNullOrWhiteSpace(translated))
        {
            return translated.Trim();
        }

        return string.IsNullOrWhiteSpace(primaryValue) ? null : primaryValue.Trim();
    }

    /// <summary>
    /// Writes one language's value, returning the new JSON — or null when nothing is left, so an
    /// emptied map is stored as NULL rather than as "{}".
    /// </summary>
    public static string? Set(string? translationsJson, string lang, string? value)
    {
        var map = new Dictionary<string, string>(Read(translationsJson));
        if (string.IsNullOrWhiteSpace(value)) map.Remove(lang);
        else map[lang] = value.Trim();

        if (map.Count == 0) return null;

        var obj = new JsonObject();
        foreach (var (key, v) in map) obj[key] = v;
        return obj.ToJsonString();
    }

    /// <summary>Every language that has its own value, for showing which are set.</summary>
    public static IReadOnlyDictionary<string, string> All(string? translationsJson) => Read(translationsJson);

    private static IReadOnlyDictionary<string, string> Read(string? translationsJson)
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
                if (value is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                    map[key] = text;
            }
            return map;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
