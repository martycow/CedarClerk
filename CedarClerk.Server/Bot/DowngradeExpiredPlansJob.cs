using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace CedarClerk.Server.Bot;

/// <summary>
/// Persists what Plans.Effective already enforces at request time: users whose paid period has
/// lapsed are moved to Free. Runs hourly; the 2-day grace in Plans.NextExpiry means auto-renewing
/// users (Stripe/Stars) never get here unless renewal actually failed or was cancelled.
/// </summary>
[DisallowConcurrentExecution]
public class DowngradeExpiredPlansJob(CedarDbContext db, ILogger<DowngradeExpiredPlansJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = DateTime.UtcNow;
        var expired = await db.Users
            .Where(u => (u.PlanTier == PlanTiers.Pro || u.PlanTier == PlanTiers.ProPlus)
                        && u.PlanExpiresAt != null && u.PlanExpiresAt < now)
            .ToListAsync();

        foreach (var user in expired)
        {
            logger.LogInformation("Plan lapsed — user {UserId} downgraded from {Tier} (expired {ExpiresAt})",
                user.Id, user.PlanTier, user.PlanExpiresAt);
            user.PlanTier = PlanTiers.Free;
            user.PlanExpiresAt = null;
        }

        if (expired.Count > 0)
            await db.SaveChangesAsync();
    }
}
