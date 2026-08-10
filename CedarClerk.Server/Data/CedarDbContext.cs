using CedarClerk.Core;
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
    public DbSet<BlogViewGeoDaily> BlogViewGeoDailies => Set<BlogViewGeoDaily>();
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
    public DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();

    // Indie-gamedev module (Phase 13, ADR-101) — same context on purpose, see Entities.IndieDev.cs.
    public DbSet<Project> Projects => Set<Project>();

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
        // The upsert key: a view either finds today's (country, language) bucket or creates it.
        builder.Entity<BlogViewGeoDaily>()
            .HasIndex(v => new { v.OwnerId, v.Day, v.Country, v.Language })
            .IsUnique();
        // The runner's query is "what is waiting", and the client's is "how is this draft doing".
        builder.Entity<PublishJob>().HasIndex(j => new { j.Status, j.NextAttemptAt });
        builder.Entity<PublishJob>().HasIndex(j => new { j.DraftId, j.CreatedAt });
        // ADR-092 — the balance query, and the idempotency anchor (SQLite treats NULLs as distinct
        // in a unique index, so unanchored rows are unconstrained).
        builder.Entity<CreditEntry>().HasIndex(c => new { c.OwnerId, c.CreatedAt });
        builder.Entity<CreditEntry>()
            .HasIndex(c => new { c.Reason, c.Ref })
            .IsUnique();
        // ADR-102 — the database default has to be stated here, not left to the C# initialiser.
        // EF does not read a property initialiser when generating a migration, so without this the
        // ADD COLUMN backfills every existing draft with "" instead of "post" — and an empty type is
        // neither known nor publishable, which would have made every already-written post refuse to
        // publish on the first deploy. Declared on the model rather than hand-edited into the
        // migration so that regenerating the migration cannot quietly lose it.
        builder.Entity<Draft>().Property(d => d.DocumentType).HasDefaultValue(DocumentTypes.Post);
        // Same reason as the line above — EF ignores the property initialiser, and a project with an
        // empty type would fall through StarterDocumentType's unknown branch.
        builder.Entity<Project>().Property(p => p.ProjectType).HasDefaultValue(ProjectTypes.FullGame);
        // The project list query: this owner's projects, active ones first by their own order.
        builder.Entity<Project>().HasIndex(p => new { p.OwnerId, p.ArchivedAt });
        // "What is in this project" — the dashboard's only real question, and the one the drafts
        // list asks back when it shows which project a document belongs to.
        builder.Entity<Draft>().HasIndex(d => new { d.OwnerId, d.ProjectId });
    }
}
