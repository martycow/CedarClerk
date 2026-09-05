using System.Globalization;
using System.Reflection;
using CedarClerk.Localization;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// T-050 / T-194. The whole design rests on three things that are easy to break silently: messages
// must be properties (a const is baked into the caller and can never be language-dependent), the
// language must come from CurrentUICulture rather than a parameter, and every language table must
// hold every member — a missing row falls back to English without anybody noticing.
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
    public void English_is_the_member_itself_and_the_answer_for_a_language_the_app_does_not_know()
    {
        Assert.Equal("Draft not found.", Under("en", () => ErrorMessages.DraftNotFound));
        Assert.Equal("Draft not found.", Under("it", () => ErrorMessages.DraftNotFound));
        Assert.Equal("Daily AI limit (20 calls) reached — resets at midnight UTC.",
            Under("pt-BR", () => ErrorMessages.AiDailyLimitReached(20)));
    }

    [Fact]
    public void A_message_a_table_lacks_falls_back_to_english()
    {
        // Every real member is translated (the test below insists), so the fallback is exercised on
        // a key no table holds — the situation a freshly added member is in until its rows exist.
        Assert.Equal("Nothing here", Under("de", () => ErrorMessages.T("Nothing here", key: "NoSuchMember")));
        Assert.Equal("Nothing here: 3",
            Under("ja", () => ErrorMessages.T("Nothing here: {0}", [3], key: "NoSuchMember")));
    }

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("de-AT", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("ja-JP", "ja")]
    [InlineData("uk-UA", "uk")]
    [InlineData("be-BY", "be")]
    [InlineData("ka-GE", "ka")]
    public void A_regional_culture_still_counts_as_its_language(string culture, string language)
    {
        Assert.Equal(ErrorMessages.Translations[language][nameof(ErrorMessages.DraftNotFound)],
            Under(culture, () => ErrorMessages.DraftNotFound));
    }

    [Fact]
    public void Every_ui_language_has_a_table_and_every_table_speaks_for_every_member()
    {
        var members = Members();
        Assert.NotEmpty(members);

        var problems = new List<string>();
        foreach (var language in Languages.UiLanguages.Where(l => l != Languages.English))
        {
            if (!ErrorMessages.Translations.TryGetValue(language, out var table))
            {
                problems.Add($"{language}: no table at all");
                continue;
            }

            foreach (var orphan in table.Keys.Except(members.Select(m => m.Name)))
                problems.Add($"{language}: row '{orphan}' names no member");

            foreach (var member in members)
            {
                if (!table.TryGetValue(member.Name, out var translation))
                {
                    problems.Add($"{language}: {member.Name} is missing");
                    continue;
                }

                var parameters = Parameters(member);
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (!translation.Contains("{" + i))
                        problems.Add($"{language}: {member.Name} drops argument {{{i}}}");
                }

                // The member must actually answer with the row under that culture: this is what
                // catches a row keyed by a typo, an argument bound to the key, or a table that was
                // never wired into the lookup. Matched by shape, because a member may reshape its
                // argument on the way in (an upper-cased language code, a byte count in MB).
                var actual = Under(language, () => Invoke(member, parameters.Select(Sample).ToArray()));
                if (!Shape(translation).IsMatch(actual))
                    problems.Add($"{language}: {member.Name} answered '{actual}' rather than '{translation}'");
            }
        }

        Assert.True(problems.Count == 0,
            "Every ErrorMessages member needs a row in every language table (ErrorMessages.<lang>.cs):"
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_member_formats_its_arguments_in_english_too()
    {
        // A `{0}` the English text forgot would surface as a literal brace in production.
        foreach (var member in Members())
        {
            var parameters = Parameters(member);
            var text = Under("en", () => Invoke(member, parameters.Select(Sample).ToArray()));
            Assert.DoesNotContain("{", text);
            foreach (var parameter in parameters.Where(p => p.ParameterType == typeof(string)))
                Assert.Contains((string)Sample(parameter), text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static MemberInfo[] Members() =>
        typeof(ErrorMessages).GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m switch
            {
                PropertyInfo p => p.PropertyType == typeof(string),
                MethodInfo f => f.ReturnType == typeof(string) && !f.IsSpecialName,
                _ => false,
            })
            .ToArray();

    private static System.Text.RegularExpressions.Regex Shape(string template) =>
        new("^" + System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Escape(template), @"\\{\d+(:[^}]*)?}", ".+?") + "$",
            System.Text.RegularExpressions.RegexOptions.Singleline);

    private static ParameterInfo[] Parameters(MemberInfo member) =>
        member is MethodInfo method ? method.GetParameters() : [];

    private static string Invoke(MemberInfo member, object[] args) => member switch
    {
        PropertyInfo p => (string)p.GetValue(null)!,
        MethodInfo f => (string)f.Invoke(null, args)!,
        _ => throw new InvalidOperationException(member.Name),
    };

    private static object Sample(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var t when t == typeof(int) => 7,
        var t when t == typeof(long) => 7L,
        var t when t == typeof(DateTime) => new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
        var t when t == typeof(string) => "sample-" + parameter.Name,
        var t => throw new InvalidOperationException($"No sample for {t.Name} ({parameter.Name})"),
    };

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
    // ErrorMessages; T-194 took the interpolated ones with them. This is what keeps the next one
    // from being written — the same argument SchemaDriftGuardTests makes about migrations, and the
    // reason that one is trusted.
    [Fact]
    public void No_endpoint_answers_with_a_hardcoded_english_string()
    {
        var serverDir = FindServerDirectory();
        // `error = "..."` and `error = $"..."` are how every endpoint reports a failure; anything
        // else is a variable or already an ErrorMessages member.
        var pattern = new System.Text.RegularExpressions.Regex(@"error\s*=\s*\$?""");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(serverDir, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(file) is { } dir && Path.GetFileName(dir) == AgentDirectory) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
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
