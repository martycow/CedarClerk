using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

public class DialogueEndpointsTests
{
    [Fact]
    public void Translation_sheet_languages_are_supported_unique_and_canonically_ordered()
    {
        var languages = DialogueEndpoints.NormalizeSheetLanguages([
            " JA ", "xx", "en", "RU", "en", "de-DE", "",
        ]);

        Assert.Equal(["ru", "en", "ja"], languages);
    }
}
