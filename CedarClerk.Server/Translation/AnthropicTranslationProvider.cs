using Anthropic;
using Anthropic.Models.Messages;

namespace CedarClerk.Server.Translation;

public class AnthropicTranslationProvider(string apiKey, string model) : ITranslationProvider
{
    public string Name => "anthropic";

    public async Task<TranslationResult> TranslateAsync(string title, string cedarJson, string targetLanguage, CancellationToken ct)
    {
        var client = new AnthropicClient
        {
            ApiKey = apiKey
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