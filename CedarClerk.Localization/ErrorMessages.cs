namespace CedarClerk.Localization;

public static class ErrorMessages
{
    public const string DraftNotFound = "Draft not found.";
    public const string InvalidToken = "Invalid token.";
    public const string BotNotRunning = "Telegram bot is not running.";
    public const string LinkYouTelegram = "Link your Telegram account first.";
    public const string TelegramBillingNotConfigured = "Telegram Stars billing is not configured!";
    public const string PaypalNotConfigured = "PayPal is not wired up yet.";
    public const string AutoTranslateProPlus = "Auto-translate is a Pro Plus feature. Upgrade to use it.";
    public const string AutoTranslateNoProvider = "Auto-translate is not available with the configured provider";

    public const string CreateLanguageBeforePrimary = "Create this language version before making it primary.";
    // ADR-065 — the publish guard's answer to "you confirmed a diff of something else".
    public const string PublishConfirmationStale = "This post changed after the update was previewed — review the changes and confirm again.";

    // T-018.1 / T-018.3 — the two ways a save is refused rather than silently applied.
    public const string SaveShrinkNeedsConfirmation = "This save would delete most of the text — confirm that it's intentional.";
    public const string SaveConflict = "This version was edited elsewhere after you loaded it — reload before saving.";

    // T-013 — named provider and language, because the fix is switching one or picking the other.
    public static string LanguageNotSupportedByProvider(string lang, string provider) =>
        $"The configured translation provider ({provider}) cannot translate into {lang.ToUpperInvariant()}.";

    public static string AiDailyLimitReached(int limit) => $"Daily AI limit ({limit} calls) reached — resets at midnight UTC.";
    public static string LanguageIsPrimary(string lang) => $"{lang.ToUpperInvariant()} is this draft's primary language — edit it on the main tab.";
    public static string NoVersionInLanguage(string lang) => $"No {lang.ToUpperInvariant()} version of this draft";
}