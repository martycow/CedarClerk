using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Publishing;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Publish targets and the per-network text an author writes for them (T-087/T-089, ADR-077/078).
///
/// Connecting is deliberately NOT part of <see cref="IPublishTarget"/>: Telegram's connect is bot
/// membership discovery, Bluesky's is a handle plus an app password, and a Meta network's is a
/// review-gated OAuth. Three shapes with nothing in common, so each lives in its own endpoint.
/// </summary>
public static class PublishEndpoints
{
    public record ConnectBlueskyRequest(string Handle, string AppPassword, string? Service);
    public record TargetTextRequest(string Network, string Language, string Text);

    public static void MapPublishEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/publish").RequireAuthorization();

        // Every network this build can publish to, with its limits — the capability matrix (T-086)
        // as data the editor can render, plus which of the owner's accounts are connected.
        group.MapGet("/networks", async (ClaimsPrincipal user, CedarDbContext db, IEnumerable<IPublishTarget> targets) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var connected = await db.PublishTargets
                .Where(t => t.OwnerId == uid && t.IsActive)
                .Select(t => new { t.Id, t.Network, t.DisplayName, t.RemoteId, t.LastPublishedAt, t.LastError })
                .ToListAsync();

            return Results.Ok(targets.Select(t => new
            {
                network = t.Network,
                capabilities = t.Capabilities,
                accounts = connected.Where(c => c.Network == t.Network),
            }));
        });

        // Verified before it is stored: an app password that does not open a session is worth
        // rejecting now, with the user still looking at the field, rather than at publish time.
        group.MapPost("/bluesky/connect", async (
            ConnectBlueskyRequest req,
            ClaimsPrincipal user,
            CedarDbContext db,
            PublishTargetSecrets secrets,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var handle = req.Handle?.Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(handle) || string.IsNullOrWhiteSpace(req.AppPassword))
                return Results.BadRequest(new { error = "A handle and an app password are required" });

            var service = string.IsNullOrWhiteSpace(req.Service) ? BlueskyPublishTarget.DefaultService : req.Service.Trim();
            if (!Uri.TryCreate(service, UriKind.Absolute, out var serviceUri) || serviceUri.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest(new { error = "The service address must be an https:// URL" });

            var credentials = new BlueskyCredentials(handle, req.AppPassword, service);
            var http = httpFactory.CreateClient();
            http.BaseAddress = serviceUri;

            BlueskyPublishTarget.SessionResponse? session;
            try
            {
                session = await BlueskyPublishTarget.CreateSessionAsync(http, credentials, ct);
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(new { error = $"Could not reach {service}: {ex.Message}" }, statusCode: StatusCodes.Status502BadGateway);
            }

            if (session is null)
                return Results.Json(new { error = "Bluesky refused those credentials — check the handle and use an app password, not your account password" },
                    statusCode: StatusCodes.Status401Unauthorized);

            // Keyed by DID, not by handle: a handle can be renamed and the account stays the same.
            var existing = await db.PublishTargets.FirstOrDefaultAsync(
                t => t.OwnerId == uid && t.Network == PublishNetworks.Bluesky && t.RemoteId == session.Did, ct);

            var stored = secrets.Protect(JsonSerializer.Serialize(credentials with { Handle = session.Handle }));
            if (existing is not null)
            {
                existing.DisplayName = session.Handle;
                existing.CredentialsProtected = stored;
                existing.IsActive = true;
                existing.LastError = null;
            }
            else
            {
                db.PublishTargets.Add(new PublishTarget
                {
                    OwnerId = uid,
                    Network = PublishNetworks.Bluesky,
                    DisplayName = session.Handle,
                    RemoteId = session.Did,
                    CredentialsProtected = stored,
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { handle = session.Handle, did = session.Did });
        });

        // Deactivate and forget the credentials, but keep the row: it is what "this post went there"
        // still points at. Same choice as a disconnected Telegram channel.
        group.MapDelete("/targets/{id:guid}", async (Guid id, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var target = await db.PublishTargets.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == uid);
            if (target is null) return Results.NotFound();

            target.IsActive = false;
            target.CredentialsProtected = null;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ── The author's own text per network and language (T-087, ADR-077) ──────────────────
        group.MapGet("/texts/{draftId:guid}", async (Guid draftId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Drafts.AnyAsync(d => d.Id == draftId && d.OwnerId == uid)) return Results.NotFound();

            var texts = await db.DraftTargetTexts
                .Where(t => t.DraftId == draftId)
                .Select(t => new { t.Network, t.Language, t.Text })
                .ToListAsync();
            return Results.Ok(new { texts });
        });

        group.MapPut("/texts/{draftId:guid}", async (Guid draftId, TargetTextRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Drafts.AnyAsync(d => d.Id == draftId && d.OwnerId == uid)) return Results.NotFound();
            if (!PublishNetworks.IsKnown(req.Network)) return Results.BadRequest(new { error = $"Unknown network: {req.Network}" });
            if (!Languages.IsContentLanguage(req.Language)) return Results.BadRequest(new { error = $"Unknown language: {req.Language}" });

            var row = await db.DraftTargetTexts.FirstOrDefaultAsync(
                t => t.DraftId == draftId && t.Network == req.Network && t.Language == req.Language);

            // Empty means "no override" — the row goes rather than storing a blank that would read
            // as an intentional empty post to every later reader of this table.
            if (string.IsNullOrWhiteSpace(req.Text))
            {
                if (row is not null) db.DraftTargetTexts.Remove(row);
            }
            else if (row is not null)
            {
                row.Text = req.Text.Trim();
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.DraftTargetTexts.Add(new DraftTargetText
                {
                    DraftId = draftId,
                    Network = req.Network,
                    Language = req.Language,
                    Text = req.Text.Trim(),
                });
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
