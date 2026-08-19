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
    public DbSet<Series> Series => Set<Series>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();
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
    public DbSet<AssetEntry> AssetEntries => Set<AssetEntry>();
    public DbSet<EntityLink> EntityLinks => Set<EntityLink>();
    public DbSet<GameTask> GameTasks => Set<GameTask>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<Build> Builds => Set<Build>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.TelegramUserId)
            .IsUnique()
            .HasFilter("\"TelegramUserId\" IS NOT NULL");
        // ADR-108 — one local account per upstream identity. Filtered, because every ordinary
        // account has no remote id and SQLite would otherwise treat them all as duplicates.
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.RemoteUserId)
            .IsUnique()
            .HasFilter("\"RemoteUserId\" IS NOT NULL");
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

        // ADR-125 — the series page resolves by slug, and two series sharing one slug would make
        // that page a coin toss.
        builder.Entity<Series>()
            .HasIndex(s => new { s.OwnerId, s.Slug })
            .IsUnique();
        // ADR-128 — the diff-sync treats (from, to) as a set; the backlinks query walks by target.
        builder.Entity<DocumentLink>()
            .HasIndex(l => new { l.FromDraftId, l.ToDraftId })
            .IsUnique();
        builder.Entity<DocumentLink>().HasIndex(l => l.ToDraftId);
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
        // Same lesson again (T-120): EF ignores the property initialiser, so without this the
        // ADD COLUMN backfills every existing project with 0 and their first sprint would be "S0".
        builder.Entity<Project>().Property(p => p.NextSprintNumber).HasDefaultValue(1);
        // ADR-134 — the public game page resolves by slug across all owners (one blog host), so
        // uniqueness is global; filtered because null means "no public page" and SQLite would
        // otherwise still allow only distinct NULLs by accident of dialect, not by declaration.
        builder.Entity<Project>()
            .HasIndex(p => p.ShowcaseSlug)
            .IsUnique()
            .HasFilter("\"ShowcaseSlug\" IS NOT NULL");
        builder.Entity<Project>().Property(p => p.ShowcaseLinks).HasDefaultValue("");
        // T-122 — the scan's upsert key: a file is the same file if it is at the same relative path
        // in the same project. Unique, so two scans racing cannot double a row.
        builder.Entity<AssetEntry>()
            .HasIndex(a => new { a.ProjectId, a.RelativePath })
            .IsUnique();
        // The screen's two questions: "this project's files, newest first" and the kind filter over
        // them. Tens of thousands of rows per project is the expected size, not the bad case.
        builder.Entity<AssetEntry>().HasIndex(a => new { a.ProjectId, a.Kind });
        builder.Entity<AssetEntry>().Property(a => a.Kind).HasDefaultValue(AssetKinds.Other);
        // T-141 — one row per pair, whichever side created it. LinkTargets.Order is what makes the
        // unique index able to say so: without a fixed order, A→B and B→A are two different rows
        // describing the same fact.
        builder.Entity<EntityLink>()
            .HasIndex(l => new { l.FromType, l.FromId, l.ToType, l.ToId })
            .IsUnique();
        // "What is linked to this thing" is asked from both ends, so both ends are indexed.
        builder.Entity<EntityLink>().HasIndex(l => new { l.OwnerId, l.FromType, l.FromId });
        builder.Entity<EntityLink>().HasIndex(l => new { l.OwnerId, l.ToType, l.ToId });
        // T-123 — the board's only query: this project's tasks, grouped by column. Archived is in
        // the key because every screen except the archive itself filters them out first.
        builder.Entity<GameTask>().HasIndex(t => new { t.ProjectId, t.ArchivedAt, t.Status });
        // "What is due next" — the dashboard's up-next rail and the overdue count, across projects.
        builder.Entity<GameTask>().HasIndex(t => new { t.OwnerId, t.DueAt });
        // EF does not read a property initialiser when generating a migration — it wrote
        // defaultValue: "" for Draft.DocumentType and would have made every existing post
        // unpublishable (T-120, INDIEDEV.md). Declared here so the default reaches the migration.
        builder.Entity<GameTask>().Property(t => t.Status).HasDefaultValue(TaskStatuses.Backlog);
        builder.Entity<GameTask>().Property(t => t.Priority).HasDefaultValue(TaskPriorities.Normal);
        // T-124 — the planner asks for one project's sprints in date order, and nothing else.
        builder.Entity<Sprint>().HasIndex(s => new { s.ProjectId, s.StartsAt });
        // The number is what the S-chip shows, so two sprints must not share one inside a project.
        builder.Entity<Sprint>().HasIndex(s => new { s.ProjectId, s.Number }).IsUnique();
        // T-126 — the builds screen asks for one project's versions, newest first. Unique on the
        // version string: two builds called 0.4.2 in one project is a typo, not a plan.
        builder.Entity<Build>().HasIndex(b => new { b.ProjectId, b.Version }).IsUnique();
        // T-125 — a document renders with global terms plus its project's, so both are one query.
        builder.Entity<GlossaryTerm>().HasIndex(t => new { t.OwnerId, t.ProjectId, t.Language });
        // The project list query: this owner's projects, active ones first by their own order.
        builder.Entity<Project>().HasIndex(p => new { p.OwnerId, p.ArchivedAt });
        // "What is in this project" — the dashboard's only real question, and the one the drafts
        // list asks back when it shows which project a document belongs to.
        builder.Entity<Draft>().HasIndex(d => new { d.OwnerId, d.ProjectId });
    }
}
