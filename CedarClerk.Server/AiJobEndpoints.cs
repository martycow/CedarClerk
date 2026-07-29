using System.Security.Claims;

namespace CedarClerk.Server;

// See AiJobService's own comment for the "why" — jobs started by DraftEndpoints' auto-translate/
// ai-edit are polled here, not draft-scoped, since a job id already carries its own owner check.
public static class AiJobEndpoints
{
    public static void MapAiJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ai-jobs").RequireAuthorization();

        group.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, AiJobService jobs) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var job = jobs.Get(id, uid);
            if (job is null) return Results.NotFound();

            return Results.Ok(new
            {
                status = job.Status.ToString().ToLowerInvariant(),
                result = job.Status == AiJobStatus.Completed ? job.Result : null,
                error = job.Status == AiJobStatus.Failed ? job.Error : null,
            });
        });

        // User-initiated cancel (the frontend's cancelAutoTranslate/cancelAiEdit) — cancels the
        // underlying Anthropic call via the job's own CancellationTokenSource.
        group.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal user, AiJobService jobs) =>
        {
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return jobs.Cancel(id, uid) ? Results.NoContent() : Results.NotFound();
        });
    }
}
