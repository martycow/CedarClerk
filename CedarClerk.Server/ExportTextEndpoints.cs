using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Serves the copy-paste renderers (Wave 1 item 5): the Steam BBCode and itch.io HTML text of a
/// draft, for the export modal's "perfect clipboard" cards. Deliberately no publish semantics —
/// automating either store is the anti-feature; the clipboard is the product.
/// </summary>
public static class ExportTextEndpoints
{
    public const string TargetSteam = "steam";
    public const string TargetItch = "itch";

    public static void MapExportTextEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/drafts").RequireAuthorization();

        group.MapGet("/{id:guid}/export-text", async (Guid id, string? target, string? lang,
            ClaimsPrincipal user, CedarDbContext db, IConfiguration cfg) =>
        {
            if (target is not (TargetSteam or TargetItch))
                return Results.BadRequest(new { error = ErrorMessages.UnknownExportTarget(TargetSteam, TargetItch) });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == uid);
            if (draft is null) return Results.NotFound();

            var cedarJson = draft.CedarJson;
            if (lang is not null && lang != draft.PrimaryLanguage)
            {
                var translation = await db.DraftTranslations
                    .FirstOrDefaultAsync(t => t.DraftId == id && t.Language == lang);
                if (translation is null) return Results.NotFound();
                cedarJson = translation.CedarJson;
            }

            // Media must be absolute — the pasted text leaves this server. An account with no
            // tenant host yet gets relative paths dropped by the renderers' own rules.
            var host = await BlogTenant.HostForOwnerAsync(db, cfg, uid);
            var mediaBaseUrl = host is null ? null : $"https://{host}";

            var text = target == TargetSteam
                ? CedarToSteamBbcodeRenderer.Render(cedarJson, mediaBaseUrl)
                : CedarToItchHtmlRenderer.Render(cedarJson, mediaBaseUrl);
            return Results.Ok(new { text });
        });
    }
}
