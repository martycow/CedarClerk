using Anthropic;
using Anthropic.Models.Messages;

namespace CedarClerk.Server.Ai;

public class AnthropicAiEditProvider(string apiKey, string model) : IAiEditProvider
{
    public string Name => "anthropic";

    public async Task<AiEditResult> EditAsync(string title, string cedarJson, AiEditKind kind, CancellationToken ct)
    {
        var client = new AnthropicClient
        {
            ApiKey = apiKey
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
