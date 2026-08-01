namespace CedarClerk.Core;

/// <summary>
/// Suggests the Russian forms of a glossary term (T-040).
///
/// **Suggestions, not silent generation.** Russian declension has more exceptions than rules, and
/// a wrongly generated form would mark the wrong word in someone's post with no way to notice. So
/// this proposes forms into the alias field, where the author reads them and deletes what is
/// wrong — the same bargain auto-translate makes, and for the same reason.
///
/// What it covers is the ordinary noun patterns a glossary is mostly made of: masculine hard stems
/// (рендерер), feminine -а/-я (сборка), neuter -о/-е (окно), and masculine -ь (уровень). Anything
/// else returns nothing rather than guessing — an empty suggestion is honest, a wrong one is not.
/// </summary>
public static class RussianDeclensions
{
    private const string Vowels = "аеёиоуыэюя";

    /// <summary>
    /// Singular oblique forms, without the nominative the author already typed. Empty when the
    /// word does not fit a pattern this is confident about.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string term)
    {
        term = term.Trim();
        // Multi-word terms decline on more than one word and agree across them — well past what a
        // suffix rule can do.
        if (term.Length < 3 || term.Contains(' ') || !IsRussian(term)) return [];

        var lower = term.ToLowerInvariant();
        var last = lower[^1];
        var stem = lower[..^1];

        return last switch
        {
            // сборка → сборки, сборке, сборку, сборкой
            'а' => Endings(stem, ["и", "е", "у", "ой"]),
            // статья → статьи, статье, статью, статьёй
            'я' => Endings(stem, ["и", "е", "ю", "ёй"]),
            // окно → окна, окну, окном
            'о' => Endings(stem, ["а", "у", "ом"]),
            // поле → поля, полю, полем
            'е' => Endings(stem, ["я", "ю", "ем"]),
            // уровень → уровня, уровню, уровнем — the fleeting vowel is what makes this worth a case
            'ь' => Fleeting(stem),
            // рендерер → рендерера, рендереру, рендерером, рендерере
            _ when !Vowels.Contains(last) && char.IsLetter(last) =>
                Endings(lower, ["а", "у", "ом", "е"]),
            _ => [],
        };
    }

    /// <summary>
    /// -ень/-ец words drop the vowel before the last consonant when they decline: уровень →
    /// уровня, not уровеня. Getting this wrong is the most visible way a suggestion looks generated.
    /// </summary>
    private static IReadOnlyList<string> Fleeting(string stem)
    {
        if (stem.Length >= 2 && (stem[^1] is 'н' or 'ц' or 'к') && stem[^2] is 'е' or 'о')
            stem = stem[..^2] + stem[^1];

        return Endings(stem, ["я", "ю", "ем", "е"]);
    }

    private static IReadOnlyList<string> Endings(string stem, string[] endings) =>
        endings.Select(e => stem + e).Distinct().ToList();

    private static bool IsRussian(string text) =>
        text.All(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c is 'ё' or 'Ё' or '-');
}
