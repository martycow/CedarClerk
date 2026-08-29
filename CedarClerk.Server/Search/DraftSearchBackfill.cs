using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server.Search;

/// <summary>
/// One-shot startup backfill: an installation that predates the index (or lost it) gets every
/// draft indexed once, and an installation whose index has rows is left alone. Never blocks or
/// fails startup — a blog without search is degraded, a blog that will not boot is down.
/// </summary>
public sealed class DraftSearchBackfill(IServiceScopeFactory scopes, ILogger<DraftSearchBackfill> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreatePlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
            var index = scope.ServiceProvider.GetRequiredService<IDraftSearchIndex>();

            await DraftSearchSchema.EnsureAsync(db, ct);
            var indexed = await db.Database.SqlQuery<long>($"SELECT count(*) AS Value FROM DraftSearch").FirstAsync(ct);
            if (indexed > 0) return;

            var ids = await db.Drafts.Select(d => d.Id).ToListAsync(ct);
            if (ids.Count == 0) return;

            foreach (var id in ids)
                await index.ReindexDraftAsync(id, ct);
            logger.LogInformation("DraftSearch backfill indexed {Count} draft(s)", ids.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DraftSearch backfill failed — search stays empty until drafts are saved again");
        }
    }
}
