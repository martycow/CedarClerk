using CedarClerk.Server.Tenancy;
using Quartz;

namespace CedarClerk.Server.Bot;

/// <summary>
/// The age half of the autosave ceiling (ADR-065): drops every <c>save</c> revision older than
/// <see cref="DraftRevisionService.MaxSaveRevisionAge"/>. The per-save pruning only touches a
/// document somebody is still editing, so without this a document left alone keeps its full
/// copies forever. Publication and restore revisions are never touched.
/// </summary>
[DisallowConcurrentExecution]
public class PruneSaveRevisionsJob(CedarDbContext db, TenantProvider tenant, ILogger<PruneSaveRevisionsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // Runs on a timer, not in a request: there is no signed-in user, and the work is over
        // every owner's rows at once.
        tenant.UsePlatform();

        var removed = await DraftRevisionService.PruneExpiredSavesAsync(db, DateTime.UtcNow, context.CancellationToken);
        if (removed > 0)
            logger.LogInformation("Pruned {Count} autosave revisions older than {Days} days",
                removed, DraftRevisionService.MaxSaveRevisionAge.TotalDays);
    }
}
