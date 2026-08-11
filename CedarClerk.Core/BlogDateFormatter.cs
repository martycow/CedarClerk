namespace CedarClerk.Core;

/// <summary>
/// Dates on the blog, in the language of the page they appear on (T-094).
///
/// Explicit month tables rather than <c>CultureInfo</c>. The original reason was that the Pi's bare
/// runtime had no ICU data, which is why the rest of this codebase formats with
/// <c>InvariantCulture</c>; the DigitalOcean droplet does have ICU (11.08.2026), but the tables stay
/// — nine languages of month names are the product's own data, and reading them out of whatever the
/// host machine happens to ship is how the blog ended up showing a hardcoded Russian month header
/// above an English card date **on the same page**, with neither following the language the reader
/// had asked for.
///
/// Only the twelve month names and the day/month order are language-dependent here. Anything more
/// (declension after a numeral, for instance) would be a translation problem rather than a
/// formatting one, and Russian's genitive is handled by carrying both forms below.
/// </summary>
public static class BlogDateFormatter
{
    // Nominative — a standalone heading ("Август 2026"), which is what the timeline separator is.
    private static readonly Dictionary<string, string[]> MonthNames = new()
    {
        ["ru"] = ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"],
        ["en"] = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        ["de"] = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"],
        ["fr"] = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"],
        ["es"] = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"],
        ["ja"] = ["1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月"],
        ["uk"] = ["Січень", "Лютий", "Березень", "Квітень", "Травень", "Червень", "Липень", "Серпень", "Вересень", "Жовтень", "Листопад", "Грудень"],
        ["be"] = ["Студзень", "Люты", "Сакавік", "Красавік", "Май", "Чэрвень", "Ліпень", "Жнівень", "Верасень", "Кастрычнік", "Лістапад", "Снежань"],
        ["ka"] = ["იანვარი", "თებერვალი", "მარტი", "აპრილი", "მაისი", "ივნისი", "ივლისი", "აგვისტო", "სექტემბერი", "ოქტომბერი", "ნოემბერი", "დეკემბერი"],
    };

    // Genitive, for "17 августа" — Russian and Ukrainian inflect the month after a day number, and
    // "17 Август" is the kind of wrong that makes a page look machine-made.
    private static readonly Dictionary<string, string[]> MonthAfterDay = new()
    {
        ["ru"] = ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"],
        ["uk"] = ["січня", "лютого", "березня", "квітня", "травня", "червня", "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"],
        ["be"] = ["студзеня", "лютага", "сакавіка", "красавіка", "мая", "чэрвеня", "ліпеня", "жніўня", "верасня", "кастрычніка", "лістапада", "снежня"],
    };

    private static string[] Months(string? lang, bool afterDay)
    {
        var code = Normalize(lang);
        if (afterDay && MonthAfterDay.TryGetValue(code, out var inflected)) return inflected;
        return MonthNames.TryGetValue(code, out var names) ? names : MonthNames["en"];
    }

    private static string Normalize(string? lang) =>
        lang is not null && MonthNames.ContainsKey(lang) ? lang : "en";

    /// <summary>"Август 2026" / "August 2026" / "2026年8月" — the timeline's month separator.</summary>
    public static string MonthHeading(DateTime date, string? lang)
    {
        var month = Months(lang, afterDay: false)[date.Month - 1];
        // Japanese writes the year first and the month name already carries 月.
        return Normalize(lang) == "ja" ? $"{date.Year}年{month}" : $"{month} {date.Year}";
    }

    /// <summary>"17 августа 2026" / "17 August 2026" / "August 17, 2026" (en-US order stays out of it).</summary>
    public static string Date(DateTime date, string? lang)
    {
        var code = Normalize(lang);
        var month = Months(lang, afterDay: true)[date.Month - 1];
        return code switch
        {
            "ja" => $"{date.Year}年{date.Month}月{date.Day}日",
            _ => $"{date.Day} {month} {date.Year}",
        };
    }

    /// <summary>The same date with a time, for a card that shows when a post went out.</summary>
    public static string DateTimeShort(DateTime date, string? lang) =>
        $"{Date(date, lang)}, {date:HH\\:mm}";

    // ADR-115. The three above take a wall-clock DateTime and stay pure formatters — which is what
    // makes them testable against literal expected strings. These take the UTC instant the database
    // actually holds and do the conversion, so no caller has to remember it.
    //
    // The zone abbreviation is appended only where a time is shown: a reader anywhere in the world
    // needs to know what "14:05" means, while a bare date does not move enough to matter.

    /// <summary>UTC instant → "17 августа 2026" in the display zone.</summary>
    public static string DateLocal(DateTime utc, string? lang) => Date(DisplayTime.ToZone(utc), lang);

    /// <summary>UTC instant → "Август 2026" in the display zone.</summary>
    public static string MonthHeadingLocal(DateTime utc, string? lang) => MonthHeading(DisplayTime.ToZone(utc), lang);

    /// <summary>UTC instant → "17 августа 2026, 14:05 PDT" in the display zone.</summary>
    public static string DateTimeLocal(DateTime utc, string? lang) =>
        $"{DateTimeShort(DisplayTime.ToZone(utc), lang)} {DisplayTime.Abbreviation(utc)}";
}
