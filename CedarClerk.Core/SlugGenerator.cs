using System.Text;

namespace CedarClerk.Core;

public static class SlugGenerator
{
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "",
        ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    public static string Slugify(string title)
    {
        var lower = title.Trim().ToLowerInvariant();
        var sb = new StringBuilder();

        foreach (var c in lower)
        {
            if (Cyrillic.TryGetValue(c, out var latin))
                sb.Append(latin);
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(c);
            else
                sb.Append('-');
        }

        var collapsed = new StringBuilder();
        var lastWasDash = false;
        foreach (var c in sb.ToString())
        {
            if (c == '-')
            {
                if (!lastWasDash && collapsed.Length > 0)
                    collapsed.Append('-');
                lastWasDash = true;
            }
            else
            {
                collapsed.Append(c);
                lastWasDash = false;
            }
        }

        var slug = collapsed.ToString().TrimEnd('-');
        return string.IsNullOrEmpty(slug) ? "post" : slug;
    }
}
