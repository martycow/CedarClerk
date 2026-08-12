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

    // A statement plus one checkbox to proceed. Always Required (Parse forces it) — an optional
    // consent checkbox is not a meaningful concept. Answer is "yes" when ticked, so the generic
    // required-question check needs no special case.
    Consent,

    // T-032 — Text in a box that admits it will be more than a line. Its own type rather than a flag
    // so the editor can offer it as a choice; the stored answer is an ordinary string.
    LongText,

    // T-031 — a block the reader does not fill in. Never Required, never collected (Parse forces
    // both), so it is invisible to validation and to the answers map.
    Static,
}

// ADR-060 — an option carries a stable Id distinct from its display Label, so the same choice
// submitted from different language versions of the form aggregates as one answer. A v1 blob
// (plain string options) parses with Id == Label, which keeps every already-stored answer
// displaying identically: the old stored values were the labels.
public record RegistrationOption(string Id, string Label);

// ImageUrl only means anything for a Static block (T-031) — a /media/... path uploaded through
// the ordinary asset endpoint. Optional so every existing blob keeps parsing unchanged.
public record RegistrationQuestion(string Id, string Label, RegistrationQuestionType Type,
    IReadOnlyList<RegistrationOption> Options, bool Required, string? ImageUrl = null);

// Draft.RegistrationFormJson (B3), parsed in Core so the blog renderer and the submit-validation
// endpoint read the same definition instead of each interpreting raw JSON. The blob is
// client-authored and never trusted: anything malformed degrades to a safe default rather than
// throwing, so a corrupt one cannot take a published post down.
public record RegistrationFormDefinition(
    string? Intro,
    bool RequireName,
    bool RequireNickname,
    bool RequireEmail,
    bool RequireSocial,
    IReadOnlyList<RegistrationQuestion> Questions,
    // T-033 — null means no mail: a generated "thanks" would be words in the owner's voice that they
    // never chose.
    string? ResponseEmailSubject = null,
    string? ResponseEmailBody = null)
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
                var type = AsString(qo["type"]) switch
                {
                    "choice" => RegistrationQuestionType.Choice,
                    "multi" => RegistrationQuestionType.Multi,
                    "consent" => RegistrationQuestionType.Consent,
                    "longtext" => RegistrationQuestionType.LongText,
                    "static" => RegistrationQuestionType.Static,
                    _ => RegistrationQuestionType.Text,
                };

                var imageUrl = type == RegistrationQuestionType.Static ? AsString(qo["imageUrl"]) : null;

                var label = AsString(qo["label"]);
                // A static block carrying only an image is legitimate; every other type needs a
                // label, since an unlabelled question can't be answered meaningfully.
                if (string.IsNullOrWhiteSpace(label) &&
                    !(type == RegistrationQuestionType.Static && !string.IsNullOrWhiteSpace(imageUrl)))
                    continue;

                var id = AsString(qo["id"]);
                if (string.IsNullOrWhiteSpace(id))
                    id = $"q{questions.Count + 1}";

                var options = (qo["options"] as JsonArray)?
                    .Select(o => AsString(o))
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => new RegistrationOption(o!, o!))
                    .ToList() ?? [];

                // A choice/multi question with no options can't be rendered as one — fall back to
                // text rather than emitting an empty <select> or a checkbox group of nothing.
                if (type is RegistrationQuestionType.Choice or RegistrationQuestionType.Multi && options.Count == 0)
                    type = RegistrationQuestionType.Text;

                // An optional consent checkbox isn't a meaningful concept, and a required static
                // block is a form nobody can submit — force both regardless of what a hand-edited
                // or older blob says.
                var required = type switch
                {
                    RegistrationQuestionType.Consent => true,
                    RegistrationQuestionType.Static => false,
                    _ => (bool?)qo["required"] ?? false,
                };

                questions.Add(new RegistrationQuestion(id!, label ?? "", type, options, required, imageUrl));
            }
        }

        return new RegistrationFormDefinition(
            Intro: AsString(obj["intro"]),
            RequireName: AsBool(obj["requireName"]),
            RequireNickname: AsBool(obj["requireNickname"]),
            RequireEmail: AsBool(obj["requireEmail"]),
            RequireSocial: AsBool(obj["requireSocial"]),
            Questions: questions,
            ResponseEmailSubject: AsString(obj["responseEmailSubject"]),
            ResponseEmailBody: AsString(obj["responseEmailBody"]));
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
