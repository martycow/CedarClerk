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

}
