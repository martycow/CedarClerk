using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CedarClerk.Server;

public class CedarDbContext(DbContextOptions<CedarDbContext> options, TenantProvider tenant)
    : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>Read by <see cref="TenantModelCacheKeyFactory"/> while the model is compiled.</summary>
    public bool IsPlatformModel => tenant.IsPlatform;

    /// <summary>
    /// The value every owner filter compares against. A property rather than a captured constant:
    /// EF turns it into a query parameter and re-reads it per query, so a tenant resolved after the
    /// context was created (an ordinary request, whose owner is only known after authentication)
    /// still filters correctly.
    /// </summary>
    public string? TenantId => tenant.TenantId;

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
    public DbSet<DraftGlossaryExclusion> DraftGlossaryExclusions => Set<DraftGlossaryExclusion>();
    public DbSet<GlossaryTermUsage> GlossaryTermUsages => Set<GlossaryTermUsage>();
    public DbSet<DraftStatSeen> DraftStatSeens => Set<DraftStatSeen>();
    public DbSet<FormPreset> FormPresets => Set<FormPreset>();
    public DbSet<PostInvite> PostInvites => Set<PostInvite>();
    public DbSet<PostRegistration> PostRegistrations => Set<PostRegistration>();
    public DbSet<PublishTarget> PublishTargets => Set<PublishTarget>();
    public DbSet<DraftStatSnapshot> DraftStatSnapshots => Set<DraftStatSnapshot>();
    public DbSet<DraftTargetText> DraftTargetTexts => Set<DraftTargetText>();
    public DbSet<PublishJob> PublishJobs => Set<PublishJob>();
    public DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<FeedbackEntry> FeedbackEntries => Set<FeedbackEntry>();
    public DbSet<Preset> Presets => Set<Preset>();
    public DbSet<LandingSettings> LandingSettings => Set<LandingSettings>();
    public DbSet<BlogSubscriber> BlogSubscribers => Set<BlogSubscriber>();
    public DbSet<BlogNotifyJob> BlogNotifyJobs => Set<BlogNotifyJob>();
    public DbSet<QueueSlot> QueueSlots => Set<QueueSlot>();
    public DbSet<ChannelInviteLink> ChannelInviteLinks => Set<ChannelInviteLink>();
    public DbSet<ChannelMemberDaily> ChannelMemberDailies => Set<ChannelMemberDaily>();
    public DbSet<TrackedLink> TrackedLinks => Set<TrackedLink>();
    public DbSet<TrackedLinkClickDaily> TrackedLinkClickDailies => Set<TrackedLinkClickDaily>();

    // Indie-gamedev module (Phase 13, ADR-101) — same context on purpose, see Entities.IndieDev.cs.
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AssetEntry> AssetEntries => Set<AssetEntry>();
    public DbSet<EntityLink> EntityLinks => Set<EntityLink>();
    public DbSet<GameTask> GameTasks => Set<GameTask>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<Build> Builds => Set<Build>();
    public DbSet<ShowcaseStatDaily> ShowcaseStatDailies => Set<ShowcaseStatDaily>();
    public DbSet<ShowcaseFollower> ShowcaseFollowers => Set<ShowcaseFollower>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<CanvasBoard> CanvasBoards => Set<CanvasBoard>();
    public DbSet<CanvasItem> CanvasItems => Set<CanvasItem>();
    public DbSet<DialogueScript> DialogueScripts => Set<DialogueScript>();
    public DbSet<DialogueLineTranslation> DialogueLineTranslations => Set<DialogueLineTranslation>();

    // Here rather than at the AddDbContext call, so that no way of building this context can miss
    // it. Without the factory, EF caches one model per context type and the first one compiled —
    // tenant or platform — is silently handed to every later context of the other kind.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);
        options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwners();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampOwners();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>
    /// Gives every new owned row the tenant it was created under, and refuses to write one that
    /// would belong to nobody.
    ///
    /// The refusal is the point. A row saved with an empty OwnerId matches no tenant, so it would
    /// be written successfully and then be invisible to the person who just created it — a bug
    /// that looks like data loss and cannot be found by reading the endpoint that caused it. The
    /// seven places whose owner does not come from the request (the stats job, the public blog's
    /// reader rows, a channel post sent by the queue) set it themselves and never reach the throw.
    /// </summary>
    private void StampOwners()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added) continue;

            var owner = entry.Metadata.FindProperty("OwnerId");
            if (owner is null || owner.ClrType != typeof(string)) continue;

            var property = entry.Property("OwnerId");
            if (property.CurrentValue is string { Length: > 0 }) continue;

            if (TenantId is null)
                throw new InvalidOperationException(
                    $"{entry.Metadata.DisplayName()} was added with no OwnerId, in a scope that has no tenant to take one from. Set it explicitly.");

            property.CurrentValue = TenantId;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ApplyTenantFilters(builder);
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
        // The tenant lookup on every subdomain request, and the guard against two accounts
        // claiming one subdomain. Filtered: null is "no subdomain", which most accounts are.
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.TenantUsername)
            .IsUnique()
            .HasFilter("\"TenantUsername\" IS NOT NULL");
        builder.Entity<BotKnownChat>()
            .HasIndex(c => c.TelegramChatId)
            .IsUnique();
        builder.Entity<BotKnownChatAdmin>()
            .HasIndex(a => new { a.BotKnownChatId, a.TelegramUserId })
            .IsUnique();
        builder.Entity<DraftTranslation>()
            .HasIndex(t => new { t.DraftId, t.Language })
            .IsUnique();
        // The owner filter is now the leading predicate on every query against these tables, so it
        // leads their indexes too — a key starting with DraftId cannot serve "this owner's rows".
        builder.Entity<DraftTranslation>().HasIndex(t => new { t.OwnerId, t.DraftId });
        builder.Entity<DraftRevision>().HasIndex(r => new { r.OwnerId, r.DraftId });
        builder.Entity<DraftStatSnapshot>().HasIndex(s => new { s.OwnerId, s.DraftId });
        builder.Entity<DraftTargetText>().HasIndex(t => new { t.OwnerId, t.DraftId });
        builder.Entity<Comment>().HasIndex(c => new { c.OwnerId, c.DraftId });
        builder.Entity<Reaction>().HasIndex(r => new { r.OwnerId, r.DraftId });
        builder.Entity<PollVote>().HasIndex(v => new { v.OwnerId, v.DraftId });
        builder.Entity<PostInvite>().HasIndex(i => new { i.OwnerId, i.DraftId });
        builder.Entity<PostRegistration>().HasIndex(r => new { r.OwnerId, r.DraftId });
        builder.Entity<ChannelPost>().HasIndex(p => new { p.OwnerId, p.ChannelId });
        builder.Entity<ChannelStatSnapshot>().HasIndex(s => new { s.OwnerId, s.ChannelId });
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
        // Every public page resolves its slug inside one owner's blog, so two owners may hold the
        // same slug and neither may hold it twice. Filtered because null means "not published" and
        // most rows are; the pair leads with OwnerId because every lookup does.
        builder.Entity<Project>()
            .HasIndex(p => new { p.OwnerId, p.ShowcaseSlug })
            .IsUnique()
            .HasFilter("\"ShowcaseSlug\" IS NOT NULL");
        builder.Entity<Draft>()
            .HasIndex(d => new { d.OwnerId, d.BlogSlug })
            .IsUnique()
            .HasFilter("\"BlogSlug\" IS NOT NULL");
        builder.Entity<Project>().Property(p => p.ShowcaseLinks).HasDefaultValue("");
        builder.Entity<Project>().Property(p => p.ShowcaseGallery).HasDefaultValue("");
        // T-300 — a host answers for one project across the whole installation, not per owner: two
        // accounts claiming one domain is one of them serving the other's page.
        builder.Entity<Project>()
            .HasIndex(p => p.CustomDomain)
            .IsUnique()
            .HasFilter("\"CustomDomain\" IS NOT NULL");
        // T-296 — the upsert key. Unique, so two views landing in the same second cannot split one
        // day into two rows the way RecordViewGeoAsync's index stops it from doing.
        builder.Entity<ShowcaseStatDaily>()
            .HasIndex(s => new { s.ProjectId, s.Day, s.Kind, s.Label })
            .IsUnique();
        // T-297 — one address follows a project once; the endpoint stores lowercase, so no
        // collation is needed for the index to mean that.
        builder.Entity<ShowcaseFollower>()
            .HasIndex(f => new { f.ProjectId, f.Email })
            .IsUnique();
        // Confirm and unsubscribe arrive as a token and nothing else — the reader has no account to
        // look the row up by.
        builder.Entity<ShowcaseFollower>().HasIndex(f => f.ConfirmToken);
        builder.Entity<ShowcaseFollower>().HasIndex(f => f.UnsubscribeToken).IsUnique();
        // Wave 1 item 7 — one address subscribes to one blog once; the endpoint stores lowercase,
        // so no collation is needed for the index to mean that. Same shape as ShowcaseFollower's.
        builder.Entity<BlogSubscriber>()
            .HasIndex(s => new { s.OwnerId, s.Email })
            .IsUnique();
        // Confirm and unsubscribe arrive as a token and nothing else — the reader has no account.
        builder.Entity<BlogSubscriber>().HasIndex(s => s.ConfirmToken);
        builder.Entity<BlogSubscriber>().HasIndex(s => s.UnsubscribeToken).IsUnique();
        // The runner's only question: what is waiting to be sent.
        builder.Entity<BlogNotifyJob>().HasIndex(j => new { j.Status, j.CreatedAt });
        // Wave 1 item 8 — the public preview page resolves by token alone, across owners. Filtered:
        // null means "no link", which is what every draft starts as.
        builder.Entity<Draft>()
            .HasIndex(d => d.PreviewToken)
            .IsUnique()
            .HasFilter("\"PreviewToken\" IS NOT NULL");
        // ADR-135 — one row per address; the endpoint stores lowercase, so the index can be plain.
        builder.Entity<WaitlistEntry>()
            .HasIndex(w => w.Email)
            .IsUnique();
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
        builder.Entity<DraftGlossaryExclusion>()
            .HasIndex(x => new { x.DraftId, x.GlossaryTermId, x.Language })
            .IsUnique();
        // ADR-238 — read from the term's side ("used in how many"), written from the draft's side
        // (one save rewrites that document's whole set), so both ends are indexed.
        builder.Entity<GlossaryTermUsage>().HasIndex(u => new { u.OwnerId, u.GlossaryTermId });
        builder.Entity<GlossaryTermUsage>().HasIndex(u => new { u.OwnerId, u.DraftId });
        // The project list query: this owner's projects, active ones first by their own order.
        builder.Entity<Project>().HasIndex(p => new { p.OwnerId, p.ArchivedAt });
        // "What is in this project" — the dashboard's only real question, and the one the drafts
        // list asks back when it shows which project a document belongs to.
        builder.Entity<Draft>().HasIndex(d => new { d.OwnerId, d.ProjectId });

        // T-301 — one invitation per address per project: a second invite is a resend, not a second
        // row.
        builder.Entity<ProjectMember>()
            .HasIndex(m => new { m.ProjectId, m.Email })
            .IsUnique();
        // And one row per person per project once accepted. Filtered because most invited rows have
        // no user yet, and SQLite treats NULLs as distinct anyway — the filter states the intent.
        builder.Entity<ProjectMember>()
            .HasIndex(m => new { m.ProjectId, m.MemberUserId })
            .IsUnique()
            .HasFilter("\"MemberUserId\" IS NOT NULL");
        // "Which projects am I in" — asked cross-tenant on every canvas request.
        builder.Entity<ProjectMember>().HasIndex(m => m.MemberUserId);
        // The invitation arrives as a token and nothing else, the way ShowcaseFollower's do.
        builder.Entity<ProjectMember>()
            .HasIndex(m => m.InviteToken)
            .IsUnique()
            .HasFilter("\"InviteToken\" IS NOT NULL");
        // EF ignores the property initialiser, as Draft.DocumentType found out the hard way.
        builder.Entity<ProjectMember>().Property(m => m.Role).HasDefaultValue(ProjectRoles.Editor);
        // T-358 — the same three indexes a ProjectMember carries, for the same three questions:
        // one invitation per address per team, one row per person per team, and "which teams am I
        // in", which is asked cross-tenant on every project request a non-owner makes.
        builder.Entity<TeamMember>()
            .HasIndex(m => new { m.TeamId, m.Email })
            .IsUnique();
        builder.Entity<TeamMember>()
            .HasIndex(m => new { m.TeamId, m.MemberUserId })
            .IsUnique()
            .HasFilter("\"MemberUserId\" IS NOT NULL");
        builder.Entity<TeamMember>().HasIndex(m => m.MemberUserId);
        builder.Entity<TeamMember>()
            .HasIndex(m => m.InviteToken)
            .IsUnique()
            .HasFilter("\"InviteToken\" IS NOT NULL");
        // EF ignores the property initialisers, as Draft.DocumentType found out the hard way.
        builder.Entity<TeamMember>().Property(m => m.Role).HasDefaultValue(ProjectRoles.Editor);
        builder.Entity<TeamMember>().Property(m => m.Status).HasDefaultValue(TeamMemberStatuses.Active);
        builder.Entity<Team>().HasIndex(t => t.OwnerId);
        // "Which projects does this team reach" — asked whenever a team's people are resolved.
        builder.Entity<Project>().HasIndex(p => p.TeamId);

        // Wave 2 item 10 — the fill job walks one owner's active slots; the list screen asks the same.
        builder.Entity<QueueSlot>().HasIndex(s => new { s.OwnerId, s.TargetId });
        // The occupancy check: is this slot occurrence already taken, whatever its status.
        builder.Entity<ScheduledPost>().HasIndex(p => new { p.SlotId, p.ScheduledAtUtc });
        // Wave 2 item 15 — a chat_member update names the link by its url and nothing else.
        builder.Entity<ChannelInviteLink>()
            .HasIndex(l => l.InviteLink)
            .IsUnique();
        builder.Entity<ChannelInviteLink>().HasIndex(l => new { l.OwnerId, l.ChannelId });
        // The upsert key: a join either finds today's (channel, link) bucket or creates it. SQLite
        // treats NULLs as distinct in a unique index, so the organic (null-link) rows are guarded
        // by the upsert reading before writing, same as BlogViewGeoDaily's callers do.
        builder.Entity<ChannelMemberDaily>()
            .HasIndex(d => new { d.OwnerId, d.ChannelId, d.Day, d.InviteLinkId })
            .IsUnique();
        // Wave 2 item 16 — the /l/{code} redirect resolves by code alone, across owners.
        builder.Entity<TrackedLink>()
            .HasIndex(l => l.Code)
            .IsUnique();
        builder.Entity<TrackedLink>().HasIndex(l => new { l.OwnerId, l.DraftId });
        // The click upsert key: one row per link per UTC day.
        builder.Entity<TrackedLinkClickDaily>()
            .HasIndex(d => new { d.TrackedLinkId, d.Day })
            .IsUnique();

        // The board list: one project's boards, most recently touched first.
        builder.Entity<CanvasBoard>().HasIndex(b => new { b.ProjectId, b.UpdatedAt });
        builder.Entity<CanvasBoard>().Property(b => b.Background).HasDefaultValue(CanvasBackgrounds.Grid);
        builder.Entity<CanvasBoard>().Property(b => b.Version).HasDefaultValue(1);
        // The only read a board ever does: everything on it, in render order.
        builder.Entity<CanvasItem>().HasIndex(i => new { i.BoardId, i.Z });
        // Project deletion and the account sweep both go by project, never by board.
        builder.Entity<CanvasItem>().HasIndex(i => i.ProjectId);
        builder.Entity<CanvasItem>().Property(i => i.Kind).HasDefaultValue(CanvasItemKinds.Note);
        builder.Entity<CanvasItem>().Property(i => i.Payload).HasDefaultValue("{}");
        builder.Entity<CanvasItem>().Property(i => i.Version).HasDefaultValue(1);
        // The dialogue tool's list screen: one project's scripts, most recently edited first.
        builder.Entity<DialogueScript>().HasIndex(s => new { s.ProjectId, s.UpdatedAt });
        // The xlsx import upserts by exactly this triple, and two texts for one (line, language)
        // would mean the sheet and the app disagree about what the translation is.
        builder.Entity<DialogueLineTranslation>()
            .HasIndex(t => new { t.DialogueScriptId, t.LineId, t.Language })
            .IsUnique();
    }

    /// <summary>
    /// Owner-scoped entities, and the reason each of the others is not one.
    ///
    /// Skipped entirely for a platform context, so the filter is absent from the query rather than
    /// disabled inside it — see <see cref="TenantModelCacheKeyFactory"/>.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder builder)
    {
        if (tenant.IsPlatform) return;

        builder.Entity<AiUsage>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Asset>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<AssetEntry>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<BlogNotifyJob>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<BlogStatSnapshot>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<BlogSubscriber>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<BlogViewGeoDaily>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Build>().HasQueryFilter(e => e.OwnerId == TenantId);
        // The canvas rows carry the project owner's id even when a member wrote them, so this
        // filter is also what forces cross-owner work to open the owner's scope explicitly
        // (ADR-217) instead of widening anything here.
        builder.Entity<CanvasBoard>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<CanvasItem>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Channel>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<CreditEntry>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DialogueLineTranslation>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DialogueScript>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DocumentLink>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Draft>().HasQueryFilter(e => e.OwnerId == TenantId);
        // T-191 — a user writes their own feedback under the tenant filter; admin reads every
        // entry under the platform scope, the same shape as every other owner-scoped table.
        builder.Entity<FeedbackEntry>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Preset>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DraftGlossaryExclusion>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DraftStatSeen>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<EntityLink>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Folder>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<FormPreset>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<GameTask>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<GlossaryTerm>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<GlossaryTermUsage>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Payment>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Project>().HasQueryFilter(e => e.OwnerId == TenantId);
        // OwnerId here is the project owner, not the invitee — a membership is the owner's row about
        // somebody else, so it filters like the rest of the project.
        builder.Entity<ProjectMember>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Team>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<TeamMember>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<PublishJob>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<PublishTarget>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<QueueSlot>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<ScheduledPost>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Series>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<ShowcaseFollower>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<ShowcaseStatDaily>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Sprint>().HasQueryFilter(e => e.OwnerId == TenantId);

        // Rows that belong to a draft or a channel rather than carrying an owner of their own.
        // Every endpoint reaches them by parent id, never through the parent's navigation, so a
        // filter on the parent would not have covered them.
        builder.Entity<ChannelInviteLink>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<ChannelMemberDaily>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<ChannelPost>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<ChannelStatSnapshot>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<TrackedLink>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<TrackedLinkClickDaily>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Comment>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DraftRevision>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DraftStatSnapshot>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DraftTargetText>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<DraftTranslation>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<PollVote>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<PostInvite>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<PostRegistration>().HasQueryFilter(e => e.OwnerId == TenantId);
        builder.Entity<Reaction>().HasQueryFilter(e => e.OwnerId == TenantId);

        // ApplicationUser carries no filter on purpose: Identity reads AspNetUsers on every
        // authorized request, and sign-in happens before anyone knows which tenant the request is
        // for. Filtering it would lock every account out of its own login.
        //
        // InviteCode, WaitlistEntry, AdminAuditEntry, BotKnownChat and BotKnownChatAdmin belong to
        // the platform, not to any tenant — one shared bot, one invite list, one audit log.
    }
}
