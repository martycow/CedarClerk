using System.Security.Claims;
using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using CedarClerk.Localization;

namespace CedarClerk.Server;

// Admin panel (IF2) — see ADR-122 in docs/DECISIONS.md for the scoping decisions this follows.
//
// Every other endpoint file in this app filters by OwnerId (61 such queries at the time of
// writing). The deliberate choice here is NOT to thread an "admin bypasses the filter" flag
// through those: one missed call site would be a cross-tenant leak. Instead every cross-owner
// read lives here, behind one gate, so the security property is a single sentence — everything
// under /api/admin is admin-only, everything else stays owner-scoped.
public static partial class AdminEndpoints
{
    public record SetPlanRequest(string Tier, DateTime? ExpiresAt);
    public record SetLockedRequest(bool Locked);
    public record SetAdminRequest(bool IsAdmin);
    public record CreateInviteRequest(string Code, string? Label, DateTime? ExpiresAt, int? MaxUses);
    public record SetActiveRequest(bool IsActive);
    public record SetUserInviteRequest(Guid? InviteCodeId);

    // Every mutation goes through here. Takes the actor and target so the row reads correctly
    // later without joining to anything that might have changed since.
    /// <summary>
    /// A signed movement, so one control both tops up and takes back. Note is the admin's own words
    /// about why, and it goes into the audit log rather than into the ledger — the ledger records
    /// what moved, the audit records who decided and why.
    /// </summary>
    public record AdjustCreditsRequest(int Amount, string? Note);

