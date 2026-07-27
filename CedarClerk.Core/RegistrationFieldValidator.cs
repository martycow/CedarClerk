using System.Globalization;

namespace CedarClerk.Core;

// Field rules for the private-post registration form (N6). Lives in Core so the server check and
// the browser check can't drift into disagreeing about what a name is — the page script mirrors
// this rule, and the server is the one that decides, since a form POST is trivially replayable.
public static class RegistrationFieldValidator
{
    public const int NameMinLength = 2;

    // Letters of ANY alphabet, spaces and '-'. Cyrillic names have to pass, so this is a Unicode
    // category test, not an A-Z range. Digits and every other punctuation mark are rejected —
    // Marty's rule is "no special characters except the hyphen".
    public static bool IsValidName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < NameMinLength)
            return false;

        var hasLetter = false;
        foreach (var c in name)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
                continue;
            }
            // A name made only of hyphens and spaces isn't a name — hence the hasLetter check.
            if (c is '-' || char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator)
                continue;

            return false;
        }

        return hasLetter;
    }
}
