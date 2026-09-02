using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Publishing;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// The editor's Preview tab (ADR-239 clause 10): what Telegram would receive and what the blog
/// would show, per language, for the owner's own draft — projected on the server and never sent.
/// </summary>
public static class PreviewEndpoints
{
    public static void MapPreviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/drafts").RequireAuthorization();

        group.MapGet("/{id:guid}/preview/telegram", async (Guid id, string? lang, ClaimsPrincipal user,
            CedarDbContext db, IEnumerable<IPublishTarget> targets, CancellationToken ct) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var capabilities = targets.First(t => t.Network == PublishNetworks.Telegram).Capabilities;
            return await TelegramAsync(db, uid, id, lang, capabilities, ct);
        });

        group.MapGet("/{id:guid}/preview/blog", async (Guid id, string? lang, string? theme, ClaimsPrincipal user,
            CedarDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return await BlogAsync(db, uid, id, lang, theme, ctx.Response, ct);
        });
    }

    // Media URLs stay relative (/media/…): the phone is drawn on the app host, where the editor's
    // cookie already reaches them, and the base URL takes no part in the split or the counts.
    public static async Task<IResult> TelegramAsync(CedarDbContext db, string uid, Guid id, string? lang,
        PublishCapabilities capabilities, CancellationToken ct = default)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
        if (draft is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });

        var language = lang ?? draft.PrimaryLanguage;
        var document = await DraftRevisionService.ResolveAsync(db, draft, language, ct);
        if (document is null) return Results.NotFound(new { error = ErrorMessages.NoVersionInLanguage(language) });

        var blocks = CedarToTelegramBlocksRenderer.Render(document.Value.CedarJson);
        var parts = TelegramThreadSplitter.Split(blocks, capabilities);
        var buttons = TelegramPublishTarget.ParseCtaButtons(draft.CtaButtonsJson);
        return Results.Ok(TelegramPreviewProjection.Project(language, parts, buttons, capabilities));
    }

    public static async Task<IResult> BlogAsync(CedarDbContext db, string uid, Guid id, string? lang, string? theme,
        HttpResponse response, CancellationToken ct = default)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
        if (draft is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });

        var language = lang ?? draft.PrimaryLanguage;
        var document = await DraftRevisionService.ResolveAsync(db, draft, language, ct);
        if (document is null) return Results.NotFound(new { error = ErrorMessages.NoVersionInLanguage(language) });

        var title = language == draft.PrimaryLanguage
            ? draft.ArticleTitle ?? document.Value.Title
            : document.Value.Title;

        response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
        var html = BlogEndpoints.RenderDraftPreviewPage(language, document.Value.CedarJson, title, theme);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
