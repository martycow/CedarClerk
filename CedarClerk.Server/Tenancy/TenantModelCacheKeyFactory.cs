using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CedarClerk.Server.Tenancy;

/// <summary>
/// Lets a tenant context and a platform context compile to two different models, so the filter is
/// either in the query or absent from it.
///
/// The alternative — one model whose filter reads <c>platform || OwnerId == id</c> — parameterises
/// the flag, and SQLite plans <c>WHERE @p0 OR "OwnerId" = @p1</c> as a table scan: every owner
/// index in the schema stops being used, on every query, to express a condition that is constant
/// for the life of the context. Two cached models cost one extra compile.
/// </summary>
public sealed class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is CedarDbContext cedar
            ? (context.GetType(), cedar.IsPlatformModel, designTime)
            : (context.GetType(), false, designTime);
}
