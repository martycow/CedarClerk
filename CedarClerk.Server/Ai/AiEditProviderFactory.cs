using CedarClerk.Core;

namespace CedarClerk.Server.Ai;

public static class AiEditProviderFactory
{
    public static IAiEditProvider? Create(IConfiguration cfg, IHttpClientFactory httpFactory)
    {
        var provider = cfg[Consts.General.ProviderKeyCfg]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(provider)) 
            return null;

        switch (provider)
        {
            case "anthropic":
            {
                var key = cfg[Consts.Anthropic.ApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new AiEditException($"{Consts.Anthropic.ApiKeyCfg} is not set");

                var model = cfg[Consts.Anthropic.ModelCfg] ?? Consts.Anthropic.DefaultModel;
                return new AnthropicAiEditProvider(key, model);
            }
            case "openai":
            {
                var key = cfg[Consts.OpenAi.ApiKeyCfg];
                if (string.IsNullOrEmpty(key))
                    throw new AiEditException($"{Consts.OpenAi.ApiKeyCfg} is not set");

                var model = cfg[Consts.OpenAi.ModelCfg] ?? Consts.OpenAi.DefaultModel;
                return new OpenAiAiEditProvider(httpFactory, key, model);
            }
            case "deepl":
                throw new AiEditException("DeepL can't perform free-form AI edits — configure Cedar:Translate:Provider as anthropic or openai to use this feature");
            default:
                throw new AiEditException($"Unknown translation provider '{provider}'.");
        }
    }
}
