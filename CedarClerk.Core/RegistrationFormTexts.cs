using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

// ADR-060 — form auto-translate support. Mirrors TipTapTextNodes' extract/replace contract, but
// over a v2 form blob's per-language text dictionaries: ExtractTexts pulls every translatable
// string of one source language in a fixed walk order (intro, then per question: label, then each
// option label — "" for a missing slot, so the order is identical whatever is filled in), the
// caller translates the flat list, and ReplaceTexts writes the results into the target language's
// slots in the same order, skipping blanks so a missing source string stays missing.
public static class RegistrationFormTexts
{
    /// <summary>
    /// Rewrites a v1 single-language blob as a v2 multi-language one holding that single
    /// language. A blob that already is v2 comes back unchanged; an unparseable one comes back
    /// as a v2 wrapping of the defensive default, same degradation rule as everywhere else.
    /// </summary>
    public static string UpgradeToV2(string? json, string lang)
    {
        if (RegistrationFormSet.IsMultiLanguage(json))
            return json!;

        var form = RegistrationFormDefinition.Parse(json) ?? RegistrationFormDefinition.Default;

        var questions = new JsonArray();
        foreach (var q in form.Questions)
        {
            var options = new JsonArray();
            foreach (var o in q.Options)
                options.Add(new JsonObject { ["id"] = o.Id, ["label"] = new JsonObject { [lang] = o.Label } });

            questions.Add(new JsonObject
            {
                ["id"] = q.Id,
                ["type"] = q.Type switch
                {
                    RegistrationQuestionType.Choice => "choice",
                    RegistrationQuestionType.Multi => "multi",
                    RegistrationQuestionType.Consent => "consent",
                    _ => "text",
                },
                ["required"] = q.Required,
                ["label"] = new JsonObject { [lang] = q.Label },
                ["options"] = options,
            });
        }

        var root = new JsonObject
        {
            ["v"] = 2,
            ["languages"] = new JsonArray(lang),
            ["requireName"] = form.RequireName,
            ["requireNickname"] = form.RequireNickname,
            ["requireEmail"] = form.RequireEmail,
            ["requireSocial"] = form.RequireSocial,
            ["questions"] = questions,
        };
        if (!string.IsNullOrWhiteSpace(form.Intro))
            root["intro"] = new JsonObject { [lang] = form.Intro };

        return root.ToJsonString(SerializerOptions);
    }

    public static List<string> ExtractTexts(string v2Json, string sourceLang)
    {
        var texts = new List<string>();
        Walk(ParseObject(v2Json), (get, _) => texts.Add(get(sourceLang) ?? ""));
        return texts;
    }

    public static string ReplaceTexts(string v2Json, string targetLang, IReadOnlyList<string> translated)
    {
        var root = ParseObject(v2Json);
        var i = 0;

        Walk(root, (_, set) =>
        {
            if (i >= translated.Count)
                throw new ArgumentException("Translated text count does not match form text slots");
            var value = translated[i++];
            if (!string.IsNullOrWhiteSpace(value))
                set(targetLang, value);
        });

        if (i != translated.Count)
            throw new ArgumentException("Translated text count does not match form text slots");

        if (root["languages"] is not JsonArray langs)
            root["languages"] = langs = [];
        if (!langs.Select(RegistrationFormDefinition.AsString).Contains(targetLang))
            langs.Add(targetLang);

        return root.ToJsonString(SerializerOptions);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("Not a v2 form blob");

    // Fires (get(lang), set(lang, value)) for every translatable text slot: intro, then per
    // question its label followed by each option's label. The order must be identical between
    // ExtractTexts and ReplaceTexts — both walk the same parsed shape, and every slot fires
    // whether or not it currently holds text (get returns null for a missing language).
    private static void Walk(JsonObject root, Action<Func<string, string?>, Action<string, string>> onSlot)
    {
        onSlot(l => ReadLangText(root, "intro", l), (l, v) => WriteLangText(root, "intro", l, v));

        if (root["questions"] is not JsonArray questions)
            return;

        foreach (var q in questions)
        {
            if (q is not JsonObject qo)
                continue;

            onSlot(l => ReadLangText(qo, "label", l), (l, v) => WriteLangText(qo, "label", l, v));

            if (qo["options"] is not JsonArray options)
                continue;

            foreach (var o in options)
            {
                if (o is not JsonObject oo)
                    continue;
                onSlot(l => ReadLangText(oo, "label", l), (l, v) => WriteLangText(oo, "label", l, v));
            }
        }
    }

    private static string? ReadLangText(JsonObject owner, string key, string lang) =>
        owner[key] is JsonObject map ? RegistrationFormDefinition.AsString(map[lang]) : null;

    private static void WriteLangText(JsonObject owner, string key, string lang, string value)
    {
        if (owner[key] is not JsonObject map)
            owner[key] = map = [];
        map[lang] = value;
    }
}
