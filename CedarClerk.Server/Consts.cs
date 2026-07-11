namespace CedarClerk.Server;

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
    
    public const string BotTokenCfg = "Cedar:BotToken";
    public const string InviteCodeCfg = "Cedar:InviteCode";
    public const string BlogHostCfg = "Cedar:BlogHost";
    public const string PublicBaseUrlCfg = "Cedar:PublicBaseUrl";
    public const string StripeSecretKeyCfg = "Cedar:Stripe:SecretKey";
    public const string StripeWebhookSecretCfg = "Cedar:Stripe:WebhookSecret";
    public const string StripePriceIdCfg = "Cedar:Stripe:PriceId";
    public const string StarsAmountCfg = "Cedar:Billing:StarsAmount";
    public const string ProviderKeyCfg = "Cedar:Translate:Provider";
    public const string AnthropicApiKeyCfg = "Cedar:Translate:AnthropicApiKey";
    public const string AnthropicModelCfg = "Cedar:Translate:AnthropicModel";
    public const string OpenAiApiKeyCfg = "Cedar:Translate:OpenAiApiKey";
    public const string OpenAiModelCfg = "Cedar:Translate:OpenAiModel";
    public const string DeepLApiKeyCfg = "Cedar:Translate:DeepLApi";

    public const string DefaultClaudeModel = "claude-opus-4-8";
    public const string DefaultOpenAiModel = "gpt-4o";
    
    public const string Html = "Html";
    public const string Markdown = "Markdown";

    public static class PreDefinedCommands
    {
        public const string Start = "/start";
    }
}