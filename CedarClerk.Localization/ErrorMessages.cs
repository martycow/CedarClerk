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

    public static string AiDailyLimitReached(int limit) => $"Daily AI limit ({limit} calls) reached — resets at midnight UTC.";
}