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
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<BotKnownChat> BotKnownChats => Set<BotKnownChat>();
    public DbSet<BotKnownChatAdmin> BotKnownChatAdmins => Set<BotKnownChatAdmin>();
    public DbSet<DraftTranslation> DraftTranslations => Set<DraftTranslation>();
    public DbSet<Payment> Payments => Set<Payment>();

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
    }
}