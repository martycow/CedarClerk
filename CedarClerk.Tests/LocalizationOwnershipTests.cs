using System.Globalization;
using CedarClerk.Core;
using CedarClerk.Localization;

namespace CedarClerk.Tests;

public class LocalizationOwnershipTests
{
    [Fact]
    public void Localization_has_no_application_or_framework_dependencies()
    {
        var assembly = typeof(Languages).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name!.StartsWith("CedarClerk.") || reference.Name.StartsWith("Microsoft.AspNetCore"));
        foreach (var type in new[] { typeof(EmailTexts), typeof(BlogDateFormatter), typeof(DisplayTime),
                     typeof(TimeZones), typeof(RussianDeclensions), typeof(LocalizedTextMap),
                     typeof(BlogTexts), typeof(LandingTexts), typeof(TemplateLibrary) })
            Assert.Same(assembly, type.Assembly);
    }

    [Fact]
    public void Every_content_language_has_reader_chrome_and_an_endonym()
    {
        Assert.Equal(9, Languages.ContentLanguages.Count);
        Assert.Equal(new[] { "en", "ru" }, Languages.InterfaceDictionaries);
        foreach (var language in Languages.ContentLanguages)
        {
            Assert.NotEqual(language.ToUpperInvariant(), Languages.EndonymOf(language));
            var gate = BlogTexts.GateChrome[language];
            Assert.False(string.IsNullOrWhiteSpace(BlogTexts.ReadingLabels[language].Menu));
            var html = CedarToBlogHtmlRenderer.RegistrationFormHtml(RegistrationFormDefinition.Default, "Title", language);
            Assert.Contains(gate.Heading, html);
            Assert.Contains(gate.Submit, html);
        }
        Assert.Equal("XX", Languages.EndonymOf("xx"));
        Assert.Contains(BlogTexts.GateChrome["en"].Heading,
            CedarToBlogHtmlRenderer.RegistrationFormHtml(RegistrationFormDefinition.Default, "Title", "xx"));
    }

    [Fact]
    public async Task Email_language_is_isolated_between_concurrent_requests()
    {
        async Task<string> Render(string language)
        {
            var original = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                await Task.Yield();
                return EmailTexts.ConfirmSubject;
            }
            finally { CultureInfo.CurrentUICulture = original; }
        }

        var texts = await Task.WhenAll(Render("ru-RU"), Render("en-US"), Render("de-DE"));
        Assert.Equal("Подтвердите адрес почты — Cedar Clerk", texts[0]);
        Assert.Equal("Confirm your email — Cedar Clerk", texts[1]);
        Assert.Equal(texts[1], texts[2]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void Moving_email_resources_preserves_escaping(string language)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var html = EmailTexts.TeamInviteBody("<script>team</script>", "<b>name</b>", "https://example.test/invite");
            Assert.DoesNotContain("<script>", html);
            Assert.DoesNotContain("<b>name</b>", html);
            Assert.Contains("&lt;script&gt;team&lt;/script&gt;", html);
            Assert.Contains("https://example.test/invite", html);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }
}
