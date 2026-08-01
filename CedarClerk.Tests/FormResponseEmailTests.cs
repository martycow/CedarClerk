using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-033 — the reply a respondent gets. It is per language like everything else a reader sees, and
// absent means no mail at all: an owner who has not written one has not agreed to write to their
// readers, and a generated "thanks" in their voice would be words they never chose.
public class FormResponseEmailTests
{
    private const string V2 = """
        {"v":2,"languages":["ru","en"],"intro":{"ru":"Привет","en":"Hi"},
         "requireName":true,"requireNickname":false,"requireEmail":true,"requireSocial":false,
         "questions":[],
         "responseEmailSubject":{"ru":"Спасибо","en":"Thank you"},
         "responseEmailBody":{"ru":"Мы получили вашу заявку.","en":"We got your submission."}}
        """;

    [Theory]
    [InlineData("ru", "Спасибо", "Мы получили вашу заявку.")]
    [InlineData("en", "Thank you", "We got your submission.")]
    public void The_reply_follows_the_language_the_form_was_shown_in(string lang, string subject, string body)
    {
        var form = RegistrationFormSet.ResolveV2(V2, lang);

        Assert.NotNull(form);
        Assert.Equal(subject, form!.ResponseEmailSubject);
        Assert.Equal(body, form.ResponseEmailBody);
    }

    [Fact]
    public void A_form_without_a_reply_asks_for_no_mail_to_be_sent()
    {
        const string json = """
            {"v":2,"languages":["ru"],"intro":{"ru":"Привет"},"requireName":true,
             "requireNickname":false,"requireEmail":true,"requireSocial":false,"questions":[]}
            """;

        var form = RegistrationFormSet.ResolveV2(json, "ru");

        Assert.NotNull(form);
        Assert.Null(form!.ResponseEmailBody);
    }

    // Every form written before this feature existed parses unchanged and asks for no mail —
    // which is what the submit path checks before sending anything.
    [Fact]
    public void A_v1_form_still_parses_and_asks_for_nothing()
    {
        var form = RegistrationFormDefinition.Parse("""
            {"intro":"Заполните форму","requireName":true,"requireNickname":false,
             "requireEmail":true,"requireSocial":false,"questions":[]}
            """);

        Assert.NotNull(form);
        Assert.Equal("Заполните форму", form!.Intro);
        Assert.Null(form.ResponseEmailSubject);
        Assert.Null(form.ResponseEmailBody);
    }
}
