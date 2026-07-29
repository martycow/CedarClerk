using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// FI4.1 — a private post can carry its registration form in more than one language: the primary
// one in Draft.RegistrationFormJson, the rest in Draft.RegistrationFormTranslationsJson as a
// JSON object keyed by language code.
//
// ADR-060 superseded that per-column model with a single multi-language "v2" blob (one skeleton
// of question/option ids, per-language text overlays) stored in the primary column alone; the
// translations column and its per-slot writes remain only as the v1 compatibility path. Pick and
// LanguagesWithForm below understand both shapes, so call sites never care which one a row holds.
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
        if (IsMultiLanguage(primaryJson))
            return ResolveV2(primaryJson!, lang);

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
        if (IsMultiLanguage(primaryJson))
            return V2Languages(primaryJson!);

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

    /// <summary>True when the blob is an ADR-060 v2 multi-language form ("v": 2).</summary>
    public static bool IsMultiLanguage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            return JsonNode.Parse(json) is JsonObject obj
                && obj["v"] is JsonValue v && v.TryGetValue<int>(out var version) && version == 2;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Projects one language out of a v2 blob into the single-language definition the renderer
    /// and submit validation consume. Per-string fallback: a missing translation falls back to
    /// the skeleton's first language rather than dropping the question. Rendered option values
    /// are the stable option ids (ADR-060), so submitted answers are language-neutral.
    /// </summary>
    public static RegistrationFormDefinition? ResolveV2(string json, string lang)
    {
        JsonObject obj;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed)
                return RegistrationFormDefinition.Default;
            obj = parsed;
        }
        catch (JsonException)
        {
            return RegistrationFormDefinition.Default;
        }

        var languages = ReadLanguages(obj);
        var questions = new List<RegistrationQuestion>();

        if (obj["questions"] is JsonArray arr)
        {
            foreach (var q in arr)
            {
                if (q is not JsonObject qo)
                    continue;

                var label = PickText(qo["label"], lang, languages);
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                var id = RegistrationFormDefinition.AsString(qo["id"]);
                if (string.IsNullOrWhiteSpace(id))
                    id = $"q{questions.Count + 1}";

                var type = RegistrationFormDefinition.AsString(qo["type"]) switch
                {
                    "choice" => RegistrationQuestionType.Choice,
                    "multi" => RegistrationQuestionType.Multi,
                    "consent" => RegistrationQuestionType.Consent,
                    _ => RegistrationQuestionType.Text,
                };

                var options = new List<RegistrationOption>();
                if (qo["options"] is JsonArray optArr)
                {
                    foreach (var o in optArr)
                    {
                        if (o is not JsonObject oo) continue;
                        var optLabel = PickText(oo["label"], lang, languages);
                        if (string.IsNullOrWhiteSpace(optLabel)) continue;
                        var optId = RegistrationFormDefinition.AsString(oo["id"]);
                        options.Add(new RegistrationOption(string.IsNullOrWhiteSpace(optId) ? optLabel! : optId!, optLabel!));
                    }
                }

                if (type is RegistrationQuestionType.Choice or RegistrationQuestionType.Multi && options.Count == 0)
                    type = RegistrationQuestionType.Text;

                var required = type == RegistrationQuestionType.Consent
                    || RegistrationFormDefinition.AsBool(qo["required"]);

                questions.Add(new RegistrationQuestion(id!, label!, type, options, required));
            }
        }

        return new RegistrationFormDefinition(
            Intro: PickText(obj["intro"], lang, languages),
            RequireName: RegistrationFormDefinition.AsBool(obj["requireName"]),
            RequireNickname: RegistrationFormDefinition.AsBool(obj["requireNickname"]),
            RequireEmail: RegistrationFormDefinition.AsBool(obj["requireEmail"]),
            RequireSocial: RegistrationFormDefinition.AsBool(obj["requireSocial"]),
            Questions: questions);
    }

    private static IReadOnlyList<string> V2Languages(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject obj)
                return ReadLanguages(obj);
        }
        catch (JsonException) { }
        return [];
    }

    private static List<string> ReadLanguages(JsonObject obj) =>
        (obj["languages"] as JsonArray)?
            .Select(RegistrationFormDefinition.AsString)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!)
            .Distinct()
            .ToList() ?? [];

    // A per-language dictionary {"ru": "…", "en": "…"} — the requested language first, then the
    // skeleton's languages in declared order, so a partially translated form degrades string by
    // string rather than wholesale. A plain string (v1 remnant inside a v2 blob) is accepted too.
    private static string? PickText(JsonNode? node, string lang, IReadOnlyList<string> languages)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var plain):
                return string.IsNullOrWhiteSpace(plain) ? null : plain;
            case JsonObject map:
            {
                var byLang = RegistrationFormDefinition.AsString(map[lang]);
                if (!string.IsNullOrWhiteSpace(byLang))
                    return byLang;
                foreach (var l in languages)
                {
                    var fallback = RegistrationFormDefinition.AsString(map[l]);
                    if (!string.IsNullOrWhiteSpace(fallback))
                        return fallback;
                }
                return null;
            }
            default:
                return null;
        }
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
