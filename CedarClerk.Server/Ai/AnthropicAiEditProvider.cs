using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models;
using Anthropic.Models.Messages;
using CedarClerk.Core;

namespace CedarClerk.Server.Ai;

public class AnthropicAiEditProvider(string apiKey, string model) : IAiEditProvider
{
    public string Name => "anthropic";

    // Same bounded retry as AnthropicTranslationProvider — see its comment for the full reasoning
    // (28.07.2026, a real document 502'd with "overloaded" on the first and only attempt; this
    // provider has the identical MaxRetries=0 gap).
    private const int MaxAttempts = 3;

    public async Task<AiEditResult> EditAsync(string title, string cedarJson, AiEditKind kind, CancellationToken ct)
    {
        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            // The SDK default (10 min, 2 retries) can leave the request looking hung for ~30
            // minutes; fail fast instead so the caller gets a clear error, not a frozen spinner.
            Timeout = Consts.Anthropic.RequestTimeout,
            MaxRetries = 0,
        };

        var prompt = AiEditPromptGenerator.Build(title, cedarJson, kind);

        Message response;
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                response = await client.Messages.Create(new MessageCreateParams
                {
                    Model = model,
                    MaxTokens = Consts.Anthropic.MaxOutputTokens,
                    // See AnthropicTranslationProvider.cs's comment — no Thinking/OutputConfig at
                    // all, not just turned down: adaptive thinking isn't supported on every model
                    // (a clean 400 from Anthropic confirmed this the moment the configured model
                    // changed to Haiku 4.5), and a grammar-fix/rewrite pass never needed it anyway.
                    Messages = [new() { Role = Role.User, Content = prompt }],
                }, cancellationToken: ct);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller (e.g. disconnected client) cancelled — not our timeout, let it propagate as-is
            }
            catch (OperationCanceledException)
            {
                throw new AiEditException($"Anthropic didn't respond within {Consts.Anthropic.RequestTimeout.TotalSeconds:0}s — try again");
            }
            catch (AnthropicServiceException ex) when (attempt < MaxAttempts
                && ex.ErrorType is ErrorType.OverloadedError or ErrorType.RateLimitError)
            {
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch (Exception ex)
            {
                throw new AiEditException($"Anthropic API request failed: {ex.Message}", ex);
            }
        }

        var text = string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text));

        return AiEditPromptGenerator.ParseResult(text);
    }
}
