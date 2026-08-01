using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

public class CedarDbContext(DbContextOptions<CedarDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ScheduledPost> ScheduledPosts => Set<ScheduledPost>();
    public DbSet<ChannelStatSnapshot> ChannelStatSnapshots => Set<ChannelStatSnapshot>();
    public DbSet<BlogStatSnapshot> BlogStatSnapshots => Set<BlogStatSnapshot>();
    public DbSet<ChannelPost> ChannelPosts => Set<ChannelPost>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<BotKnownChat> BotKnownChats => Set<BotKnownChat>();
    public DbSet<BotKnownChatAdmin> BotKnownChatAdmins => Set<BotKnownChatAdmin>();
    public DbSet<DraftTranslation> DraftTranslations => Set<DraftTranslation>();
    public DbSet<DraftRevision> DraftRevisions => Set<DraftRevision>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<GlossaryTerm> GlossaryTerms => Set<GlossaryTerm>();
    public DbSet<DraftStatSeen> DraftStatSeens => Set<DraftStatSeen>();
    public DbSet<FormPreset> FormPresets => Set<FormPreset>();
    public DbSet<PostInvite> PostInvites => Set<PostInvite>();
    public DbSet<PostRegistration> PostRegistrations => Set<PostRegistration>();
    public DbSet<PublishTarget> PublishTargets => Set<PublishTarget>();
    public DbSet<DraftStatSnapshot> DraftStatSnapshots => Set<DraftStatSnapshot>();
    public DbSet<DraftTargetText> DraftTargetTexts => Set<DraftTargetText>();
    public DbSet<PublishJob> PublishJobs => Set<PublishJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.TelegramUserId)
            .IsUnique()
            .HasFilter("\"TelegramUserId\" IS NOT NULL");
        builder.Entity<BotKnownChat>()
            .HasIndex(c => c.TelegramChatId)
            .IsUnique();
        builder.Entity<BotKnownChatAdmin>()
            .HasIndex(a => new { a.BotKnownChatId, a.TelegramUserId })
            .IsUnique();
        builder.Entity<DraftTranslation>()
            .HasIndex(t => new { t.DraftId, t.Language })
            .IsUnique();
        builder.Entity<DraftRevision>()
            .HasIndex(r => new { r.DraftId, r.Language, r.Kind, r.Destination, r.CreatedAt });
        builder.Entity<AiUsage>()
            .HasIndex(a => new { a.OwnerId, a.Day })
            .IsUnique();
        // T-084 — one row per (tenant, network, remote account). Connecting the same account twice
        // must be an update, not a second row: two rows would mean two credential blobs for one
        // account, and nothing could say which is current.
        builder.Entity<PublishTarget>()
            .HasIndex(t => new { t.OwnerId, t.Network, t.RemoteId })
            .IsUnique();
        // Read as "this draft's series, oldest first" and written once per draft per night —
        // the same shape as DraftRevision's index, for the same reason.
        builder.Entity<DraftStatSnapshot>()
            .HasIndex(s => new { s.DraftId, s.TakenAt });
        // T-087 — one text per draft, network and language. Keyed by network rather than by target
        // row: an author writes one Bluesky version of a post, not one per connected handle.
        builder.Entity<DraftTargetText>()
            .HasIndex(t => new { t.DraftId, t.Network, t.Language })
            .IsUnique();
        // The runner's query is "what is waiting", and the client's is "how is this draft doing".
        builder.Entity<PublishJob>().HasIndex(j => new { j.Status, j.NextAttemptAt });
        builder.Entity<PublishJob>().HasIndex(j => new { j.DraftId, j.CreatedAt });
    }
}
