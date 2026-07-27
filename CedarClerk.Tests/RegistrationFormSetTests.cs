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
