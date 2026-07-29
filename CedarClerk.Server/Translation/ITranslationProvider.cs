namespace CedarClerk.Server.Translation;

public record TranslationResult(string Title, string CedarJson);

public interface ITranslationProvider
{
    string Name { get; }

    Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct);
}

// ADR-060 — the narrow "translate a flat list of strings" capability form auto-translate needs:
// no TipTap document, no title, one translation per input at the same index (blanks pass through
// untranslated). Implemented by the providers whose wire model fits (Anthropic via ADR-059's
// chunk machinery, DeepL via its native batch); a provider without it gets a 501 from the
// form-translate endpoint rather than a forced whole-document round-trip.
public interface ITextsTranslationProvider
{
    Task<IReadOnlyList<string>> TranslateTextsAsync(IReadOnlyList<string> texts, string targetLanguage, CancellationToken ct);
}