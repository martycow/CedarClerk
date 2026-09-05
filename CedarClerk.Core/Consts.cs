namespace CedarClerk.Core;

public static class Consts
{
    // 0.18/0.19 were skipped by decision (31.08.2026): the open-beta sprint is publicly "v0.2.0"
    // and 0.20.x is the number that both reads as it and still sorts after 0.17.x everywhere
    // versions are compared (deploy, tags, self-update).
    //
    // Bump this before every deploy. Two consecutive builds once both called themselves 0.20.0,
    // so the health check's version match proved nothing and the swap had to be confirmed by an
    // endpoint's status code instead.
    public const string CurrentVersion = "0.21.0";
    public const string DataDirectoryKey = "CEDAR_DATA_DIR";
    public const string DbFileName = "cedar.db";

    // Must equal the auth ticket's own expiry — see Program.cs.
    public static readonly TimeSpan AuthCookieLifetime = TimeSpan.FromDays(30);
    
    public const string DataProtectionApplicationName = "CedarClerk.Server";
    
    // Prohibited subdomains to prevent users to use them
    public static readonly string[] ReservedSubdomains =
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
        public const string MainHost = "https://cedarclerk.app";
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
        public const string TenantHostCfg = "Cedar:TenantHost";

        // A blog to show a stranger on the landing page, as a full host. Unset hides the link
        // rather than guessing an account.
        public const string ShowcaseBlogCfg = "Cedar:ShowcaseBlog";

        // The TenantUsername of the account whose blog answers on the legacy blog host. Unset means
        // "the one admin account", which is what a single-tenant install already is.
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

        // Consent for Analytics.Events, one cookie for both surfaces: the landing is server-rendered
        // and the app is the SPA, so a value the server can read is the only thing both can agree on.
        public const string ConsentCookie = "cedar_consent";
        public const string ConsentGranted = "granted";
        public const string ConsentDenied = "denied";
        
        public const string PrivateAccessCookiePrefix = "cedar_access_";
        
