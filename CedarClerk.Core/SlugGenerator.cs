using CedarClerk.Localization;
using System.Text;

namespace CedarClerk.Core;

public static class SlugGenerator
{
    /// <summary>
    /// Generates a URL-friendly slug
    /// </summary>
    public static string Slugify(string title)
    {
        var lower = title.Trim().ToLowerInvariant();
        var sb = new StringBuilder();

        foreach (var c in lower)
        {
            if (Transliteration.CyrillicToLatin(c) is { } latin)
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
