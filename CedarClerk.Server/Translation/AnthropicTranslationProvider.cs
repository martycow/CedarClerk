using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models;
using Anthropic.Models.Messages;
using CedarClerk.Core;

namespace CedarClerk.Server.Translation;

// ADR-059 (docs/DECISIONS.md) — extracts text via TipTapTextNodes, translates it in parallel
// chunks, and splices the result back into the untouched original document (same pattern
// DeepLTranslationProvider already used). The model never sees or reproduces TipTap JSON
// structure, which is what used to make large documents slow and prone to truncated/malformed
// output. OpenAiTranslationProvider intentionally still uses the old whole-document approach.
public class AnthropicTranslationProvider(string apiKey, string model) : ITranslationProvider, ITextsTranslationProvider
{
    public string Name => "anthropic";

    // Bounded retry for Anthropic's own transient capacity signals only (28.07.2026 — a real
    // ~360-line document 502'd with "overloaded" on the first and only attempt). Now scoped
    // per-chunk instead of per-document, so a retry only costs one chunk's tokens, not the whole
    // document's. MaxRetries=0 on the client below is deliberate and stays — this is a narrow,
    // separate retry for exactly OverloadedError/RateLimitError, both of which Anthropic returns
    // fast (not after hanging).
    private const int MaxAttempts = 3;

    public async Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct)
    {
        List<string> texts;
        try
        {
            texts = TipTapTextNodes.ExtractTexts(cedarJson);
        }
        catch (Exception ex)
        {
            throw new TranslationException("Draft document is not valid JSON", ex);
        }

        var all = new List<string> { title };
        all.AddRange(texts);

        var translatedAll = await TranslateTextsAsync(all, targetLanguage, ct);
        var translatedTitle = translatedAll[0];
        var translatedJson = TipTapTextNodes.ReplaceTexts(cedarJson, translatedAll.Skip(1).ToList());
        return new TranslationResult(translatedTitle, translatedJson);
    }

    public async Task<IReadOnlyList<string>> TranslateTextsAsync(IReadOnlyList<string> texts, string targetLanguage, CancellationToken ct)
    {
        var chunks = ChunkByBudget(texts.ToList(), Consts.Anthropic.TranslationChunkCharBudget, Consts.Anthropic.TranslationChunkMaxStrings);
        var results = new List<string>[chunks.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, chunks.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Consts.Anthropic.MaxParallelChunks, CancellationToken = ct },
            async (idx, token) => { results[idx] = await TranslateChunkAsync(chunks[idx], targetLanguage, token); });

        return results.SelectMany(r => r).ToList();
    }

    private async Task<List<string>> TranslateChunkAsync(List<string> chunk, string targetLanguage, CancellationToken ct)
    {
        // Blank/whitespace-only strings need no translation and are excluded from the API call
        // entirely — one less thing that could come back mismatched, and one less thing to pay for.
        var nonBlankIndices = new List<int>();
        var nonBlankTexts = new List<string>();
        for (var i = 0; i < chunk.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(chunk[i]))
            {
                nonBlankIndices.Add(i);
                nonBlankTexts.Add(chunk[i]);
            }
        }

        var result = new List<string>(chunk);
        if (nonBlankTexts.Count == 0)
            return result;

        // A fresh client per chunk — chunks translate concurrently and the SDK's own
        // thread-safety under concurrent calls on one shared client instance isn't documented;
        // a client is cheap configuration, not a pooled connection, so this sidesteps the question.
        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            Timeout = Consts.Anthropic.ChunkRequestTimeout,
            MaxRetries = 0,
        };

        var prompt = TranslationChunkPromptGenerator.Build(nonBlankTexts, targetLanguage);

        List<string> translated;
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await client.Messages.Create(new MessageCreateParams
                {
                    Model = model,
                    MaxTokens = Consts.Anthropic.MaxOutputTokens,
                    Messages = [new() { Role = Role.User, Content = prompt }],
                }, cancellationToken: ct);

                var text = string.Concat(response.Content
                    .Select(b => b.Value)
                    .OfType<TextBlock>()
                    .Select(b => b.Text));

                translated = TranslationChunkPromptGenerator.ParseResult(text, nonBlankTexts.Count);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller (job hard-timeout or disconnected client) cancelled — let it propagate as-is
            }
            catch (OperationCanceledException)
            {
                throw new TranslationException($"Anthropic didn't respond within {Consts.Anthropic.ChunkRequestTimeout.TotalSeconds:0}s — try again");
            }
            catch (AnthropicServiceException ex) when (attempt < MaxAttempts
                && ex.ErrorType is ErrorType.OverloadedError or ErrorType.RateLimitError)
            {
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch (TranslationException) when (attempt < MaxAttempts)
            {
                // Malformed/mismatched output (e.g. the model split or dropped one entry) — retry
                // the same chunk rather than failing the whole translation over what's usually a
                // one-off slip, same instinct as the overload retry above.
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch (TranslationException)
            {
                throw; // out of attempts — surface the specific parse/mismatch error as-is
            }
            catch (Exception ex)
            {
                throw new TranslationException($"Anthropic API request failed: {ex.Message}", ex);
            }
        }

        for (var i = 0; i < nonBlankIndices.Count; i++)
            result[nonBlankIndices[i]] = translated[i];

        return result;
    }

    // Groups items into chunks that never split a single string across two chunks — a chunk closes
    // once adding the next item would exceed charBudget or maxCount, whichever comes first. An
    // oversized single string just becomes its own one-item chunk rather than being split, since
    // splitting would break the 1:1 index mapping TipTapTextNodes.ReplaceTexts relies on.
    public static List<List<string>> ChunkByBudget(List<string> items, int charBudget, int maxCount)
    {
        var chunks = new List<List<string>>();
        var current = new List<string>();
        var currentChars = 0;

        foreach (var item in items)
        {
            if (current.Count > 0 && (currentChars + item.Length > charBudget || current.Count >= maxCount))
            {
                chunks.Add(current);
                current = [];
                currentChars = 0;
            }

            current.Add(item);
            currentChars += item.Length;
        }

        if (current.Count > 0)
            chunks.Add(current);

        return chunks;
    }
}
