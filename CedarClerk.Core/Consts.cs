namespace CedarClerk.Core;

public static class Consts
{
    public const string CurrentVersion = "0.9.4";
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

    public static class Signatures
    {
        // Free tier always gets this fixed, non-removable attribution instead of a custom
        // PostSignature — the "upgrade to customize/remove it" hook for Pro. See Phase 8 Step 5,
        // docs/ROADMAP.md.
        public const string FreeAttributionText = "Published with Cedar Clerk";
    }

    public static class URLs
    {
        public const string MainHost = "https://cedarclerk.mooexe.dev";
        public const string BlogHost = "blog.mooexe.dev";
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

        // The account granted admin rights on startup (IF2). Config-driven on purpose: the first
        // admin can't be made through the admin panel, and this works on a fresh database or a
        // restored backup without hand-editing SQL on the Pi.
        public const string AdminEmailCfg = "Cedar:AdminEmail";

        public const string ProviderKeyCfg = "Cedar:Translate:Provider";

        public const string ViewedCookiePrefix = "cedar_viewed_";

        // Grants access to a private post once a valid invite token has been presented — much
        // longer-lived than ViewedCookiePrefix, which is a same-visit view-count dedup, not an
        // access grant. See the ADR following ADR-040, docs/DECISIONS.md.
        public const string PrivateAccessCookiePrefix = "cedar_access_";
    }

    public static class FileSizes
    {
        public const long ImageMaxBytes = 50L * 1024 * 1024;
        public const long MediaMaxBytes = 1000L * 1024 * 1024;

        // Telegram rejects a photo fetched by URL above this size with a misleading "wrong type
        // of the web page content" error — confirmed empirically (19.07.2026) against
        // @testingandfun: a 9.88MB JPEG already failed, a 0.94MB one succeeded. Kept well under
        // the ~10MB ballpark documented for Telegram's own remote-fetch photo limit as a safety
        // margin. See the ADR in docs/DECISIONS.md.
        public const long TelegramSafeImageBytes = 4L * 1024 * 1024;

        // User-selectable compression degree for the export modal (see the ADR following
        // ADR-031) — "standard" is TelegramSafeImageBytes above; these are the other two presets.
        // "high" (6MB) trades some of the safety margin for quality — still comfortably under the
        // ~9.88MB point our empirical test showed Telegram already rejecting, but closer to it.
        public const long TelegramCompressSmallBytes = 2L * 1024 * 1024;
        public const long TelegramCompressHighBytes = 6L * 1024 * 1024;
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

        // The SDK's own per-call default (10 min, 2 retries) can leave a request hanging for
        // ~30 minutes with zero feedback before it ever surfaces an error. Bound it much tighter
        // so a stuck call fails fast with a clear message instead of looking frozen.
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
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

    public static class Email
    {
        public const string ResendApiKeyCfg = "Cedar:Email:ResendApiKey";
        public const string FromAddressCfg = "Cedar:Email:FromAddress";
    }

    // Registration form shown to uninvited visitors of a private post (B3).
    public static class RegistrationForm
    {
        // Matches AuthEndpoints' PreferenceJsonMaxChars — same "client owns the shape, server
        // only bounds the size" treatment as the other JSON preference blobs.
        public const int FormJsonMaxChars = 16_000;
        public const int AnswersJsonMaxChars = 8_000;
        public const int FieldMaxLength = 200;

        // Per-post, per-visitor submission cap. A public form that hands out access is an
        // obvious flood target, and nothing else in the blog endpoints is rate-limited.
        public const int MaxSubmissionsPerVisitor = 3;
        public static readonly TimeSpan SubmissionWindow = TimeSpan.FromHours(24);
    }

    // Admin panel (IF2).
    public static class Admin
    {
        // The audit log grows without bound and nothing pages it yet — this is what the panel
        // shows, not what is kept.
        public const int AuditPageSize = 100;

        // Short enough to type from a message, long enough not to be guessed off a public page.
        public const int MinInviteCodeLength = 6;

        // Both lists are newest-first and unpaged; these are what the panel shows, not what exists.
        public const int PostPageSize = 100;
        public const int PaymentPageSize = 100;
    }

    // Watermark tiled over a private post's blog page (I7).
    public static class Watermark
    {
        // A watermark is a short label ("CONFIDENTIAL", a reader's name) repeated across the
        // page — long text tiles into unreadable mush, so the cap is deliberately tight.
        public const int MaxLength = 60;
    }

    // View/reaction deltas on the /drafts screen (B23).
    public static class DraftActivity
    {
        // How long the owner has to be away before the next /drafts load counts as a new
        // session and rolls the DraftStatSeen baseline forward.
        public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);
    }
}