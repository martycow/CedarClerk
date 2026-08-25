using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Rows whose parent is gone, left behind by the incomplete delete path <see cref="DraftDeletion"/>
/// replaces. They are unreachable through the app — nothing can render a comment on a post that
/// does not exist — but they still hold text, and they are still in every backup generation.
///
/// A count and a removal, separately, so the number can be looked at before anything is deleted.
/// </summary>
public static class OrphanSweep
{
    public static async Task<Dictionary<string, int>> CountAsync(CedarDbContext db) => new()
    {
        ["Reactions"] = await db.Reactions.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["Comments"] = await db.Comments.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["PollVotes"] = await db.PollVotes.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["PostInvites"] = await db.PostInvites.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["PostRegistrations"] = await db.PostRegistrations.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["DraftTranslations"] = await db.DraftTranslations.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["DraftRevisions"] = await db.DraftRevisions.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["DraftStatSnapshots"] = await db.DraftStatSnapshots.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["DraftTargetTexts"] = await db.DraftTargetTexts.CountAsync(x => !db.Drafts.Any(d => d.Id == x.DraftId)),
        ["ChannelPosts"] = await db.ChannelPosts.CountAsync(x => !db.Channels.Any(c => c.Id == x.ChannelId)),
        ["ChannelStatSnapshots"] = await db.ChannelStatSnapshots.CountAsync(x => !db.Channels.Any(c => c.Id == x.ChannelId)),
    };

    public static async Task<Dictionary<string, int>> RemoveAsync(CedarDbContext db) => new()
    {
        ["Reactions"] = await db.Reactions.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["Comments"] = await db.Comments.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["PollVotes"] = await db.PollVotes.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["PostInvites"] = await db.PostInvites.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["PostRegistrations"] = await db.PostRegistrations.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["DraftTranslations"] = await db.DraftTranslations.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["DraftRevisions"] = await db.DraftRevisions.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["DraftStatSnapshots"] = await db.DraftStatSnapshots.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["DraftTargetTexts"] = await db.DraftTargetTexts.Where(x => !db.Drafts.Any(d => d.Id == x.DraftId)).ExecuteDeleteAsync(),
        ["ChannelPosts"] = await db.ChannelPosts.Where(x => !db.Channels.Any(c => c.Id == x.ChannelId)).ExecuteDeleteAsync(),
        ["ChannelStatSnapshots"] = await db.ChannelStatSnapshots.Where(x => !db.Channels.Any(c => c.Id == x.ChannelId)).ExecuteDeleteAsync(),
    };
}
