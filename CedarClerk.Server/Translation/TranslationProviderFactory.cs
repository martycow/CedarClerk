namespace CedarClerk.Server.Translation;

public static class TranslationProviderFactory
{
    public static ITranslationProvider? Create(IConfiguration cfg, IHttpClientFactory httpFactory)
    {
        var provider = cfg[Consts.ProviderKeyCfg]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(provider)) return null;

        switch (provider)
        {
            case "anthropic":
            {
                var key = cfg[Consts.AnthropicApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new TranslationException($"{Consts.AnthropicApiKeyCfg} is not set");
                
                var model = cfg[Consts.AnthropicModelCfg] ?? Consts.DefaultClaudeModel;
                return new AnthropicTranslationProvider(key, model);
            }
            case "openai":
            {
                var key = cfg[Consts.OpenAiApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new TranslationException($"{Consts.OpenAiApiKeyCfg} is not set");
                
                var model = cfg[Consts.OpenAiModelCfg] ?? Consts.DefaultOpenAiModel;
                return new OpenAiTranslationProvider(httpFactory, key, model);
            }
            case "deepl":
            {
                var key = cfg[Consts.DeepLApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new TranslationException($"{Consts.DeepLApiKeyCfg} is not set");
                
                return new DeepLTranslationProvider(httpFactory, key);
            }
            default:
                throw new TranslationException($"Unknown translation provider '{provider}'.");
        }
    }
}
