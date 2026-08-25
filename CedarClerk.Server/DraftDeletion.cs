using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

/// <summary>
/// Everything that has to go when a draft does.
///
/// Extracted because the delete endpoint cleared five of the thirteen tables that point at a
/// draft, and the other eight accumulated rows nobody could reach: production carries 16 of them.
/// One list, so the next child table added is added here rather than forgotten in one of the two
/// places that delete a document.
///
/// The draft row itself is left to the caller — the endpoint has an ordering constraint of its own
/// (it reads ProjectId before deleting) that does not belong in here.
/// </summary>
public static class DraftDeletion
{
    public static async Task CascadeAsync(CedarDbContext db, string ownerId, Guid draftId)
    {
        await db.DraftGlossaryExclusions.Where(x => x.DraftId == draftId && x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftStatSeens.Where(x => x.DraftId == draftId && x.OwnerId == ownerId).ExecuteDeleteAsync();
        await db.DraftRevisions.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.DraftTranslations.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.DraftStatSnapshots.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.DraftTargetTexts.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.Reactions.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.Comments.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.PollVotes.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.PostInvites.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.PostRegistrations.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        // The send log: a row naming a draft that no longer exists cannot answer the one question
        // it is for, which is where a post went.
        await db.ChannelPosts.Where(x => x.DraftId == draftId).ExecuteDeleteAsync();
        await db.DocumentLinks.Where(x => x.FromDraftId == draftId || x.ToDraftId == draftId).ExecuteDeleteAsync();
    }
}

/// <summary>The same, for a channel.</summary>
public static class ChannelDeletion
{
    public static async Task CascadeAsync(CedarDbContext db, string ownerId, Guid channelId)
    {
        await db.ChannelPosts.Where(x => x.ChannelId == channelId).ExecuteDeleteAsync();
        await db.ChannelStatSnapshots.Where(x => x.ChannelId == channelId).ExecuteDeleteAsync();
    }
}
