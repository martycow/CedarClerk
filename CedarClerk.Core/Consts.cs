namespace CedarClerk.Core;

public static class Consts
{
    public const string Url = "https://cedarclerk.mooexe.dev";
    public const string DefaultBlogHost = "blog.mooexe.dev";
    public const string Localhost = "http://localhost:8080";
    
    public const string CurrentVersion = "0.7.0";
    public const string DataDirectoryKey = "CEDAR_DATA_DIR";
    public const string DbFileName = "cedar.db";
    
    public const int PasswordMinLength = 8;

    // Not a secret — just enough to avoid storing raw visitor IPs directly.
    public const string VisitorHashSalt = "cedar-clerk-visitor-v1";
    
    public const string Html = "Html";
    public const string Markdown = "Markdown";
    
    public const string InviteCodeCfg = "Cedar:InviteCode";
    public const string BlogHostCfg = "Cedar:BlogHost";
    public const string PublicBaseUrlCfg = "Cedar:PublicBaseUrl";
    
    public const string ProviderKeyCfg = "Cedar:Translate:Provider";
    
    public static class PreDefinedCommands
    {
        public const string Start = "/start";
    }
    
    public static class Stripe
    {
        public const string SecretKeyCfg = "Cedar:Stripe:SecretKey";
        public const string WebhookSecretCfg = "Cedar:Stripe:WebhookSecret";
        public const string ProPriceIdCfg = "Cedar:Stripe:ProPriceId";
        public const string ProPlusPriceIdCfg = "Cedar:Stripe:ProPlusPriceId";
        public const string ProPlusTrialPriceIdCfg = "Cedar:Stripe:ProPlusTrialPriceId";
    }

    public static class PayPal
    {
        public const string SecretKeyCfg = "Cedar:PayPal:SecretKey";
        public const string ClientIdCfg = "Cedar:PayPal:ClientId";
    }

    public static class Telegram
    {
        public const string BotTokenCfg = "Cedar:Telegram:BotToken";
        public const string ProStarsPriceCfg = "Cedar:Telegram:ProStarsPrice";
    }

    public static class Anthropic
    {
        public const string ApiKeyCfg = "Cedar:Anthropic:ApiKey";
        public const string ModelCfg = "Cedar:Anthropic:Model";
        public const string DefaultModel = "claude-opus-4-8";
    }

    public static class OpenAi
    {
        public const string ApiKeyCfg = "Cedar:OpenAi:ApiKey";
        public const string ModelCfg = "Cedar:OpenAi:Model";
        public const string DefaultModel = "gpt-4o";
    }

    public static class DeepL
    {
        public const string ApiKeyCfg = "Cedar:DeepL:ApiKey";
    }
}