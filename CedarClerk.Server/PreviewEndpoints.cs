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

        group.MapGet("/{id:guid}/preview/micro", async (Guid id, string network, string? lang, ClaimsPrincipal user,
            CedarDbContext db, IEnumerable<IPublishTarget> targets, IConfiguration cfg, CancellationToken ct) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var target = targets.FirstOrDefault(t => t.Network == network && t.Capabilities.DerivesShortPost);
            if (target is null) return Results.BadRequest(new { error = ErrorMessages.UnknownNetwork(network) });
            return await MicroAsync(db, cfg, uid, id, network, lang, target.Capabilities, ct);
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

    // The author's own text (ADR-077) and the blog link (ADR-094) come from the same rows the
    // targets read, so the card shows the announcement that would actually go out.
    public static async Task<IResult> MicroAsync(CedarDbContext db, IConfiguration cfg, string uid, Guid id,
        string network, string? lang, PublishCapabilities capabilities, CancellationToken ct = default)
    {
        var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid, ct);
        if (draft is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });

        var language = lang ?? draft.PrimaryLanguage;
        var document = await DraftRevisionService.ResolveAsync(db, draft, language, ct);
        if (document is null) return Results.NotFound(new { error = ErrorMessages.NoVersionInLanguage(language) });

        var authorText = await db.DraftTargetTexts
            .Where(t => t.DraftId == id && t.OwnerId == uid && t.Network == network && t.Language == language)
            .Select(t => t.Text)
            .FirstOrDefaultAsync(ct);
        var blogUrl = await MicroThreadPlan.BlogUrlAsync(draft, language, db, cfg, ct);
        var reserve = MicroThreadPlan.LinkReserve(network, blogUrl);
        return Results.Ok(MicroPreviewProjection.Project(network, language, document.Value.CedarJson, authorText, blogUrl, reserve, capabilities));
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
