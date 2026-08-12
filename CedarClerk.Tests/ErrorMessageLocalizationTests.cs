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

    /// <summary>
    /// The desktop agent (ADR-117) answers in English on purpose, and is exempt.
    ///
    /// Its only client is the Electron main process, which is English by decision (ADR-116, "the shell
    /// speaks English"). No human reads these strings: the page never displays them — `AssetSyncService`
    /// catches agent failures as a *kind* and supplies its own localised wording, logging the original
    /// to the console. Putting them in <c>ErrorMessages</c> would translate sentences for a reader that
    /// does not exist, and would put machine diagnostics in the catalogue of things authors see.
    ///
    /// The exemption is a directory rather than a pattern, so a genuinely user-facing endpoint cannot
    /// quietly inherit it by being written in the same style.
    /// </summary>
    private const string AgentDirectory = "Agent";

    // T-050 closed 01.08.2026: the ~60 inline English literals in the endpoint files moved into
    // ErrorMessages. This is what keeps the 61st from being written — the same argument
    // SchemaDriftGuardTests makes about migrations, and the reason that one is trusted.
    [Fact]
    public void No_endpoint_answers_with_a_hardcoded_english_string()
    {
        var serverDir = FindServerDirectory();
        var pattern = new System.Text.RegularExpressions.Regex(@"error\s*=\s*""");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(serverDir, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(file) is { } dir && Path.GetFileName(dir) == AgentDirectory) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // `error = "..."` is how every endpoint reports a failure; anything else is a
                // variable, an interpolation, or already an ErrorMessages member.
                if (pattern.IsMatch(lines[i]))
                    offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + lines[i].Trim());
            }
        }

        Assert.True(offenders.Count == 0,
            "These answer in English regardless of the reader's language — move them to ErrorMessages:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string FindServerDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "CedarClerk.Server")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "CedarClerk.Server");
    }
}
