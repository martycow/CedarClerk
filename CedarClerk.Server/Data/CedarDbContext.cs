using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Data;

public class CedarDbContext(DbContextOptions<CedarDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ScheduledPost> ScheduledPosts => Set<ScheduledPost>();
}