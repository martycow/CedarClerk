using Anthropic;
using Anthropic.Models.Messages;
using CedarClerk.Core;

namespace CedarClerk.Server.Ai;

public class AnthropicAiEditProvider(string apiKey, string model) : IAiEditProvider
{
    public string Name => "anthropic";

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
            throw new AiEditException($"Anthropic didn't respond within {Consts.Anthropic.RequestTimeout.TotalSeconds:0}s — try again");
        }
        catch (Exception ex)
        {
            throw new AiEditException($"Anthropic API request failed: {ex.Message}", ex);
        }

        var text = string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text));

        return AiEditPromptGenerator.ParseResult(text);
    }
}
