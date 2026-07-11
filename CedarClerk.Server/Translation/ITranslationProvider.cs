namespace CedarClerk.Server.Translation;

public record TranslationResult(string Title, string CedarJson);

public interface ITranslationProvider
{
    string Name { get; }

    Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct);
}