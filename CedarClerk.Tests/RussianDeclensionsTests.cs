using CedarClerk.Localization;
using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-040. These are suggestions the author reads before accepting, which is the only reason a
// suffix rule is allowed near Russian morphology at all — so what is tested is that the common
// patterns come out right and that everything else comes out empty rather than wrong.
public class RussianDeclensionsTests
{
    [Fact]
    public void A_masculine_hard_stem_declines()
    {
        var forms = RussianDeclensions.Suggest("рендерер");

        Assert.Contains("рендерера", forms);
        Assert.Contains("рендереру", forms);
        Assert.Contains("рендерером", forms);
        // The nominative is what the author typed — repeating it as an alias would be noise.
        Assert.DoesNotContain("рендерер", forms);
    }

    [Fact]
    public void A_feminine_noun_declines()
    {
        var forms = RussianDeclensions.Suggest("сборка");

        Assert.Contains("сборки", forms);
        Assert.Contains("сборке", forms);
        Assert.Contains("сборку", forms);
    }

    [Fact]
    public void A_neuter_noun_declines()
    {
        Assert.Contains("окна", RussianDeclensions.Suggest("окно"));
        Assert.Contains("поля", RussianDeclensions.Suggest("поле"));
    }

    // The one that separates a rule from a guess: the vowel disappears when the word declines.
    [Fact]
    public void A_fleeting_vowel_disappears()
    {
        var forms = RussianDeclensions.Suggest("уровень");

        Assert.Contains("уровня", forms);
        Assert.Contains("уровнем", forms);
        Assert.DoesNotContain("уровеня", forms);
    }

    [Theory]
    [InlineData("renderer")]          // not Russian — English words do not decline this way
    [InlineData("игровой движок")]    // two words agree with each other; a suffix rule cannot
    [InlineData("ИИ")]                // too short to be anything but a guess
    public void Anything_it_is_not_confident_about_suggests_nothing(string term)
    {
        Assert.Empty(RussianDeclensions.Suggest(term));
    }

    [Fact]
    public void Suggestions_never_repeat_themselves()
    {
        var forms = RussianDeclensions.Suggest("кедр");

        Assert.Equal(forms.Count, forms.Distinct().Count());
    }
}
