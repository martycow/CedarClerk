using System.Text.Json;

// The namespace follows the module rather than the folder: everything the canvas hub touches is
// part of the indie-dev module, and its callers — the endpoints, the tests — name it there.
namespace CedarClerk.Server.Modules.IndieDev;

public sealed record CanvasBoardSummaryDto(
    Guid Id, Guid ProjectId, string Name, string Background, int ItemCount,
    DateTime CreatedAt, DateTime UpdatedAt, int Version, bool CanWrite);

public sealed record CanvasItemDto(
    Guid Id, Guid BoardId, string Kind,
    double X, double Y, double Width, double Height, double Rotation, int Z,
    string Color, JsonElement Payload,
    string CreatedBy, DateTime CreatedAt, string UpdatedBy, DateTime UpdatedAt, int Version);

public sealed record PresenceDto(string ConnectionId, string UserId, string Name, int ColorIndex, string Role);

/// <summary>
/// What a board looks like on arrival, from the hub's <c>Join</c> and from the REST read alike:
/// one shape, so a page that opened over HTTP and a page that re-joined after a reconnect
/// reconcile against the same thing. <c>Peers</c> is empty over REST — presence only exists for a
/// live connection.
/// </summary>
public sealed record CanvasSnapshot(
    CanvasBoardSummaryDto Board, string ProjectName, CanvasItemDto[] Items,
    string Role, bool CanWrite, PresenceDto[] Peers);

public sealed record CanvasItemInput(
    Guid Id, string Kind, double X, double Y, double Width, double Height,
    double Rotation, string? Color, JsonElement Payload);

public sealed record CanvasItemPatch(
    Guid Id, double? X, double? Y, double? Width, double? Height,
    double? Rotation, string? Color, JsonElement? Payload);

public sealed record CanvasGeometry(Guid Id, double X, double Y, double Width, double Height, double Rotation);

public static class CanvasMapping
{
    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement.Clone();

    public static CanvasBoardSummaryDto Describe(CanvasBoard board, int itemCount, bool canWrite) =>
        new(board.Id, board.ProjectId, board.Name, board.Background, itemCount,
            board.CreatedAt, board.UpdatedAt, board.Version, canWrite);

    public static CanvasItemDto Describe(CanvasItem item) =>
        new(item.Id, item.BoardId, item.Kind,
            item.X, item.Y, item.Width, item.Height, item.Rotation, item.Z,
            item.Color, ParsePayload(item.Payload),
            item.CreatedByUserId, item.CreatedAt, item.UpdatedByUserId, item.UpdatedAt, item.Version);

    /// <summary>
    /// The column holds JSON text and the wire carries an object — the client would otherwise parse
    /// a string on every render. Cloned off its document on purpose: a <see cref="JsonElement"/>
    /// outliving the <see cref="JsonDocument"/> it was read from points at freed memory.
    /// </summary>
    private static JsonElement ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return EmptyPayload;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyPayload;
        }
    }
}
