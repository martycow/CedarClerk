using CedarClerk.Core;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CedarClerk.Server;

/// <summary>
/// What <c>dotnet ef</c> builds a context with. Needed since the context gained a required
/// <see cref="TenantProvider"/>: the tools construct it outside DI and cannot supply one.
///
/// Platform, so the model the tools compare against carries no query filters. Filters do not
/// reach the schema either way, but a migration generated from a filtered model would be one more
/// thing to reason about for no gain.
/// </summary>
public sealed class CedarDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CedarDbContext>
{
    public CedarDbContext CreateDbContext(string[] args)
    {
        var dataDir = Environment.GetEnvironmentVariable(Consts.DataDirectoryKey)
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

        var options = new DbContextOptionsBuilder<CedarDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dataDir, Consts.DbFileName)}")
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, TenantModelCacheKeyFactory>()
            .Options;

        return new CedarDbContext(options, TenantProvider.Platform());
    }
}
