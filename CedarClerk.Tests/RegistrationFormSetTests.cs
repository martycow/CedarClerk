using CedarClerk.Core;

namespace CedarClerk.Tests;

// FI4.1 — a private post can carry one registration form per language. These pin the two things
// the blog gate depends on: the right language's form is picked, and a broken blob degrades to
// the primary form instead of throwing on a live page.
public class RegistrationFormSetTests
{
    private const string RuForm = """{"intro":"Привет","requireName":true,"requireEmail":true,"questions":[]}""";
    private const string EnForm = """{"intro":"Hello","requireName":true,"requireEmail":false,"questions":[]}""";

    private static string Translations(params (string Lang, string Json)[] entries) =>
        "{" + string.Join(",", entries.Select(e => $"\"{e.Lang}\":{e.Json}")) + "}";

    [Fact]
    public void Picks_the_requested_language()
    {
        var form = RegistrationFormSet.Pick(RuForm, Translations(("en", EnForm)), "en");
        Assert.Equal("Hello", form!.Intro);
        Assert.False(form.RequireEmail);
    }

    [Fact]
    public void Falls_back_to_primary_when_that_language_has_no_form()
    {
        var form = RegistrationFormSet.Pick(RuForm, Translations(("en", EnForm)), "de");
        Assert.Equal("Привет", form!.Intro);
    }

    [Fact]
    public void Primary_language_never_reads_the_translations()
    {
        var form = RegistrationFormSet.Pick(RuForm, Translations(("ru", EnForm)), "ru");
        Assert.Equal("Привет", form!.Intro);
    }

    [Fact]
    public void No_form_at_all_is_null()
    {
        Assert.Null(RegistrationFormSet.Pick(null, null, "en"));
    }

    [Fact]
    public void Malformed_translations_fall_back_rather_than_throw()
    {
        var form = RegistrationFormSet.Pick(RuForm, "not json at all", "en");
        Assert.Equal("Привет", form!.Intro);
    }

    [Fact]
    public void Lists_the_languages_a_reader_could_be_greeted_in()
    {
        var languages = RegistrationFormSet.LanguagesWithForm(RuForm, Translations(("en", EnForm), ("de", EnForm)));
        Assert.Equal(["ru", "en", "de"], languages);
    }

    [Fact]
    public void Lists_nothing_when_the_post_has_no_form()
    {
        Assert.Empty(RegistrationFormSet.LanguagesWithForm(null, null));
    }

    [Fact]
    public void SetTranslation_adds_and_reads_back()
    {
        var json = RegistrationFormSet.SetTranslation(null, "en", EnForm);
        Assert.Equal("Hello", RegistrationFormSet.Pick(RuForm, json, "en")!.Intro);
    }

    [Fact]
    public void SetTranslation_survives_a_second_round_trip()
    {
        // The map is stored as nested JSON, not as an escaped string — writing a second language
        // into it must not double-encode the first.
        var json = RegistrationFormSet.SetTranslation(null, "en", EnForm);
        json = RegistrationFormSet.SetTranslation(json, "de", EnForm);
        Assert.Equal("Hello", RegistrationFormSet.Pick(RuForm, json, "en")!.Intro);
        Assert.Equal("Hello", RegistrationFormSet.Pick(RuForm, json, "de")!.Intro);
    }

    [Fact]
    public void SetTranslation_clearing_the_last_language_stores_null()
    {
        var json = RegistrationFormSet.SetTranslation(null, "en", EnForm);
        Assert.Null(RegistrationFormSet.SetTranslation(json, "en", null));
    }
}

// ADR-060 — the v2 multi-language blob: one skeleton (stable question/option ids), per-language
// text overlays, resolved to a single-language definition for the renderer and validation.
public class RegistrationFormV2Tests
{
    private const string V2 = """
        {"v":2,"languages":["ru","en"],
         "intro":{"ru":"Привет","en":"Hello"},
         "requireName":true,"requireEmail":true,
         "questions":[
           {"id":"genre","type":"choice","required":true,
            "label":{"ru":"Жанр","en":"Genre"},
            "options":[{"id":"o1","label":{"ru":"Да","en":"Yes"}},{"id":"o2","label":{"ru":"Нет"}}]},
           {"id":"about","type":"text","label":{"ru":"О себе"}}
         ]}
        """;

    [Fact]
    public void Detects_v2_blobs()
    {
        Assert.True(RegistrationFormSet.IsMultiLanguage(V2));
        Assert.False(RegistrationFormSet.IsMultiLanguage("""{"questions":[]}"""));
        Assert.False(RegistrationFormSet.IsMultiLanguage(null));
        Assert.False(RegistrationFormSet.IsMultiLanguage("not json"));
    }

