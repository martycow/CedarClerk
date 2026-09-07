namespace CedarClerk.Core;

// Explicit month tables keep the reader's language independent of the host's ICU data.
public static class BlogDateFormatter
{
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

    // Russian, Ukrainian and Belarusian inflect the month after a day number.
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

    public static string MonthHeading(DateTime date, string? lang)
    {
        var month = Months(lang, afterDay: false)[date.Month - 1];
        return Normalize(lang) == "ja" ? $"{date.Year}年{month}" : $"{month} {date.Year}";
    }

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

    public static string DateTimeShort(DateTime date, string? lang) =>
        $"{Date(date, lang)}, {date:HH\\:mm}";

    public static string DateLocal(DateTime utc, string? lang, string? timeZoneId = null) =>
        Date(DisplayTime.ToZone(utc, timeZoneId), lang);

    public static string MonthHeadingLocal(DateTime utc, string? lang, string? timeZoneId = null) =>
        MonthHeading(DisplayTime.ToZone(utc, timeZoneId), lang);

    public static string DateTimeLocal(DateTime utc, string? lang, string? timeZoneId = null) =>
        $"{DateTimeShort(DisplayTime.ToZone(utc, timeZoneId), lang)} {DisplayTime.Abbreviation(utc, timeZoneId)}";
}
