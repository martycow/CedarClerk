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
}

public record RegistrationQuestion(string Id, string Label, RegistrationQuestionType Type, IReadOnlyList<string> Options, bool Required);

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

                var label = (string?)qo["label"];
                if (string.IsNullOrWhiteSpace(label))
                    continue; // an unlabelled question can't be answered meaningfully

                var id = (string?)qo["id"];
                if (string.IsNullOrWhiteSpace(id))
                    id = $"q{questions.Count + 1}";

                var type = (string?)qo["type"] switch
                {
                    "choice" => RegistrationQuestionType.Choice,
                    "multi" => RegistrationQuestionType.Multi,
                    _ => RegistrationQuestionType.Text,
                };

                var options = (qo["options"] as JsonArray)?
                    .Select(o => (string?)o)
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => o!)
                    .ToList() ?? [];

                // A choice/multi question with no options can't be rendered as one — fall back to
                // text rather than emitting an empty <select> or a checkbox group of nothing.
                if (type is RegistrationQuestionType.Choice or RegistrationQuestionType.Multi && options.Count == 0)
                    type = RegistrationQuestionType.Text;

                questions.Add(new RegistrationQuestion(id!, label!, type, options, (bool?)qo["required"] ?? false));
            }
        }

        return new RegistrationFormDefinition(
            Intro: (string?)obj["intro"],
            RequireName: (bool?)obj["requireName"] ?? false,
            RequireNickname: (bool?)obj["requireNickname"] ?? false,
            RequireEmail: (bool?)obj["requireEmail"] ?? false,
            RequireSocial: (bool?)obj["requireSocial"] ?? false,
            Questions: questions);
    }
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
