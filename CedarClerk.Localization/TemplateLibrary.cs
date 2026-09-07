namespace CedarClerk.Localization;

/// <summary>
/// Wave 2 item 18 — the built-in devlog starter templates. A static library, deliberately NOT rows
/// seeded at registration: nothing exists in anyone's account until they pick one, and an unused
/// picker costs nobody a row. Bodies are complete TipTap documents in both content languages;
/// every one must render through the Blocks renderer without throwing (unit-tested per body per
/// language), because a starter that cannot publish is worse than none.
/// </summary>
public static class TemplateLibrary
{
    public sealed record Entry(string Id, string NameEn, string NameRu,
        string DescriptionEn, string DescriptionRu, string BodyEn, string BodyRu);

    public static string Name(Entry entry, string language) =>
        language == Languages.Russian ? entry.NameRu : entry.NameEn;

    public static string Description(Entry entry, string language) =>
        language == Languages.Russian ? entry.DescriptionRu : entry.DescriptionEn;

    /// <summary>Russian gets the Russian body; every other content language starts from English.</summary>
    public static string Body(Entry entry, string language) =>
        language == Languages.Russian ? entry.BodyRu : entry.BodyEn;

    public static Entry? Find(string id) => All.FirstOrDefault(e => e.Id == id);

    public static readonly IReadOnlyList<Entry> All =
    [
        new Entry("weekly-devlog",
            "Weekly devlog", "Еженедельный девлог",
            "The regular what-happened-this-week post that keeps a channel alive.",
            "Регулярный пост «что случилось за неделю», на котором держится канал.",
            """
            {"type":"doc","content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"This week"}]},
            {"type":"paragraph","content":[{"type":"text","text":"One or two sentences on what the week was about."}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Done"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"The thing that shipped"}]}]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"The thing that finally works"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"In progress"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"What is on the desk right now"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Next week"}]},
            {"type":"paragraph","content":[{"type":"text","text":"The one thing next week is for."}]}]}
            """,
            """
            {"type":"doc","content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"За неделю"}]},
            {"type":"paragraph","content":[{"type":"text","text":"Пара предложений о том, какой была неделя."}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Сделано"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"То, что доехало до релиза"}]}]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"То, что наконец работает"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"В работе"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Что сейчас на столе"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"На следующей неделе"}]},
            {"type":"paragraph","content":[{"type":"text","text":"Одна главная цель следующей недели."}]}]}
            """),

        new Entry("screenshot-saturday",
            "Screenshot Saturday", "Скриншот-суббота",
            "One strong image, a line of context, and the tag the whole gamedev feed reads.",
            "Один сильный кадр, строка контекста и тег, по которому его найдёт вся геймдев-лента.",
            """
            {"type":"doc","content":[
            {"type":"paragraph","content":[{"type":"text","text":"Drop this week's best shot here — one image beats four."}]},
            {"type":"paragraph","content":[{"type":"text","text":"One sentence about what the shot shows and why it was hard or fun to get."}]},
            {"type":"paragraph","content":[{"type":"text","marks":[{"type":"bold"}],"text":"#screenshotsaturday"}]}]}
            """,
            """
            {"type":"doc","content":[
            {"type":"paragraph","content":[{"type":"text","text":"Сюда — лучший кадр недели: один снимок работает лучше четырёх."}]},
            {"type":"paragraph","content":[{"type":"text","text":"Одно предложение о том, что на кадре и почему его было трудно или весело добыть."}]},
            {"type":"paragraph","content":[{"type":"text","marks":[{"type":"bold"}],"text":"#screenshotsaturday"}]}]}
            """),

        new Entry("patch-notes",
            "Patch notes", "Патчноут",
            "A release announcement readers can skim: added, changed, fixed.",
            "Анонс обновления, который читается по диагонали: добавлено, изменено, исправлено.",
            """
            {"type":"doc","content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Version 0.0.0"}]},
            {"type":"paragraph","content":[{"type":"text","text":"One line on what this update is about."}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Added"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Changed"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Fixed"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]}]}
            """,
            """
            {"type":"doc","content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Версия 0.0.0"}]},
            {"type":"paragraph","content":[{"type":"text","text":"Одной строкой: про что это обновление."}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Добавлено"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Изменено"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Исправлено"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]}]}
            """),

        new Entry("postmortem",
            "Postmortem", "Постмортем",
            "The honest look back after a release or a jam: what worked, what did not, what next.",
            "Честный разбор после релиза или джема: что сработало, что нет и что дальше.",
            """
            {"type":"doc","content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"What we set out to do"}]},
            {"type":"paragraph","content":[{"type":"text","text":"The goal as it was stated before the work began."}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"What went right"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"What went wrong"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"What we learned"}]},
            {"type":"paragraph","content":[{"type":"text","text":"The one lesson worth the whole story."}]}]}
            """,
            """
            {"type":"doc","content":[
            {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Что мы хотели сделать"}]},
            {"type":"paragraph","content":[{"type":"text","text":"Цель — так, как она звучала до начала работы."}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Что получилось"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Что пошло не так"}]},
            {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"…"}]}]}]},
            {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Чему научились"}]},
            {"type":"paragraph","content":[{"type":"text","text":"Один вывод, ради которого стоило пройти всю историю."}]}]}
            """),
    ];
}
