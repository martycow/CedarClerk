namespace CedarClerk.Localization;

public static partial class BlogTexts
{
    public sealed record IndexChrome(
        string Order, string SortNew, string SortOld, string SortPopular, string SortUnpopular,
        string LanguageGroup, string AllLanguages, string Available,
        string ShowMore, string NothingYet, string NoMatch, string ReadIn);

    public static readonly IReadOnlyDictionary<string, IndexChrome> IndexLabels =
        new Dictionary<string, IndexChrome>
        {
            ["en"] = new("Order", "Newest first", "Oldest first", "Most popular", "Least popular",
                "Language", "All languages", "available",
                "Show {0} more of {1}", "Nothing published yet.", "No posts match.", "Read in"),
            ["ru"] = new("Порядок", "Сначала новые", "Сначала старые", "Самые читаемые", "Наименее читаемые",
                "Язык", "Все языки", "есть перевод",
                "Показать ещё {0} из {1}", "Пока ничего не опубликовано.", "Ничего не найдено.", "Читать на"),
        };

}