    [Fact]
    public void Pick_resolves_the_requested_language_from_a_v2_primary()
    {
        var form = RegistrationFormSet.Pick(V2, null, "en");
        Assert.Equal("Hello", form!.Intro);
        Assert.Equal("Genre", form.Questions[0].Label);
        // The rendered option value is the stable id, so "Да" and "Yes" aggregate as one answer.
        Assert.Equal(new RegistrationOption("o1", "Yes"), form.Questions[0].Options[0]);
    }

    [Fact]
    public void Missing_translations_fall_back_per_string_not_wholesale()
    {
        var form = RegistrationFormSet.Pick(V2, null, "en")!;
        // "Нет" has no EN label; the option survives with the RU text rather than vanishing.
        Assert.Equal(new RegistrationOption("o2", "Нет"), form.Questions[0].Options[1]);
        // A question with no EN label at all keeps its RU one.
        Assert.Equal("О себе", form.Questions[1].Label);
    }

    [Fact]
    public void V2_ignores_the_legacy_translations_column()
    {
        var form = RegistrationFormSet.Pick(V2, """{"en":{"intro":"stale"}}""", "en");
        Assert.Equal("Hello", form!.Intro);
    }

    [Fact]
    public void LanguagesWithForm_reads_the_v2_language_list()
    {
        Assert.Equal(["ru", "en"], RegistrationFormSet.LanguagesWithForm(V2, null));
    }

    [Fact]
    public void UpgradeToV2_wraps_a_v1_blob_and_keeps_answers_compatible()
    {
        const string v1 = """{"intro":"Hi","requireName":true,"questions":[{"id":"g","label":"Genre","type":"choice","options":["RPG"]}]}""";
        var upgraded = RegistrationFormTexts.UpgradeToV2(v1, "ru");

        Assert.True(RegistrationFormSet.IsMultiLanguage(upgraded));
        var form = RegistrationFormSet.Pick(upgraded, null, "ru")!;
        Assert.Equal("Hi", form.Intro);
        // v1 option ids are the labels — an upgraded form keeps storing what it always stored.
        Assert.Equal(new RegistrationOption("RPG", "RPG"), form.Questions[0].Options[0]);
    }

    [Fact]
    public void UpgradeToV2_passes_a_v2_blob_through_unchanged()
    {
        Assert.Same(V2, RegistrationFormTexts.UpgradeToV2(V2, "ru"));
    }

    [Fact]
    public void ExtractTexts_walks_intro_labels_and_options_in_fixed_order()
    {
        var texts = RegistrationFormTexts.ExtractTexts(V2, "ru");
        Assert.Equal(["Привет", "Жанр", "Да", "Нет", "О себе"], texts);
    }

    [Fact]
    public void ExtractTexts_yields_blanks_for_missing_slots_so_order_is_stable()
    {
        var texts = RegistrationFormTexts.ExtractTexts(V2, "en");
        Assert.Equal(["Hello", "Genre", "Yes", "", ""], texts);
    }

    [Fact]
    public void ReplaceTexts_fills_the_target_language_and_registers_it()
    {
        var replaced = RegistrationFormTexts.ReplaceTexts(V2, "de", ["Hallo", "Genre", "Ja", "Nein", "Über dich"]);

        var form = RegistrationFormSet.Pick(replaced, null, "de")!;
        Assert.Equal("Hallo", form.Intro);
        Assert.Equal(new RegistrationOption("o1", "Ja"), form.Questions[0].Options[0]);
        Assert.Equal("Über dich", form.Questions[1].Label);
        Assert.Equal(["ru", "en", "de"], RegistrationFormSet.LanguagesWithForm(replaced, null));
    }

    [Fact]
    public void ReplaceTexts_skips_blanks_so_missing_source_stays_missing()
    {
        var replaced = RegistrationFormTexts.ReplaceTexts(V2, "de", ["Hallo", "Genre", "Ja", "", ""]);
        var form = RegistrationFormSet.Pick(replaced, null, "de")!;
        // The blank slots fall back to the skeleton's first language rather than storing "".
        Assert.Equal("Нет", form.Questions[0].Options[1].Label);
    }

    [Fact]
    public void ReplaceTexts_with_wrong_count_throws()
    {
        Assert.Throws<ArgumentException>(() => RegistrationFormTexts.ReplaceTexts(V2, "de", ["only one"]));
    }
}
