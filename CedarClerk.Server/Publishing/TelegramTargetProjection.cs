using CedarClerk.Core;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Publishing;

/// <summary>
/// Keeps the general <see cref="PublishTarget"/> row that stands for a Telegram channel in step
/// with its <see cref="Channel"/> row (T-085, ADR-078).
///
/// A projection rather than a replacement, and the reason is in the schema: `ChannelPost`,
/// `ChannelStatSnapshot` and `BotKnownChat` all key off `Channel`, and none of the three
/// generalises — member counts and per-channel post logs are Telegram concepts. So `Channel`
/// stays the Telegram detail table and `PublishTarget` is the general row in front of it.
///
/// Nothing here calls SaveChanges: every caller is already inside a unit of work that has more to
/// write, and splitting the save would let a channel exist without its target after a failure.
/// </summary>
public static class TelegramTargetProjection
{
    /// <summary>The network's own identity for a channel. Invariant culture — this is a key, not a display value.</summary>
    public static string RemoteIdOf(Channel channel) =>
        channel.TelegramChatId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Insert-or-update, keyed the way the unique index is. Reconnecting a channel that was
    /// disconnected before reactivates the original row rather than adding a second one — which is
    /// what the index requires, and also what keeps the old row's history attached.
    /// </summary>
    public static async Task<PublishTarget> EnsureAsync(CedarDbContext db, Channel channel, CancellationToken ct = default)
    {
        var remoteId = RemoteIdOf(channel);
        var existing = await db.PublishTargets.FirstOrDefaultAsync(
            t => t.OwnerId == channel.OwnerId && t.Network == PublishNetworks.Telegram && t.RemoteId == remoteId, ct);

        if (existing is not null)
        {
            existing.DisplayName = channel.Title;
            existing.ChannelId = channel.Id;
            existing.IsActive = true;
            existing.LastError = null;
            return existing;
        }

        var target = new PublishTarget
        {
            OwnerId = channel.OwnerId,
            Network = PublishNetworks.Telegram,
            DisplayName = channel.Title,
            RemoteId = remoteId,
            // Null on purpose: Telegram posts through Cedar Clerk's own bot, so there is nothing
            // of the tenant's to store. Bluesky is the first network where this is not null.
            CredentialsProtected = null,
            ChannelId = channel.Id,
        };
        db.PublishTargets.Add(target);
        return target;
    }

    /// <summary>
    /// Disconnecting a channel deactivates its target instead of deleting it — the row is the only
    /// remaining answer to "where did this post go" once the channel is gone, and `LastPublishedAt`
    /// is on it. Same deactivate-don't-delete choice `InviteCode` made.
    /// </summary>
    public static async Task DeactivateAsync(CedarDbContext db, Channel channel, CancellationToken ct = default)
    {
        var remoteId = RemoteIdOf(channel);
        var existing = await db.PublishTargets.FirstOrDefaultAsync(
            t => t.OwnerId == channel.OwnerId && t.Network == PublishNetworks.Telegram && t.RemoteId == remoteId, ct);

        if (existing is null) return;
        existing.IsActive = false;
        existing.ChannelId = null;
    }

    /// <summary>
    /// Gives every channel that predates this table its target row. Runs at startup and is
    /// idempotent, which is why it is C# with a test rather than SQL inside the migration: a
    /// one-shot data migration that runs on a production database deserves to be runnable twice
    /// and provable in a test, and neither is true of a hand-written INSERT…SELECT.
    /// </summary>
    public static async Task<int> BackfillAsync(CedarDbContext db, CancellationToken ct = default)
    {
        var channels = await db.Channels.ToListAsync(ct);
        if (channels.Count == 0) return 0;

        // Counted by entity state, not by how many rows the change tracker holds: EnsureAsync
        // *loads* an existing row, which makes Local.Count grow whether or not anything was added.
        // In a test that reuses one context the two are indistinguishable, which is exactly why the
        // first version of this passed its "safe to run again" test and still logged "projected 2"
        // on every production start.
        var added = 0;
        foreach (var channel in channels)
        {
            var target = await EnsureAsync(db, channel, ct);
            if (db.Entry(target).State == EntityState.Added) added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }
}
