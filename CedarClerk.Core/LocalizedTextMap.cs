using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// One short author-authored string that can differ per content language — the cross-link labels
// today (I15 + "перекрёстные ссылки тоже могут быть на разных языках"), and the same shape any
// future per-language label should reuse.
//
// Stored as the primary-language value in its own column plus a JSON object of the others, for
// the same reason RegistrationFormSet is split that way: the single-language author is the common
// case, their value stays exactly where it already is, and no existing row needs migrating.
//
// Nothing here throws on a malformed blob — a corrupt map degrades to "no translations", which
// falls back to the primary value and then to the caller's built-in default.
public static class LocalizedTextMap
{
    // Core stays free of a project reference to CedarClerk.Localization (same rule as
    // CedarToBlogHtmlRenderer.Render), so the primary language code is spelled out here.
    private const string PrimaryLanguage = "ru";

    /// <summary>
    /// The value for <paramref name="lang"/>: that language's own text, else the primary one,
    /// else null. Blank counts as absent — an author who cleared a field means "use the default",
    /// not "show an empty label".
    /// </summary>
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
