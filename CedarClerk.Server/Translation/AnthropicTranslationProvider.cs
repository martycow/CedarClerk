using Anthropic;
using Anthropic.Models.Messages;
using CedarClerk.Core;

namespace CedarClerk.Server.Translation;

public class AnthropicTranslationProvider(string apiKey, string model) : ITranslationProvider
{
    public string Name => "anthropic";

    public async Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct)
    {
        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            // The SDK default (10 min, 2 retries) can leave the request looking hung for ~30
            // minutes; fail fast instead so the caller gets a clear error, not a frozen spinner.
            Timeout = Consts.Anthropic.RequestTimeout,
            MaxRetries = 0,
        };

        var prompt = TranslationPromptGenerator.Build(title, cedarJson, targetLanguage);

        Message response;
        try
        {
            response = await client.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = 16000,
                Thinking = new ThinkingConfigAdaptive(),
                Messages = [new() { Role = Role.User, Content = prompt }],
            }, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller (e.g. disconnected client) cancelled — not our timeout, let it propagate as-is
        }
        catch (OperationCanceledException)
        {
            throw new TranslationException($"Anthropic didn't respond within {Consts.Anthropic.RequestTimeout.TotalSeconds:0}s — try again");
        }
        catch (Exception ex)
        {
            throw new TranslationException($"Anthropic API request failed: {ex.Message}", ex);
        }

        var text = string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text));

        return TranslationPromptGenerator.ParseResult(text);
    }
}