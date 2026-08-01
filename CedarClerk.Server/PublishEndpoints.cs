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
    public record QueuePublishRequest(Guid DraftId, List<Guid>? TargetIds, string? Language = null, string? ConfirmedFingerprint = null, bool SplitIntoThread = false);
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
                return Results.BadRequest(new { error = ErrorMessages.HandleAndAppPasswordRequired });

            var service = string.IsNullOrWhiteSpace(req.Service) ? BlueskyPublishTarget.DefaultService : req.Service.Trim();
            if (!Uri.TryCreate(service, UriKind.Absolute, out var serviceUri) || serviceUri.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest(new { error = ErrorMessages.ServiceMustBeHttps });

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
                return Results.Json(new { error = ErrorMessages.BlueskyCredentialsRefused },
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

        // T-106 — what the thread would look like, before anything is sent. The author sees the
        // parts, their sizes and where each one starts; splitting a post into eight messages is a
        // loud act and must never be a surprise.
        group.MapGet("/thread-preview", async (Guid draftId, string network, string? language,
            ClaimsPrincipal user, CedarDbContext db, IEnumerable<IPublishTarget> targets, IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerId == uid);
            if (draft is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });

            var implementation = targets.FirstOrDefault(t => t.Network == network);
            if (implementation is null) return Results.BadRequest(new { error = ErrorMessages.UnknownNetwork(network) });

            var lang = language ?? draft.PrimaryLanguage;
            var document = await DraftRevisionService.ResolveAsync(db, draft, lang);
            if (document is null) return Results.NotFound(new { error = ErrorMessages.NoVersionInLanguage(lang) });

            var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
            var blocks = CedarToTelegramBlocksRenderer.Render(document.Value.CedarJson, mainHost).ToList();
            var parts = TelegramThreadSplitter.Split(blocks, implementation.Capabilities);

            return Results.Ok(new
            {
                parts = parts.Select((p, i) => new
                {
                    index = i,
                    startsWith = p.StartsWith,
                    characters = p.Characters,
                    mediaCount = p.MediaCount,
                    cutReason = p.CutReason,
                }),
            });
        });

        // ── The publish queue (T-090, ADR-081) ───────────────────────────────────────────────
        // Publishing stopped being an HTTP request that waits for a network to finish downloading
        // media from us (ADR-080). The request now queues the work and answers immediately; the
        // client watches the rows below.
        group.MapPost("/jobs", async (
            QueuePublishRequest req,
            ClaimsPrincipal user,
            CedarDbContext db,
            PublishJobRunner runner,
            IEnumerable<IPublishTarget> publishers,
            IConfiguration cfg) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var draft = await db.Drafts.FirstOrDefaultAsync(d => d.Id == req.DraftId && d.OwnerId == uid);
            if (draft is null) return Results.NotFound(new { error = ErrorMessages.DraftNotFound });
            if (req.TargetIds is null || req.TargetIds.Count == 0)
                return Results.BadRequest(new { error = ErrorMessages.PickADestination });

            var language = req.Language ?? draft.PrimaryLanguage;
            var targets = await db.PublishTargets
                .Where(t => req.TargetIds.Contains(t.Id) && t.OwnerId == uid && t.IsActive)
                .ToListAsync();
            if (targets.Count != req.TargetIds.Count)
                return Results.Json(new { error = ErrorMessages.DestinationNotConnected }, statusCode: StatusCodes.Status403Forbidden);

            // ADR-065's guard, unchanged in meaning and moved to where publishing now starts:
            // re-sending over a live post still requires naming the version that was previewed.
            foreach (var target in targets)
            {
                var destination = target.Network == PublishNetworks.Telegram ? target.RemoteId : target.Id.ToString();
                var kind = target.Network == PublishNetworks.Telegram
                    ? DraftRevisionService.Kinds.Telegram
                    : target.Network;
                if (!await DraftRevisionService.ConfirmationSatisfiedAsync(db, draft, language, kind, destination, req.ConfirmedFingerprint))
                {
                    var fresh = await DraftRevisionService.PreviewAsync(db, draft, language, kind, destination);
                    return Results.Json(new { error = ErrorMessages.PublishConfirmationStale, preview = fresh },
                        statusCode: StatusCodes.Status409Conflict);
                }
            }

            // T-086's check, enforced rather than merely displayed (01.08.2026). It was advisory,
            // shown in the export window — so a document 36% over Telegram's character limit with
            // ten times the media it takes was still queued, refused by the network, and reported
            // as "wrong type of the web page content", which says nothing about either. A blocking
            // issue is the network's own verdict known in advance; queuing anyway only delays it.
            var document = await DraftRevisionService.ResolveAsync(db, draft, language);
            if (document is not null)
            {
                var referenced = CedarPackage.FindReferencedMediaPaths(document.Value.CedarJson);
                var sizes = referenced.Count == 0
                    ? new Dictionary<string, long>()
                    : await db.Assets.Where(a => referenced.Contains(a.LocalPath))
                        .ToDictionaryAsync(a => a.LocalPath, a => a.SizeBytes);

                foreach (var target in targets)
                {
                    var implementation = publishers.FirstOrDefault(p => p.Network == target.Network);
                    if (implementation is null) continue;
                    // T-106 — splitting is what answers "too long" and "too much media", so those
                    // stop being blocking when the author has asked for a thread. An oversized
                    // single image still blocks: no number of messages makes it smaller.
                    var blocking = PublishValidator.Validate(document.Value.CedarJson, implementation.Capabilities, sizes)
                        .Where(i => i.Blocking)
                        .Where(i => !req.SplitIntoThread
                                    || (i.Code != PublishIssueCodes.TooLong && i.Code != PublishIssueCodes.TooManyMedia))
                        .ToList();
                    if (blocking.Count > 0)
                        return Results.Json(new { error = ErrorMessages.PublishWontFit(target.Network), issues = blocking, network = target.Network },
                            statusCode: StatusCodes.Status422UnprocessableEntity);
                }
            }

            // One job per destination — a post that reaches Telegram and fails on Bluesky is a
            // partial success, and one row per target is what lets it be reported as one — and,
            // when the author asked for a thread, one job per part on top of that (T-106).
            var jobs = new List<PublishJob>();
            foreach (var target in targets)
            {
                var partCount = 1;
                if (req.SplitIntoThread && document is not null
                    && publishers.FirstOrDefault(p => p.Network == target.Network) is { } implementation)
                {
                    var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
                    var blocks = CedarToTelegramBlocksRenderer.Render(document.Value.CedarJson, mainHost).ToList();
                    partCount = Math.Max(1, TelegramThreadSplitter.Split(blocks, implementation.Capabilities).Count);
                }

                var threadId = partCount > 1 ? Guid.NewGuid() : (Guid?)null;
                for (var i = 0; i < partCount; i++)
                {
                    jobs.Add(new PublishJob
                    {
                        OwnerId = uid,
                        DraftId = draft.Id,
                        TargetId = target.Id,
                        Network = target.Network,
                        Language = language,
                        ThreadId = threadId,
                        PartIndex = i,
                        PartCount = partCount,
                    });
                }
            }

            db.PublishJobs.AddRange(jobs);
            await db.SaveChangesAsync();

            // Saved before kicked: a runner must never look for a row that is not committed yet.
            // Only the first part of a thread is kicked — each later part is started by the one
            // before it, which is what keeps them in order.
            foreach (var job in jobs.Where(j => j.PartIndex == 0)) runner.Kick(job.Id);

            return Results.Ok(new { jobs = jobs.Select(j => new { j.Id, j.Network, j.TargetId, j.Status, j.PartIndex, j.PartCount }) });
        });

        group.MapGet("/jobs", async (Guid draftId, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            // Only this draft's recent jobs: the client polls this while a publish is in flight.
            var jobs = await db.PublishJobs
                .Where(j => j.DraftId == draftId && j.OwnerId == uid)
                .OrderByDescending(j => j.CreatedAt)
                .Take(20)
                .Select(j => new
                {
                    j.Id, j.Network, j.TargetId, j.Language, j.Status, j.Attempts, j.PartIndex, j.PartCount,
                    j.Error, j.RemoteId, j.PublicUrl, j.CreatedAt, j.FinishedAt,
                })
                .ToListAsync();
            return Results.Ok(new { jobs });
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
