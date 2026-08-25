namespace CedarClerk.Core;

public static class Consts
{
    public const string CurrentVersion = "0.15.0";
    public const string DataDirectoryKey = "CEDAR_DATA_DIR";
    public const string DbFileName = "cedar.db";

    // Must equal the auth ticket's own expiry — see Program.cs.
    public static readonly TimeSpan AuthCookieLifetime = TimeSpan.FromDays(30);
    
    public const string DataProtectionApplicationName = "CedarClerk.Server";
    
    // Prohibited subdomains to prevent users to use them
    public static string[] ReservedSubdomains =
    [
        "www", "app", "api", "admin", "mail", "blog", "docs", "status", "cdn",
        "static", "assets", "help", "support", "billing", "auth", "login",
        "smtp", "imap", "mx", "ftp", "ns1", "ns2", "dev", "staging", "test", "root"
    ];

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
        
        public const string CreditPackPrefix = "credits-";
        public const string CustomCreditsPrefix = "custom:";
    }

    public static class Signatures
    {
        public const string FreeAttributionText = "Published with Cedar Clerk";
    }

    public static class URLs
    {
        public const string MainHost = "https://cedarclerk.mooexe.dev";
        public const string BlogHost = "blog.mooexe.dev";

        // Tenant blogs live at <username>.cedarclerk.app (ADR-020).
        public const string TenantHost = "cedarclerk.app";
        public const string Localhost = "http://localhost:8080";
    }

    public static class PreDefinedCommands
    {
        public const string Start = "/start";
    }

    public static class Documents
    {
        public const int MaxTreeDepth = 10;
    }

    public static class General
    {
        // Not a secret — just enough to avoid storing raw visitor IPs directly.
        public const string VisitorHashSalt = "cedar-clerk-visitor-v1";
        
        public const string DisplayTimeZone = "America/Los_Angeles";
        public const string DisplayTimeZoneWindows = "Pacific Standard Time";
        public const string DisplayTimeZoneStandard = "PST";
        public const string DisplayTimeZoneDaylight = "PDT";

        public const string MainHostCfg = "Cedar:MainHost";
        public const string BlogHostCfg = "Cedar:BlogHost";
        public const string TenantHostCfg = "Cedar:TenantHost";

        // The TenantUsername of the account whose blog answers on the legacy blog host. Unset means
        // "the one admin account", which is what a single-tenant install already is.
        public const string BlogOwnerCfg = "Cedar:BlogOwner";
        public const string InviteCodeCfg = "Cedar:InviteCode";

        // Set ONLY by the desktop shell, which binds to 127.0.0.1 and serves one person on their own
        // machine. Without it a fresh desktop install cannot create its first account at all.
        public const string OpenRegistrationCfg = "Cedar:Registration:Open";

        // ADR-117 agent mode: the same executable stripped to reading this machine's disk — no
        // database, Identity, bot or SPA, only /agent/*. Set ONLY by the desktop shell; on a hosted
        // server it would be a filesystem-listing service with no business existing there.
        public const string AgentModeCfg = "Cedar:Agent:Enabled";

        // Shared secret the shell generates per launch. An unauthenticated loopback service that
        // enumerates folders is open to every process on the machine and to any page that can reach
        // 127.0.0.1; the agent has no Identity cookie to close it (ADR-117 decision 6).
        public const string AgentTokenCfg = "Cedar:Agent:Token";
        
        public const string ThumbBudgetCfg = "Cedar:AssetIndex:ThumbBudgetBytes";
        public const long ThumbBudgetDefaultBytes = 2L * 1024 * 1024 * 1024;
        
        public const string UrlsCfg = "Cedar:Urls";
        
        public const string AdminEmailCfg = "Cedar:AdminEmail";

        public const string ProviderKeyCfg = "Cedar:Translate:Provider";

        public const string ViewedCookiePrefix = "cedar_viewed_";
        
        public const string PrivateAccessCookiePrefix = "cedar_access_";
        
        public const string UnknownGeo = "??";
    }

    public static class FileSizes
    {
        public const long ImageMaxBytes = 50L * 1024 * 1024;                // 50MB
        public const long MediaMaxBytes = 1000L * 1024 * 1024;              // 1GB
        
        public const long TelegramSafeImageBytes = 4L * 1024 * 1024;        // 4MB
        public const long TelegramCompressSmallBytes = 2L * 1024 * 1024;    // 2MB
        public const long TelegramCompressHighBytes = 6L * 1024 * 1024;     // 6MD
    }

    public static class X
    {
        public const string ClientIdCfg = "Cedar:X:ClientId";
        public const string ClientSecretCfg = "Cedar:X:ClientSecret";
    }

    public static class Stripe
    {
        public const string SecretKeyCfg = "Cedar:Stripe:SecretKey";
        public const string WebhookSecretCfg = "Cedar:Stripe:WebhookSecret";
        public const string ProPriceIdCfg = "Cedar:Stripe:ProPriceId";
        public const string ProPlusPriceIdCfg = "Cedar:Stripe:ProPlusPriceId";
    }

    public static class PayPal
    {
        public const string SecretKeyCfg = "Cedar:PayPal:SecretKey";
        public const string ClientIdCfg = "Cedar:PayPal:ClientId";

        // Live or Sandbox.
        public const string ModeCfg = "Cedar:PayPal:Mode";
    }

    public static class Telegram
    {
        public const string BotTokenCfg = "Cedar:Telegram:BotToken";
        public const string ProStarsPriceCfg = "Cedar:Telegram:ProStarsPrice";
        public const string ProPlusStarsPriceCfg = "Cedar:Telegram:ProPlusStarsPrice";
        public const string TrialStarsPriceCfg = "Cedar:Telegram:TrialStarsPrice";

        public const int DefaultProStarsPrice = 150;        // ~ $3.00
        public const int DefaultProPlusStarsPrice = 250;    // ~ $5.00
        public const int DefaultTrialStarsPrice = 50;       // ~ $1.00

        // The editor's status bar carries its own copy inside a localized string ("6 / 32,768").
        public const int MaxPostChars = 32_768;

        // Far below MaxPostChars: past roughly this size the client collapses a channel post behind
        // "Show more" (6,412 chars collapsed, 1,787 did not — measured 01.08.2026). ADR-086.
        public const int ThreadPartChars = 3_000;

        // ADR-088 escape hatch: "url" flips the target back to URL delivery instead of uploading
        // bytes. Server config only, never author-facing.
        public const string MediaDeliveryCfg = "Cedar:Telegram:MediaDelivery";
        public const string MediaDeliveryUrl = "url";
    }

    public static class Anthropic
    {
        public const string ApiKeyCfg = "Cedar:Anthropic:ApiKey";
        public const string ModelCfg = "Cedar:Anthropic:Model";
        public const string DefaultModel = "claude-haiku-4-5";

        // The SDK's default retries can stack three 10-minute hangs into ~30 minutes of silence, so
        // both providers set MaxRetries = 0 and add their own bounded retry for the fast-failing
        // errors only. Nothing upstream holds a connection open since ADR-058-follow-up.
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(600);

        // Auto-translate is the slowest call in the app — a whole-document JSON round-trip — and 10
        // minutes was cutting it close (Marty, 29.07.2026). AI-edit stays on RequestTimeout. The
        // frontend's matching poll deadline is AUTO_TRANSLATE_TIMEOUT_MS in drafts.service.ts.
        public static readonly TimeSpan AutoTranslateTimeout = TimeSpan.FromMinutes(20);

        // ADR-059 — translation goes chunk by chunk, in parallel. The per-chunk timeout is short
        // because each call is small: a stuck one should fail fast into the bounded retry.
        // AutoTranslateTimeout above is still the outer ceiling across every chunk.
        public static readonly TimeSpan ChunkRequestTimeout = TimeSpan.FromMinutes(2);
        public const int TranslationChunkCharBudget = 6_000;
        public const int TranslationChunkMaxStrings = 150;
        public const int MaxParallelChunks = 4;

        // Haiku 4.5's own ceiling. At 16,000 a ~47,000-character document 502'd with "malformed
        // translation output" (29.07.2026): the response must hold the entire translated document as
        // one JSON object, and the output was cut off mid-JSON.
        public const int MaxOutputTokens = 64_000;
    }

    public static class Email
    {
        public const string ResendApiKeyCfg = "Cedar:Email:ResendApiKey";
        public const string FromAddressCfg = "Cedar:Email:FromAddress";
    }

    // Registration form shown to uninvited visitors of a private post (B3).
    public static class RegistrationForm
    {
        // Same "client owns the shape, server only bounds the size" treatment as the other JSON blobs.
        public const int FormJsonMaxChars = 16_000;
        public const int AnswersJsonMaxChars = 8_000;
        public const int FieldMaxLength = 200;

        // A public form that hands out access is an obvious flood target, and nothing else in the
        // blog endpoints is rate-limited.
        public const int MaxSubmissionsPerVisitor = 3;
        public static readonly TimeSpan SubmissionWindow = TimeSpan.FromHours(24);
    }

    // Fallbacks for the cross-links between a post's two homes (I15); overridable per author. The
    // blog one is language-dependent because the blog page is.
    public static class CrossLinks
    {
        public const string DefaultBlogLinkText = "Read on the blog →";
        public const string DefaultTelegramLinkTextEn = "View in Telegram &#8594;";
        public const string DefaultTelegramLinkTextRu = "Смотреть в Telegram &#8594;";
    }

    public static class Admin
    {
        // These are what the panel shows, not what is kept — nothing pages any of the three yet.
        public const int AuditPageSize = 100;
        public const int PostPageSize = 100;
        public const int PaymentPageSize = 100;

        // Short enough to type from a message, long enough not to be guessed off a public page.
        public const int MinInviteCodeLength = 6;
    }

    // The reader-facing headline, separate from the draft's own name. Far longer than the 64-char
    // draft name on purpose: a name finds a draft in a list, a title is a real headline.
    public static class ArticleTitle
    {
        public const int MaxLength = 200;
    }

    // Watermark tiled over a private post's blog page (I7). Tight cap: long text tiles into mush.
    public static class Watermark
    {
        public const int MaxLength = 60;
    }

    public static class DraftActivity
    {
        // How long the owner has to be away before the next /drafts load counts as a new session and
        // rolls the DraftStatSeen baseline forward (B23).
        public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);
    }
}