    private static void Audit(CedarDbContext db, ApplicationUser actor, string action,
        ApplicationUser? target = null, string? details = null)
    {
        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorId = actor.Id,
            ActorEmail = actor.Email ?? "",
            Action = action,
            TargetUserId = target?.Id,
            TargetEmail = target?.Email,
            Details = details,
        });
    }

    public static void MapAdminEndpoints(this WebApplication app)
    {
        // RequireAuthorization covers "signed in"; the filter below covers "and is an admin".
        // Applied to the group rather than per-endpoint so a new route added here cannot
        // accidentally ship ungated.
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
            {
                var users = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var caller = await users.GetUserAsync(ctx.HttpContext.User);
                // 404, not 403: an admin panel that answers "wrong, but it exists" tells an
                // ordinary account something it has no business knowing.
                return caller is { IsAdmin: true } ? await next(ctx) : Results.NotFound();
            });

        // One row per account with the counts that make the list worth reading. Deliberately a
        // handful of grouped queries rather than a correlated subquery per user per metric.
        group.MapGet("/users", async (CedarDbContext db) =>
        {
            var users = await db.Users
                .OrderBy(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Id, u.Email, u.CreatedAt, u.IsAdmin, u.InviteCodeId,
                    u.PlanTier, u.PlanExpiresAt, u.TrialUsedAt,
                    u.TelegramUsername,
                    u.LockoutEnd,
                })
                .ToListAsync();

            var draftCounts = await db.Drafts.GroupBy(d => d.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.OwnerId, x => x.Count);
            var channelCounts = await db.Channels.GroupBy(c => c.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.OwnerId, x => x.Count);
            var publishedCounts = await db.Drafts.Where(d => d.IsBlogPublished).GroupBy(d => d.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.OwnerId, x => x.Count);
            // ADR-092 — a balance is the sum of the ledger, never a stored counter. Summed for
            // everyone in one query rather than per user, the same way the other counts are.
            var creditBalances = await db.CreditEntries.GroupBy(c => c.OwnerId)
                .Select(g => new { OwnerId = g.Key, Total = g.Sum(c => c.Delta) }).ToDictionaryAsync(x => x.OwnerId, x => x.Total);

            var now = DateTime.UtcNow;
            return Results.Ok(users.Select(u => new
            {
                u.Id,
                u.Email,
                u.CreatedAt,
                u.IsAdmin,
                u.InviteCodeId,
                // The stored tier and the tier that's actually in force differ once a paid plan
                // lapses — the list has to show what the user really has right now.
                PlanTier = u.PlanTier.ToString(),
                EffectiveTier = SubscriptionPlanHelper.CheckPlanExpiration(u.PlanTier, u.PlanExpiresAt, now).ToString(),
                u.PlanExpiresAt,
                TrialUsed = u.TrialUsedAt is not null,
                u.TelegramUsername,
                IsLocked = u.LockoutEnd is not null && u.LockoutEnd > now,
                Drafts = draftCounts.GetValueOrDefault(u.Id),
                Published = publishedCounts.GetValueOrDefault(u.Id),
                Channels = channelCounts.GetValueOrDefault(u.Id),
                Credits = creditBalances.GetValueOrDefault(u.Id),
            }));
        });

        // Headline numbers for the panel's landing view — all derived from data that already
        // exists, no new collection.
        group.MapGet("/summary", async (CedarDbContext db) =>
        {
            var now = DateTime.UtcNow;
            return Results.Ok(new
            {
                Users = await db.Users.CountAsync(),
                PaidUsers = await db.Users.CountAsync(u => u.PlanTier != PlanTiers.Free
                    && (u.PlanExpiresAt == null || u.PlanExpiresAt > now)),
                Drafts = await db.Drafts.CountAsync(),
                Published = await db.Drafts.CountAsync(d => d.IsBlogPublished),
                Comments = await db.Comments.CountAsync(),
                Reactions = await db.Reactions.CountAsync(),
                Channels = await db.Channels.CountAsync(),
                StorageBytes = await db.Assets.SumAsync(a => (long)a.SizeBytes),
            });
        });

        // ---------- Step 2: user management ----------
        //
        // Deliberately NOT here: deleting a user (Marty's call — it would cascade to published
        // blog posts that have public URLs) and editing other people's content.

        group.MapPost("/users/{id}/plan", async (string id, SetPlanRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            if (!Enum.TryParse<PlanTiers>(req.Tier, ignoreCase: true, out var tier))
                return Results.BadRequest(new { error = $"Unknown tier '{req.Tier}'" });

            var actor = (await users.GetUserAsync(principal))!;
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            var before = $"{target.PlanTier}" + (target.PlanExpiresAt is { } e ? $" until {DisplayTime.ToZone(e):yyyy-MM-dd}" : "");
            target.PlanTier = tier;
            // Null expiry on a paid tier already means "manual grant, never expires" everywhere
            // else in the app (see ApplicationUser) — the admin grant reuses that, it doesn't
            // invent a second meaning. Free never carries an expiry.
            target.PlanExpiresAt = tier == PlanTiers.Free ? null : req.ExpiresAt;
            var after = $"{target.PlanTier}" + (target.PlanExpiresAt is { } e2 ? $" until {DisplayTime.ToZone(e2):yyyy-MM-dd}" : " (no expiry)");

            Audit(db, actor, "plan", target, $"{before} → {after}");
            await db.SaveChangesAsync();
            return Results.Ok(new { target.PlanTier, target.PlanExpiresAt });
        });

        // Marty, 11.08.2026. Deliberately NOT refused on self, unlike lock and admin: those are
        // one-way doors out of the panel, while a balance is not a privilege — and testing an X
        // post needs credits on the account doing the testing. Every movement is logged either way.
        group.MapPost("/users/{id}/credits", async (string id, AdjustCreditsRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            if (req.Amount == 0)
                return Results.BadRequest(new { error = ErrorMessages.CreditAmountRequired });

            var actor = (await users.GetUserAsync(principal))!;
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            var before = await CreditWallet.BalanceAsync(db, target.Id);

            // A fresh reference per movement: (Reason, Ref) is unique, and each decision an admin
            // makes is its own event — granting 10 twice on purpose must produce 20, not 10.
            var adjusted = await CreditWallet.TryAdjustAsync(
                db, target.Id, req.Amount, CreditReasons.AdminGrant, Guid.NewGuid().ToString());

            if (!adjusted)
                return Results.BadRequest(new { error = ErrorMessages.CreditsWouldGoNegative(before) });

            var after = before + req.Amount;
            var note = string.IsNullOrWhiteSpace(req.Note) ? "" : $" — {req.Note.Trim()}";
            Audit(db, actor, "credits", target, $"{req.Amount:+#;-#;0} → {after} (was {before}){note}");
            await db.SaveChangesAsync();

            return Results.Ok(new { balance = after });
        });

        // Irreversible, and the only endpoint in the app that is. Deleting an account takes its
        // documents, media, channels and payment records with it — see AccountDeletion for what
        // survives and why. The audit row is written before the delete, since the account it names
        // will not be there to read afterwards.
        group.MapDelete("/users/{id}", async (string id, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CedarDbContext db, MediaPaths media,
            TenantOwnerCache.ForHosts hosts, ILogger<Program> logger) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            // Same reasoning as locking yourself out, one step further: there is no way back at all.
            if (actor.Id == id) return Results.BadRequest(new { error = ErrorMessages.CannotDeleteOwnAccount });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            var counts = new
            {
                drafts = await db.Drafts.CountAsync(x => x.OwnerId == id),
                assets = await db.Assets.CountAsync(x => x.OwnerId == id),
                channels = await db.Channels.CountAsync(x => x.OwnerId == id),
            };

            Audit(db, actor, "delete-account", target,
                $"{counts.drafts} drafts, {counts.assets} files, {counts.channels} channels");
            await db.SaveChangesAsync();

            await AccountDeletion.DeleteAsync(db, id, media.Dir, hosts);
            logger.LogWarning("Account {Email} deleted by {Actor} — {Drafts} drafts, {Assets} files",
                target.Email, actor.Email, counts.drafts, counts.assets);

            return Results.Ok(new { deleted = true, counts.drafts, counts.assets, counts.channels });
        });

        // Rows whose parent is gone (see OrphanSweep). Counting and removing are separate calls so
        // the number can be looked at before anything is deleted.
        group.MapGet("/maintenance/orphans", async (CedarDbContext db) =>
            Results.Ok(await OrphanSweep.CountAsync(db)));

        group.MapPost("/maintenance/orphans/purge", async (ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CedarDbContext db, ILogger<Program> logger) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            var removed = await OrphanSweep.RemoveAsync(db);
            var total = removed.Values.Sum();

            if (total > 0)
            {
                Audit(db, actor, "purge-orphans", details: string.Join(", ",
                    removed.Where(r => r.Value > 0).Select(r => $"{r.Key} {r.Value}")));
                await db.SaveChangesAsync();
                logger.LogWarning("Purged {Total} orphaned rows on behalf of {Actor}", total, actor.Email);
            }

            return Results.Ok(new { removed, total });
        });

        group.MapPost("/users/{id}/reset-trial", async (string id,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            target.TrialUsedAt = null;
            Audit(db, actor, "reset-trial", target);
            await db.SaveChangesAsync();
            return Results.Ok(new { trialUsed = false });
        });

        group.MapPost("/users/{id}/lock", async (string id, SetLockedRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            // Locking yourself out is not a decision worth honouring — there is no second admin
            // to undo it, and the fix would be hand-editing the database on the server.
            if (actor.Id == id) return Results.BadRequest(new { error = ErrorMessages.CannotLockOwnAccount });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            // Identity's own lockout, so the normal sign-in path enforces it — nothing custom to
            // get wrong. Far future rather than "forever" because the column is a DateTime.
            target.LockoutEnabled = true;
            target.LockoutEnd = req.Locked ? DateTimeOffset.UtcNow.AddYears(100) : null;
            Audit(db, actor, req.Locked ? "lock" : "unlock", target);
            await db.SaveChangesAsync();
            return Results.Ok(new { locked = req.Locked });
        });

        group.MapPost("/users/{id}/admin", async (string id, SetAdminRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            // Same reasoning as lock: demoting yourself is a one-way door out of the panel.
            if (actor.Id == id) return Results.BadRequest(new { error = ErrorMessages.CannotChangeOwnAdmin });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            target.IsAdmin = req.IsAdmin;
            Audit(db, actor, req.IsAdmin ? "grant-admin" : "revoke-admin", target);
            await db.SaveChangesAsync();
            return Results.Ok(new { target.IsAdmin });
        });

        // ---------- Step 3: invite codes ----------

        group.MapGet("/invites", async (CedarDbContext db) =>
        {
            var codes = await db.InviteCodes.OrderByDescending(c => c.CreatedAt).ToListAsync();
            // Who actually came in on each code. Counted from the users table rather than trusting
            // InviteCode.Uses — that counter can only ever drift, this can't.
            var joined = await db.Users.Where(u => u.InviteCodeId != null)
                .GroupBy(u => u.InviteCodeId!.Value)
                .Select(g => new { CodeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CodeId, x => x.Count);

            var now = DateTime.UtcNow;
            return Results.Ok(codes.Select(c => new
            {
                c.Id, c.Code, c.Label, c.IsActive, c.ExpiresAt, c.MaxUses, c.Uses, c.CreatedAt,
                Joined = joined.GetValueOrDefault(c.Id),
                // Same shared predicate registration uses, so the panel can never claim a code
                // is usable when registration would refuse it (or the reverse).
                IsUsable = InviteCodeRules.IsUsable(c.IsActive, c.ExpiresAt, c.MaxUses, c.Uses, now),
            }));
        });

        group.MapPost("/invites", async (CreateInviteRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var value = req.Code?.Trim() ?? "";
            if (value.Length < Consts.Admin.MinInviteCodeLength)
                return Results.BadRequest(new { error = $"Code must be at least {Consts.Admin.MinInviteCodeLength} characters" });
            if (await db.InviteCodes.AnyAsync(c => c.Code.ToLower() == value.ToLower()))
                return Results.BadRequest(new { error = ErrorMessages.InviteCodeExists });

            var actor = (await users.GetUserAsync(principal))!;
            var code = new InviteCode
            {
                Code = value,
                Label = req.Label?.Trim() ?? "",
                ExpiresAt = req.ExpiresAt,
                MaxUses = req.MaxUses is > 0 ? req.MaxUses : null,
            };
            db.InviteCodes.Add(code);
            Audit(db, actor, "invite-create", details: $"{value} ({code.Label})");
            await db.SaveChangesAsync();
            return Results.Ok(new { code.Id, code.Code });
        });

        // Deactivate rather than delete: users point at this row, and removing it would silently
        // erase the attribution of everyone who joined through it.
        group.MapPost("/invites/{id:guid}/active", async (Guid id, SetActiveRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            var code = await db.InviteCodes.FirstOrDefaultAsync(c => c.Id == id);
            if (code is null) return Results.NotFound();

            code.IsActive = req.IsActive;
            Audit(db, actor, req.IsActive ? "invite-enable" : "invite-disable", details: code.Code);
            await db.SaveChangesAsync();
            return Results.Ok(new { code.IsActive });
        });

        // Manual attribution (Marty's answer 4). Accounts that predate invite tracking, or came in
        // through the config fallback, have no recoverable code — this is the one-off fix so they
        // don't read "unknown" forever. Null clears it back to unknown.
        group.MapPost("/users/{id}/invite", async (string id, SetUserInviteRequest req,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, CedarDbContext db) =>
        {
            var actor = (await users.GetUserAsync(principal))!;
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            string detail = "cleared";
            if (req.InviteCodeId is { } codeId)
            {
                var code = await db.InviteCodes.FirstOrDefaultAsync(c => c.Id == codeId);
                if (code is null) return Results.BadRequest(new { error = ErrorMessages.NoSuchInviteCode });
                detail = code.Code;
            }

            target.InviteCodeId = req.InviteCodeId;
            // Flagged as manual in the log: this is an admin's assertion about history, not
            // something the system observed, and a year from now that distinction matters.
            Audit(db, actor, "invite-attribute", target, $"{detail} (set by hand)");
            await db.SaveChangesAsync();
            return Results.Ok(new { target.InviteCodeId });
        });

        // ---------- Step 4: every post, across owners ----------
        //
        // READ-ONLY by decision: the panel links out to the live post rather than editing anyone
        // else's content. Nothing here writes.
        group.MapGet("/posts", async (CedarDbContext db, IConfiguration cfg) =>
        {
            var owners = await db.Users.Select(u => new { u.Id, u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Email);

            var posts = await db.Drafts
                .OrderByDescending(d => d.UpdatedAt)
                .Take(Consts.Admin.PostPageSize)
                .Select(d => new
                {
                    d.Id, d.Title, d.OwnerId, d.UpdatedAt, d.BlogSlug, d.IsBlogPublished,
                    d.IsPrivate, d.IsArchived, d.ViewCount,
                    d.LastTelegramUsername, d.LastTelegramMessageId,
                })
                .ToListAsync();

            var draftIds = posts.Select(p => p.Id).ToList();
            var commentCounts = await db.Comments.Where(c => draftIds.Contains(c.DraftId))
                .GroupBy(c => c.DraftId).Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);

            // This list spans owners and slugs are per-owner, so the host is per row — resolved in
            // one batch, in keeping with the grouped-query rule the /users list follows.
            var blogHosts = await BlogTenant.HostsForOwnersAsync(db, cfg, posts.Select(p => p.OwnerId));

            return Results.Ok(posts.Select(p => new
            {
                p.Id,
                p.Title,
                OwnerEmail = owners.GetValueOrDefault(p.OwnerId),
                p.UpdatedAt,
                p.IsBlogPublished,
                p.IsPrivate,
                p.IsArchived,
                p.ViewCount,
                Comments = commentCounts.GetValueOrDefault(p.Id),
                BlogUrl = p.IsBlogPublished && p.BlogSlug != null && blogHosts.TryGetValue(p.OwnerId, out var blogHost)
                    ? $"https://{blogHost}/{p.BlogSlug}"
                    : null,
                TelegramUrl = p.LastTelegramUsername != null && p.LastTelegramMessageId != null
                    ? $"https://t.me/{p.LastTelegramUsername}/{p.LastTelegramMessageId}"
                    : null,
            }));
        });

        // ---------- Step 5: reporting on data that already exists ----------

        group.MapGet("/billing", async (CedarDbContext db) =>
        {
            var owners = await db.Users.Select(u => new { u.Id, u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Email);

            var payments = await db.Payments
                .OrderByDescending(p => p.CreatedAt)
                .Take(Consts.Admin.PaymentPageSize)
                .ToListAsync();

            return Results.Ok(new
            {
                Payments = payments.Select(p => new
                {
                    p.Id, p.Provider, p.Plan, p.Amount, p.Currency, p.Status, p.CreatedAt,
                    OwnerEmail = owners.GetValueOrDefault(p.OwnerId),
                }),
                // Only completed payments count toward a revenue figure — a failed or pending row
                // is not money.
                TotalByCurrency = payments
                    .Where(p => p.Status == "completed")
                    .GroupBy(p => p.Currency)
                    .Select(g => new { Currency = g.Key, Total = g.Sum(p => p.Amount) }),
            });
        });

        // Per-user resource use — storage and today's AI calls. Both already collected; this is
        // reporting, not new measurement.
        group.MapGet("/usage", async (CedarDbContext db) =>
        {
            var owners = await db.Users.Select(u => new { u.Id, u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Email);

            var storage = await db.Assets.GroupBy(a => a.OwnerId)
                .Select(g => new { OwnerId = g.Key, Bytes = g.Sum(a => (long)a.SizeBytes), Files = g.Count() })
                .ToListAsync();

            var today = DateTime.UtcNow.Date;
            var aiToday = await db.AiUsages.Where(a => a.Day == today)
                .ToDictionaryAsync(a => a.OwnerId, a => a.Count);

            return Results.Ok(storage.Select(s => new
            {
                OwnerEmail = owners.GetValueOrDefault(s.OwnerId),
                s.Bytes,
                s.Files,
                AiToday = aiToday.GetValueOrDefault(s.OwnerId),
            }).OrderByDescending(x => x.Bytes));
        });

        // Newest first, paged. The log grows forever by design - a log that starts halfway
        // through is missing exactly what someone would look for - so the panel reads it a page
        // at a time instead of only ever showing the newest AuditPageSize entries and silently
        // hiding the rest. `hasMore` rather than a total: counting the whole table on every page
        // request buys nothing the "Load more" button needs.
        group.MapGet("/audit", async (int? skip, CedarDbContext db) =>
        {
            var offset = Math.Max(0, skip ?? 0);
            var page = await db.AdminAuditEntries
                .OrderByDescending(a => a.CreatedAt)
                .Skip(offset)
                .Take(Consts.Admin.AuditPageSize + 1)
                .Select(a => new { a.Id, a.ActorEmail, a.Action, a.TargetEmail, a.Details, a.CreatedAt })
                .ToListAsync();

            var hasMore = page.Count > Consts.Admin.AuditPageSize;
            return Results.Ok(new
            {
                Entries = hasMore ? page.Take(Consts.Admin.AuditPageSize) : page,
                HasMore = hasMore,
            });
        });

        // T-191 (ADR-232) — the feedback inbox. Under the platform-scoped admin context, so it
        // reads every account's entries; the reporter's email rides along for a reply.
        group.MapGet("/feedback", async (bool? handled, CedarDbContext db) =>
        {
            var query = db.FeedbackEntries.AsQueryable();
            if (handled is false) query = query.Where(f => f.HandledAt == null);
            var entries = await query
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new { f.Id, f.Kind, f.Message, f.Path, f.HandledAt, f.CreatedAt, Email = f.Owner!.Email })
                .Take(200)
                .ToListAsync();
            return Results.Ok(entries);
        });

        group.MapPost("/feedback/{id:guid}/handled", async (Guid id, HandledRequest req, CedarDbContext db) =>
        {
            var entry = await db.FeedbackEntries.FirstOrDefaultAsync(f => f.Id == id);
            if (entry is null) return Results.NotFound();
            entry.HandledAt = req.Handled ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
            return Results.Ok(new { entry.HandledAt });
        });

        // AdminEndpoints.Landing.cs — inside this group, so the gate above covers it too (ADR-215).
        MapLandingAdmin(group);
        MapDiscoveryAdmin(group);
    }

    public record HandledRequest(bool Handled);
}