        public const string UnknownGeo = "??";
    }

    /// <summary>
    /// Signing in with somebody else's account (T-003, ADR-237). Telegram is not here: it is not
    /// OAuth, it carries no email, and it is already verified by <see cref="TelegramLoginVerifier"/>.
    /// </summary>
    public static class ExternalAuth
    {
        public const string GoogleClientIdCfg = "Cedar:Auth:Google:ClientId";
        public const string GoogleClientSecretCfg = "Cedar:Auth:Google:ClientSecret";

        /// <summary>The Identity login-provider name; also what the frontend puts in the URL.</summary>
        public const string Google = "Google";

        /// <summary>
        /// Where the callback sends somebody it could not sign in, with the reason. Both are SPA
        /// routes: the server never renders a page here, it only decides which one applies.
        /// </summary>
        public const string CompleteRoute = "/auth/complete";
        public const string LoginRoute = "/login";

        /// <summary>
        /// The state the two screens above read. `link` means an account already holds that address
        /// and the password is what proves it is the same person (ADR-237 clause 3).
        /// </summary>
        public const string OutcomeLink = "link";
        public const string OutcomeNew = "new";
        public const string OutcomeFailed = "failed";
    }

    /// <summary>
    /// The event dictionary of <c>docs/product/METRICS.md</c>, as code. ADR-126 makes the names a
    /// contract the provider only transports, so they live here rather than as literals at each call
    /// site — a renamed string in one of eight places is a metric that quietly stops adding up.
    /// </summary>
    public static class Analytics
    {
        public const string EnabledCfg = "Cedar:Analytics:Enabled";
        public const string ProjectKeyCfg = "Cedar:Analytics:ProjectKey";
        public const string HostCfg = "Cedar:Analytics:Host";

        // EU cloud, not the US default: the account is EU and /privacy names the region.
        public const string DefaultHost = "https://eu.i.posthog.com";

        public static class Events
        {
            public const string SignupStarted = "signup_started";
            public const string SignupCompleted = "signup_completed";
            public const string DraftCreated = "draft_created";
            public const string PostPublished = "post_published";
            public const string PostPublishedFirst = "post_published_first";
            public const string TrialStarted = "trial_started";
            public const string PlanPurchased = "plan_purchased";
            public const string PlanRenewed = "plan_renewed";
            public const string CreditsPurchased = "credits_purchased";
            public const string AiUsed = "ai_used";
        }
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

        // T-359 (audit finding 6b) — how long a cached "this Telegram user administers this chat"
        // row is trusted for. The cache only refreshes when the BOT's own membership changes, so a
        // person demoted in between keeps a grant nothing revokes; the TTL turns that into a
        // bounded one. Refresh re-verifies live and re-stamps, which is the way back in.
        public static readonly TimeSpan KnownChatAdminTtl = TimeSpan.FromDays(7);
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

    // The public game page (T-159, ADR-134; extended by ADR-216).
    public static class Showcase
    {
        public const string MediaPrefix = "/media/";
        public const int GalleryMaxImages = 12;
        public const int GalleryMaxLength = 2000;
        public const int TrailerUrlMaxLength = 300;
        public const int CustomDomainMaxLength = 120;
        public const int DownloadUrlMaxLength = 500;

        // A public form that writes a row and sends a mail, on a page with no account behind it.
        public const int MaxFollowsPerVisitor = 5;
        public static readonly TimeSpan FollowWindow = TimeSpan.FromHours(24);

        public const string ViewedCookiePrefix = "cedar_game_viewed_";
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
        // T-337 - how many un-scanned documents one listing reads a cover out of. A bound, not a
        // budget: after a few listings there is nothing left to scan, and this only keeps the first
        // one after deploy from loading every document body an account owns.
        public const int CoverScanBatch = 50;

        // How long the owner has to be away before the next /drafts load counts as a new session and
        // rolls the DraftStatSeen baseline forward (B23).
        public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);
    }

    // The reference board and the people who share it (T-301, ADR-217/218).
    // T-358 — teams. The invitation half reuses Canvas.InviteTokenBytes: one token shape for every
    // invitation the product sends, so there is one thing to reason about when it is a credential.
    public static class Teams
    {
        public const int NameMax = 60;
        public const int StatusNoteMax = 200;

        public const int TeamsPerOwner = 10;
        public const int MembersPerTeam = 50;
    }

    public static class Canvas
    {
        public const int BoardsPerProject = 20;
        public const int ItemsPerBoard = 2000;
        public const int BoardNameMax = 80;

        // One item's JSON payload. The client owns the shape, the server only bounds the size — the
        // same treatment RegistrationForm's blobs get.
        public const int PayloadMaxChars = 4000;
        public const int NoteTextMax = 2000;

        public const int MembersPerProject = 10;

        // base64url, as ShowcaseFollower's tokens are: the invitation link is the credential, so it
        // has to be unguessable on its own.
        public const int InviteTokenBytes = 32;

        // A peer's colour is hash(userId) % this, so it is stable per person with nothing stored.
        // Equal to the client's --avatar-1..6 palette: a larger modulus wraps two indices onto one
        // swatch, which is the collision the index exists to prevent.
        public const int PresenceColors = 6;
    }
}

// What a non-owner may do on a project (T-301, ADR-217). Strings, like TaskStatuses: a new role
// should be a constant and a UI, not a migration. The owner is not a role — they have no
// ProjectMember row at all, because a role column that could say "owner" is one edit away from
// handing the project over.
public static class ProjectRoles
{
    public const string Editor = "editor";
    public const string Viewer = "viewer";

    public static readonly IReadOnlyList<string> All = [Editor, Viewer];

    public static bool IsKnown(string? role) => role is not null && All.Contains(role);

    public static bool CanWrite(string? role) => role == Editor;
}

// What can sit on a board (T-301, ADR-218). The kind decides the payload's shape and nothing else;
// it is fixed at creation, so an item never has to be reinterpreted.
public static class CanvasItemKinds
{
    public const string Image = "image";
    public const string Note = "note";
    public const string Frame = "frame";
    public const string Link = "link";

    public static readonly IReadOnlyList<string> All = [Frame, Image, Link, Note];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

// What a board is drawn on. Purely cosmetic, and per board rather than per viewer — it is part of
// what the board looks like to everyone in it.
public static class CanvasBackgrounds
{
    public const string Grid = "grid";
    public const string Dots = "dots";
    public const string Blank = "blank";

    public static readonly IReadOnlyList<string> All = [Blank, Dots, Grid];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}
