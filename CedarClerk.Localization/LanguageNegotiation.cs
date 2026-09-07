namespace CedarClerk.Localization;

public static class LanguageNegotiation
{
    // Preserve header order for the existing account/browser preference contract.
    public static string? FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = part.Split(';')[0].Trim();
            // "ru-RU" and "ru" both mean Russian here — the app has no regional variants.
            var primary = tag.Split('-')[0].ToLowerInvariant();
            if (Languages.IsUiLanguage(primary)) return primary;
        }
        return null;
    }
}
