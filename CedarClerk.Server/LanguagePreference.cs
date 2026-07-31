using System.Security.Claims;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// T-050 — which language this request's messages should be in. The account's own
/// <c>UiLanguage</c> wins, since that is the language the app's chrome is already in; the
/// browser's Accept-Language is the fallback for anonymous requests (the blog's registration gate
/// is the one that matters there).
/// </summary>
public static class LanguagePreference
{
    public static async Task<string?> OfUserAsync(HttpContext ctx)
    {
        var uid = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (uid is null) return null;

        var db = ctx.RequestServices.GetService<CedarDbContext>();
        if (db is null) return null;

        var stored = await db.Users.Where(u => u.Id == uid).Select(u => u.UiLanguage).FirstOrDefaultAsync();
        return Languages.IsUiLanguage(stored ?? "") ? stored : null;
    }

    /// <summary>
    /// First acceptable tag we recognise, in the order the browser listed them. Deliberately not a
    /// full RFC 4647 q-value sort: the header's own order is already preference order in every
    /// browser that ships, and a wrong pick here costs a message in the wrong language, not a bug.
    /// </summary>
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
