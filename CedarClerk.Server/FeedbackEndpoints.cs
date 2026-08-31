using System.Security.Claims;
using CedarClerk.Localization;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Server;

// T-191 (ADR-232) — the user-facing half of the feedback channel: a signed-in account submits a
// bug report / idea / other, stored owner-scoped so triage has a name attached. Admin reads and
// marks handled under /api/admin/feedback (AdminEndpoints).
public static class FeedbackEndpoints
{
    public record SubmitFeedbackRequest(string? Kind, string? Message, string? Path);

    private static readonly HashSet<string> Kinds = ["bug", "idea", "other"];
    private const int MaxMessageLength = 4000;

    public static void MapFeedbackEndpoints(this WebApplication app)
    {
        app.MapPost("/api/feedback", async (SubmitFeedbackRequest req, ClaimsPrincipal user, CedarDbContext db) =>
        {
            var message = req.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return Results.BadRequest(new { error = ErrorMessages.FeedbackEmpty });
            if (message.Length > MaxMessageLength)
                return Results.BadRequest(new { error = ErrorMessages.FeedbackTooLong });

            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            db.FeedbackEntries.Add(new FeedbackEntry
            {
                OwnerId = uid,
                Kind = req.Kind is { } k && Kinds.Contains(k) ? k : "other",
                Message = message,
                // A path is context for a bug report; capped so a pasted URL cannot bloat the row.
                Path = req.Path is { Length: > 0 and <= 200 } p ? p : null,
            });
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization();
    }
}
