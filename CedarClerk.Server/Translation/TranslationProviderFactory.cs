using CedarClerk.Core;

namespace CedarClerk.Server.Translation;

public static class TranslationProviderFactory
{
    public static ITranslationProvider? Create(IConfiguration cfg, IHttpClientFactory httpFactory)
    {
        var provider = cfg[Consts.General.ProviderKeyCfg]?.Trim().ToLowerInvariant();
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
            default:
                throw new TranslationException($"Unknown translation provider '{provider}'.");
        }
    }
}
