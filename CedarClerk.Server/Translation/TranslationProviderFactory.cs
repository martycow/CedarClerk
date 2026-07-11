using CedarClerk.Core;

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
                var key = cfg[Consts.Anthropic.ApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new TranslationException($"{Consts.Anthropic.ApiKeyCfg} is not set");
                
                var model = cfg[Consts.Anthropic.ModelCfg] ?? Consts.Anthropic.DefaultModel;
                return new AnthropicTranslationProvider(key, model);
            }
            case "openai":
            {
                var key = cfg[Consts.OpenAi.ApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new TranslationException($"{Consts.OpenAi.ApiKeyCfg} is not set");
                
                var model = cfg[Consts.OpenAi.ApiKeyCfg] ?? Consts.OpenAi.DefaultModel;
                return new OpenAiTranslationProvider(httpFactory, key, model);
            }
            case "deepl":
            {
                var key = cfg[Consts.DeepL.ApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new TranslationException($"{Consts.DeepL.ApiKeyCfg} is not set");
                
                return new DeepLTranslationProvider(httpFactory, key);
            }
            default:
                throw new TranslationException($"Unknown translation provider '{provider}'.");
        }
    }
}
