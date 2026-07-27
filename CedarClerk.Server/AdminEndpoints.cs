using System.Security.Claims;
using CedarClerk.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// Admin panel (IF2) — see docs/admin-panel-scope.md for the scoping this follows.
//
// Every other endpoint file in this app filters by OwnerId (61 such queries at the time of
// writing). The deliberate choice here is NOT to thread an "admin bypasses the filter" flag
// through those: one missed call site would be a cross-tenant leak. Instead every cross-owner
// read lives here, behind one gate, so the security property is a single sentence — everything
// under /api/admin is admin-only, everything else stays owner-scoped.
public static class AdminEndpoints
{
    public record SetPlanRequest(string Tier, DateTime? ExpiresAt);
    public record SetLockedRequest(bool Locked);
    public record SetAdminRequest(bool IsAdmin);

    // Every mutation goes through here. Takes the actor and target so the row reads correctly
    // later without joining to anything that might have changed since.
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
                    u.Id, u.Email, u.CreatedAt, u.IsAdmin,
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

            var now = DateTime.UtcNow;
            return Results.Ok(users.Select(u => new
            {
                u.Id,
                u.Email,
                u.CreatedAt,
                u.IsAdmin,
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

            var before = $"{target.PlanTier}" + (target.PlanExpiresAt is { } e ? $" until {e:yyyy-MM-dd}" : "");
            target.PlanTier = tier;
            // Null expiry on a paid tier already means "manual grant, never expires" everywhere
            // else in the app (see ApplicationUser) — the admin grant reuses that, it doesn't
            // invent a second meaning. Free never carries an expiry.
            target.PlanExpiresAt = tier == PlanTiers.Free ? null : req.ExpiresAt;
            var after = $"{target.PlanTier}" + (target.PlanExpiresAt is { } e2 ? $" until {e2:yyyy-MM-dd}" : " (no expiry)");

            Audit(db, actor, "plan", target, $"{before} → {after}");
            await db.SaveChangesAsync();
            return Results.Ok(new { target.PlanTier, target.PlanExpiresAt });
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
            // to undo it, and the fix would be hand-editing the database on the Pi.
            if (actor.Id == id) return Results.BadRequest(new { error = "You cannot lock your own account" });

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
            if (actor.Id == id) return Results.BadRequest(new { error = "You cannot change your own admin rights" });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (target is null) return Results.NotFound();

            target.IsAdmin = req.IsAdmin;
            Audit(db, actor, req.IsAdmin ? "grant-admin" : "revoke-admin", target);
            await db.SaveChangesAsync();
            return Results.Ok(new { target.IsAdmin });
        });

        // Newest first, capped: this grows forever and nothing pages it yet.
        group.MapGet("/audit", async (CedarDbContext db) =>
            Results.Ok(await db.AdminAuditEntries
                .OrderByDescending(a => a.CreatedAt)
                .Take(Consts.Admin.AuditPageSize)
                .Select(a => new { a.Id, a.ActorEmail, a.Action, a.TargetEmail, a.Details, a.CreatedAt })
                .ToListAsync()));
    }
}
