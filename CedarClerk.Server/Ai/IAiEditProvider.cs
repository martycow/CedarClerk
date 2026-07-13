namespace CedarClerk.Server.Ai;

public enum AiEditKind { FixErrors, Schizo }

public record AiEditResult(string Title, string CedarJson);

public interface IAiEditProvider
{
    string Name { get; }

    Task<AiEditResult> EditAsync(string title, string cedarJson, AiEditKind kind, CancellationToken ct);
}
