namespace CedarClerk.Core;

public static class Consts
{
    public const string CurrentVersion = "0.7.0";
    public const string DataDirectoryKey = "CEDAR_DATA_DIR";
    public const string DbFileName = "cedar.db";
    
    public static class ContentTypes
    {
        public const string PlainText = "PlainText";
        public const string Html = "Html";
        public const string Markdown = "Markdown";
    }

    public static class Plans
    {
        public const string Free = "free";
        
        public const string Pro = "pro";
        public const int ProPrice = 3;
        
        public const string ProPlus = "proplus";
        public const int ProPlusPrice = 6;
        
        public const string Trial = "trial";
        public const int TrialPrice = 1;
    }

    public static class URLs
    {
        public const string MainHost = "https://cedarclerk.mooexe.dev";
        public const string BlogHost = "https://blog.mooexe.dev";
        public const string Localhost = "http://localhost:8080";
    }

    public static class PreDefinedCommands
    {
        public const string Start = "/start";
    }

    public static class General
    {
        // Not a secret — just enough to avoid storing raw visitor IPs directly.
        public const string VisitorHashSalt = "cedar-clerk-visitor-v1";
        
        public const string MainHostCfg = "Cedar:MainHost";
        public const string BlogHostCfg = "Cedar:BlogHost";
        public const string InviteCodeCfg = "Cedar:InviteCode";
        public const string ProviderKeyCfg = "Cedar:Translate:Provider";
    }

    public static class FileSizes
    {
        public const long ImageMaxBytes = 5 * 1024 * 1024;
        public const long MediaMaxBytes = 20 * 1024 * 1024;
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

        /// <summary>
        /// Live or Sandbox (for testing)
        /// </summary>
        public const string ModeCfg = "Cedar:PayPal:Mode";
    }

    public static class Telegram
    {
        public const string BotTokenCfg = "Cedar:Telegram:BotToken";
        public const string ProStarsPriceCfg = "Cedar:Telegram:ProStarsPrice";
        public const string ProPlusStarsPriceCfg = "Cedar:Telegram:ProPlusStarsPrice";
        public const string TrialStarsPriceCfg = "Cedar:Telegram:TrialStarsPrice";

        public const int DefaultProStarsPrice = 150; // ~ $3.00
        public const int DefaultProPlusStarsPrice = 250; // ~ $5.00
        public const int DefaultTrialStarsPrice = 50; // ~ $1.00
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