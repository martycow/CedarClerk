using System.Text.Json;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

public enum RegistrationQuestionType
{
    Text,
    Choice,

    // Several options at once (N10). Its answer is stored as a JSON array inside the same
    // string-valued answers map the other types use — see MultiAnswer below for why.
    Multi,

    // A block of text (Label — an agreement/consent statement, not a question) with a single
    // checkbox the reader must tick to proceed — e.g. "I agree to the rules". Always Required
    // (Parse below forces it): an optional consent checkbox isn't a meaningful concept. Has no
    // Options; its answer is "yes" when ticked, same absent/blank-means-unanswered shape as every
    // other type, so the existing generic required-question check needs no special case for it.
    Consent,
}

// ADR-060 — an option carries a stable Id distinct from its display Label, so the same choice
// submitted from different language versions of the form aggregates as one answer. A v1 blob
// (plain string options) parses with Id == Label, which keeps every already-stored answer
// displaying identically: the old stored values were the labels.
public record RegistrationOption(string Id, string Label);

public record RegistrationQuestion(string Id, string Label, RegistrationQuestionType Type, IReadOnlyList<RegistrationOption> Options, bool Required);

// Parsed shape of Draft.RegistrationFormJson (B3) — the form an uninvited visitor of a private
// post fills in to get access. Parsed in Core so the blog renderer and the submit-validation
// endpoint read the exact same definition instead of each interpreting the raw JSON.
//
// The JSON is client-authored (the editor writes it) and never trusted: anything missing or
// malformed degrades to a safe default rather than throwing, so a hand-edited/corrupt blob can
// never take a published post down.
public record RegistrationFormDefinition(
    string? Intro,
    bool RequireName,
    bool RequireNickname,
    bool RequireEmail,
    bool RequireSocial,
    IReadOnlyList<RegistrationQuestion> Questions)
{
    // A form with no fields at all would be a submit button that collects nothing — treat the
    // built-in name+email pair as the floor so there's always something to identify a visitor by.
    public static RegistrationFormDefinition Default { get; } =
        new(null, RequireName: true, RequireNickname: false, RequireEmail: true, RequireSocial: false, []);

    public static RegistrationFormDefinition? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Default;
        }

        if (root is not JsonObject obj)
            return Default;

        var questions = new List<RegistrationQuestion>();
        if (obj["questions"] is JsonArray arr)
        {
            foreach (var q in arr)
            {
                if (q is not JsonObject qo)
                    continue;

                // AsString rather than a (string?) cast throughout: a v2 blob (ADR-060) carries
                // objects where v1 carries strings, and a cast on an object throws — this parser
                // must degrade, never throw, whatever shape lands in the column.
                var label = AsString(qo["label"]);
                if (string.IsNullOrWhiteSpace(label))
                    continue; // an unlabelled question can't be answered meaningfully

                var id = AsString(qo["id"]);
                if (string.IsNullOrWhiteSpace(id))
                    id = $"q{questions.Count + 1}";

                var type = AsString(qo["type"]) switch
                {
                    "choice" => RegistrationQuestionType.Choice,
                    "multi" => RegistrationQuestionType.Multi,
                    "consent" => RegistrationQuestionType.Consent,
                    _ => RegistrationQuestionType.Text,
                };

                var options = (qo["options"] as JsonArray)?
                    .Select(o => AsString(o))
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => new RegistrationOption(o!, o!))
                    .ToList() ?? [];

                // A choice/multi question with no options can't be rendered as one — fall back to
                // text rather than emitting an empty <select> or a checkbox group of nothing.
                if (type is RegistrationQuestionType.Choice or RegistrationQuestionType.Multi && options.Count == 0)
                    type = RegistrationQuestionType.Text;

                // An optional consent checkbox isn't a meaningful concept — force it regardless of
                // what a hand-edited/older blob says.
                var required = type == RegistrationQuestionType.Consent || ((bool?)qo["required"] ?? false);

                questions.Add(new RegistrationQuestion(id!, label!, type, options, required));
            }
        }

        return new RegistrationFormDefinition(
            Intro: AsString(obj["intro"]),
            RequireName: AsBool(obj["requireName"]),
            RequireNickname: AsBool(obj["requireNickname"]),
            RequireEmail: AsBool(obj["requireEmail"]),
            RequireSocial: AsBool(obj["requireSocial"]),
            Questions: questions);
    }

    internal static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static bool AsBool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

// A Multi question's answer travels inside the same Dictionary<string,string> as every other
// answer (PostRegistration.AnswersJson, ADR-042) — widening that map to a union type would
// invalidate every row already stored. Instead a multi answer IS a JSON array in the string,
// which is self-describing: no delimiter to collide with option text, and a plain text answer
// that happens to look like a list still round-trips as one value.
public static class MultiAnswer
{
    public static IReadOnlyList<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var trimmed = value.TrimStart();
        if (!trimmed.StartsWith('['))
            return [value];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) is { } list
                ? list.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                : [value];
        }
        catch (JsonException)
        {
            return [value];
        }
    }
}
