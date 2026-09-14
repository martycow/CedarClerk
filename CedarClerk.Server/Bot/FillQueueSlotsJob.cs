using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;
using Quartz;
using CedarClerk.Server.Tenancy;

namespace CedarClerk.Server;

/// <summary>
/// Wave 2 item 10 — keeps every active queue slot's upcoming occurrences filled from the owner's
/// evergreen pool, as ordinary ScheduledPost rows. FIRING IS UNCHANGED: this job only writes
/// Pending rows; <see cref="PublishDueScheduledPostsJob"/> remains the only sender. An occurrence
/// with no eligible draft stays unfilled — the calendar shows it as an empty (resin) slot ticket.
/// </summary>
[DisallowConcurrentExecution]
public class FillQueueSlotsJob(CedarDbContext db, TenantProvider tenant, ILogger<FillQueueSlotsJob> logger) : IJob
{
    /// <summary>How far ahead an occurrence must be before it is worth filling — a slot two hours
    /// away is close enough that a hand-scheduled post should win it, not the pool.</summary>
    public static readonly TimeSpan MinLead = TimeSpan.FromHours(2);

    /// <summary>How far ahead occurrences are materialised.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(7);

    public async Task Execute(IJobExecutionContext context)
    {
        // Runs on a timer, not in a request: there is no Host and no signed-in user, and the
        // work is over every owner's rows at once.
        tenant.UsePlatform();

        var filled = await FillAsync(db, DateTime.UtcNow);
        if (filled > 0)
            logger.LogInformation("Queue slots filled {Count} occurrence(s)", filled);
    }

    /// <summary>Every occurrence of one slot inside (from, to], oldest first.</summary>
    public static List<DateTime> Occurrences(QueueSlot slot, DateTime fromUtc, DateTime toUtc)
    {
        var occurrences = new List<DateTime>();
        for (var day = fromUtc.Date; day <= toUtc.Date; day = day.AddDays(1))
        {
            if ((int)day.DayOfWeek != slot.DayOfWeek) continue;
            var occurrence = day.AddMinutes(slot.TimeUtcMinutes);
            if (occurrence > fromUtc && occurrence <= toUtc)
                occurrences.Add(occurrence);
        }
        return occurrences;
    }

    /// <summary>
    /// The whole fill pass over a platform-scoped context, separated from the Quartz plumbing so a
    /// test can drive it with a fabricated clock. Returns how many ScheduledPost rows were created.
    /// </summary>
    public static async Task<int> FillAsync(CedarDbContext db, DateTime nowUtc)
    {
        var slots = await db.QueueSlots.Where(s => s.IsActive).ToListAsync();
        if (slots.Count == 0) return 0;

        var targetIds = slots.Select(s => s.TargetId).Distinct().ToList();
        var targets = await db.PublishTargets
            .Where(t => targetIds.Contains(t.Id) && t.IsActive)
            .ToDictionaryAsync(t => t.Id);

        var slotIds = slots.Select(s => s.Id).ToList();
        // Occupancy is per ISO week, not per exact timestamp: dragging a slot-filled ticket on the
        // calendar keeps its SlotId but moves ScheduledAtUtc, and an exact match would read the
        // vacated timestamp as an open occurrence and fill it a second time. Failed rows do not
        // occupy — a failed slot send leaves the week open for another pick.
        var taken = (await db.ScheduledPosts
                .Where(p => p.SlotId != null && slotIds.Contains(p.SlotId.Value) && p.Status != "Failed")
                .Select(p => new { p.SlotId, p.ScheduledAtUtc })
                .ToListAsync())
            .Select(p => (p.SlotId!.Value, IsoWeekOf(p.ScheduledAtUtc)))
            .ToHashSet();

        // A draft already waiting to go out to a target must not be queued for it a second time.
        var pending = (await db.ScheduledPosts
                .Where(p => p.Status == "Pending" && p.TargetId != null)
                .Select(p => new { p.DraftId, p.TargetId })
                .ToListAsync())
            .Select(p => (p.DraftId, p.TargetId!.Value))
            .ToHashSet();

        var owners = slots.Select(s => s.OwnerId).Distinct().ToList();
        // IsPublishable is a C# switch, so the last filter runs in memory over an already-small set.
        var pool = (await db.Drafts
                .Where(d => d.IsEvergreen && !d.IsTemplate && owners.Contains(d.OwnerId))
                .ToListAsync())
            .Where(d => DocumentTypes.IsPublishable(d.DocumentType))
            .ToList();

        var from = nowUtc + MinLead;
        var to = nowUtc + Horizon;
        var created = 0;

        foreach (var slot in slots)
        {
            if (!targets.TryGetValue(slot.TargetId, out var target) || target.OwnerId != slot.OwnerId)
                continue;
            // ADR-299 — an evergreen slot on LinkedIn would be exactly the automated posting its terms forbid.
            if (target.Network == PublishNetworks.LinkedIn) continue;

            foreach (var occurrence in Occurrences(slot, from, to))
            {
                if (taken.Contains((slot.Id, IsoWeekOf(occurrence)))) continue;

                var draft = pool
                    .Where(d => d.OwnerId == slot.OwnerId
                        && (slot.Category == "" || d.EvergreenCategory == slot.Category)
                        && (d.EvergreenMaxSends is not { } max || d.EvergreenSendCount < max)
                        && (d.EvergreenUntil is not { } until || until > occurrence)
                        && !pending.Contains((d.Id, target.Id)))
                    .OrderBy(d => d.EvergreenSendCount)
                    .ThenBy(d => d.Id)
                    .FirstOrDefault();
                if (draft is null) continue; // empty pool — the occurrence stays an open slot

                db.ScheduledPosts.Add(new ScheduledPost
                {
                    DraftId = draft.Id,
                    TargetId = target.Id,
                    Network = target.Network,
                    ChatId = target.Network == PublishNetworks.Telegram ? target.RemoteId : "",
                    ScheduledAtUtc = occurrence,
                    OwnerId = slot.OwnerId,
                    Language = draft.PrimaryLanguage,
                    SlotId = slot.Id,
                });
                taken.Add((slot.Id, IsoWeekOf(occurrence)));
                pending.Add((draft.Id, target.Id));
                created++;
            }
        }

        if (created > 0)
            await db.SaveChangesAsync();
        return created;
    }

    /// <summary>A weekly slot has one occurrence per ISO week — this pair is its identity.</summary>
    public static (int Year, int Week) IsoWeekOf(DateTime utc) =>
        (System.Globalization.ISOWeek.GetYear(utc), System.Globalization.ISOWeek.GetWeekOfYear(utc));
}
