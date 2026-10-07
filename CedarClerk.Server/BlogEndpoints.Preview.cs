using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Wave 1 item 8 — the public read-only draft preview page (/preview/{token}, app host). The token
// is the whole capability: whoever holds it reads the draft's current body, nothing else — no
// comments, no reactions, no index anywhere pointing here, and robots told to stay out. A wrong or
// revoked token is a plain 404, indistinguishable from a path that never existed.
public static partial class BlogEndpoints
{
    public static class PreviewThemes
    {
        public const string Light = "light";
        public const string Dark = "dark";
    }

    public static async Task HandleDraftPreviewAsync(HttpContext ctx)
    {
        var token = ctx.Request.RouteValues["token"]?.ToString()
            ?? ctx.Request.Path.Value?.TrimEnd('/').Split('/').LastOrDefault()
            ?? "";
        if (token.Length == 0)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The token names a draft across owners and this page answers on the app host, where an
        // anonymous request resolves no tenant — a deliberate cross-owner read, scoped to this one
        // lookup the way TenantScopes prescribes rather than by widening the request's own context.
        using var scope = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>().CreatePlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();

        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.PreviewToken == token);
        if (draft is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(RenderDraftPreviewPage(draft.PrimaryLanguage, draft.CedarJson,
            draft.ArticleTitle ?? draft.Title, theme: null));
    }

    /// <summary>
    /// The preview page for one version of a draft — shared by the token page above and the
    /// owner's own per-language preview (ADR-239). <paramref name="theme"/> pins
    /// <c>data-theme</c> on the served document; null leaves the reader's own setting in charge.
    /// </summary>
    public static string RenderDraftPreviewPage(string language, string cedarJson, string title, string? theme)
    {
        var en = language != Languages.Russian;

        // Media stays relative: /media/* is served by this same host.
        var body = CedarToBlogHtmlRenderer.Render(cedarJson, "", language);
        var titleHeading = HeadingOutline.StartsWithHeading(cedarJson)
            ? ""
            : $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>";

        var banner = $"""
            <div class="preview-banner">{(BlogTexts.DraftPreviewThisIsWorkingCopyShared(en))}</div>
            """;

        var html = $"""
            {banner}
            <div class="post-reader">
            <span class="post-pin left" aria-hidden="true"></span>
            <span class="post-pin right" aria-hidden="true"></span>
            <div class="post-sheet">
            {titleHeading}
            {body}
            </div>
            </div>
            """;

        const string meta = "<meta name=\"robots\" content=\"noindex, nofollow\">";
        var page = PageShell(title, html, language, RenderHeader(new BlogHeaderInfo(null, []), language, preview: true), meta, mainClass: "site-main--post");

        return theme is PreviewThemes.Light or PreviewThemes.Dark
            ? page.Replace($"<html lang=\"{language}\">", $"<html lang=\"{language}\" data-theme=\"{theme}\">")
            : page;
    }
}
