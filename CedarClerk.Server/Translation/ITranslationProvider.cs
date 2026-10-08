namespace CedarClerk.Server.Translation;

public record TranslationResult(string Title, string CedarJson);

public interface ITranslationProvider
{
    string Name { get; }

    Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct);

    /// <summary>
    /// T-013 — not every provider covers every content language: DeepL has no Belarusian or
    /// Georgian, while the LLM providers translate all of them. Checked before the AI quota is
    /// charged, so an unsupported language costs a clear error rather than a call.
    /// </summary>
    bool SupportsTargetLanguage(string code) => true;
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

public record AiImage(string MediaType, byte[] Bytes);

public record GlossaryTermSource(string Name, string Description);

public record GlossaryTermTranslation(string Name, IReadOnlyList<string> Spellings, string Description);

// ADR-320 — the two glossary calls an LLM can make and a plain translation API cannot: a term
// translated together with the word forms its new language inflects it into, and a description
// written from the term and, when there is one, its picture. A provider without it gets a 501.
public interface IGlossaryAiProvider
{
    /// <summary>One translation per input at the same index.</summary>
    Task<IReadOnlyList<GlossaryTermTranslation>> TranslateTermsAsync(
        IReadOnlyList<GlossaryTermSource> terms, string targetLanguage, CancellationToken ct);

    Task<string> DescribeTermAsync(string term, string language, AiImage? image, CancellationToken ct);
}
