using System.Globalization;
using CedarClerk.Localization;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// T-050. The whole design rests on two things that are easy to break silently: messages must be
// properties (a const is baked into the caller and can never be language-dependent), and the
// language must come from CurrentUICulture rather than a parameter.
public class ErrorMessageLocalizationTests
{
    private static string Under(string culture, Func<string> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return read();
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void A_russian_ui_gets_russian_messages()
    {
        Assert.Equal("Черновик не найден.", Under("ru", () => ErrorMessages.DraftNotFound));
        Assert.Contains("Дневной лимит", Under("ru", () => ErrorMessages.AiDailyLimitReached(20)));
    }

    [Fact]
    public void Every_other_ui_language_falls_back_to_english()
    {
        // Matches the app's own dictionaries: only ru is translated, everything else is English.
        Assert.Equal("Draft not found.", Under("en", () => ErrorMessages.DraftNotFound));
        Assert.Equal("Draft not found.", Under("de", () => ErrorMessages.DraftNotFound));
        Assert.Equal("Draft not found.", Under("ja", () => ErrorMessages.DraftNotFound));
    }

    [Fact]
    public void A_regional_russian_culture_still_counts_as_russian()
    {
        Assert.Equal("Черновик не найден.", Under("ru-RU", () => ErrorMessages.DraftNotFound));
    }

    [Fact]
    public async Task The_language_survives_an_await()
    {
        // The reason no call site has to pass a language: CurrentUICulture flows across
        // continuations. If that ever stopped being true, every message would silently go English.
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");
            await Task.Yield();
            Assert.Equal("Черновик не найден.", ErrorMessages.DraftNotFound);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("ru-RU,ru;q=0.9,en-US;q=0.8", "ru")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("de-DE,de;q=0.9,ru;q=0.5", "de")]
    // A language the app doesn't know is skipped rather than accepted.
    [InlineData("zz,ru", "ru")]
    [InlineData("", null)]
    [InlineData("zz-ZZ", null)]
    public void Accept_language_picks_the_first_language_the_app_knows(string header, string? expected)
    {
        Assert.Equal(expected, LanguagePreference.FromAcceptLanguage(header));
    }
}
