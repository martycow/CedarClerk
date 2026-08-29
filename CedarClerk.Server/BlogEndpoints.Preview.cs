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

        var lang = draft.PrimaryLanguage;
        var en = lang != Languages.Russian;
        var title = draft.ArticleTitle ?? draft.Title;

        // Media stays relative: /media/* is served by this same host.
        var body = CedarToBlogHtmlRenderer.Render(draft.CedarJson, "", lang);
        var titleHeading = HeadingOutline.StartsWithHeading(draft.CedarJson)
            ? ""
            : $"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>";

        var banner = $"""
            <div class="preview-banner">{(en
                ? "Draft preview — this is a working copy, shared by its author. It may change or disappear."
                : "Предпросмотр черновика — это рабочая копия, которой поделился автор. Она может измениться или исчезнуть.")}</div>
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
        ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(PageShell(title, html, lang, RenderHeader(null, lang), meta,
            mainClass: "site-main--post"));
    }
}
