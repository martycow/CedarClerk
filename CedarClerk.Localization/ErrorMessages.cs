using System.Globalization;

namespace CedarClerk.Localization;

/// <summary>
/// T-050 — server-side messages, in the reader's language.
///
/// The language comes from <see cref="CultureInfo.CurrentUICulture"/>, which the server sets per
/// request from the signed-in account's <c>UiLanguage</c> (falling back to Accept-Language). That
/// is deliberate: .NET already flows CurrentUICulture across await boundaries, so **no call site
/// has to pass a language** — every existing `ErrorMessages.X` reference keeps working and simply
/// starts answering in Russian for a Russian UI.
///
/// Members are properties, not consts: a const is baked into the caller at compile time and could
/// never be language-dependent.
///
/// Anything not listed here is still English — the per-endpoint inline strings are the remaining
/// half of T-050 (see `docs/BACKLOG.md`).
/// </summary>
public static class ErrorMessages
{
    public static string DraftNotFound => Ru("Черновик не найден.", "Draft not found.");
    public static string InvalidToken => Ru("Недействительный токен.", "Invalid token.");
    public static string BotNotRunning => Ru("Telegram-бот не запущен.", "Telegram bot is not running.");

    // T-089 — the one Bluesky failure an author can act on: the stored app password no longer opens
    // a session (revoked in Bluesky's settings, or unreadable because the DataProtection key ring
    // and the database were separated — see PublishTargetSecrets).
    public static string BlueskyReconnect => Ru(
        "Не удалось войти в Bluesky — переподключите аккаунт в настройках.",
        "Could not sign in to Bluesky — reconnect the account in settings.");
    public static string LinkYouTelegram => Ru("Сначала привяжите аккаунт Telegram.", "Link your Telegram account first.");
    public static string TelegramBillingNotConfigured =>
        Ru("Оплата через Telegram Stars не настроена!", "Telegram Stars billing is not configured!");
    public static string PaypalNotConfigured => Ru("PayPal ещё не подключён.", "PayPal is not wired up yet.");
    public static string AutoTranslateProPlus =>
        Ru("Автоперевод доступен на тарифе Pro Plus.", "Auto-translate is a Pro Plus feature. Upgrade to use it.");
    public static string AutoTranslateNoProvider =>
        Ru("Настроенный провайдер не умеет автоперевод.", "Auto-translate is not available with the configured provider");

    public static string CreateLanguageBeforePrimary =>
        Ru("Сначала создайте версию на этом языке, потом делайте её основной.",
           "Create this language version before making it primary.");

    // ADR-065 — the publish guard's answer to "you confirmed a diff of something else".
    public static string PublishConfirmationStale =>
        Ru("Пост изменился после того, как обновление было показано — посмотрите изменения и подтвердите заново.",
           "This post changed after the update was previewed — review the changes and confirm again.");

    // T-018.1 / T-018.3 — the two ways a save is refused rather than silently applied.
    public static string SaveShrinkNeedsConfirmation =>
        Ru("Это сохранение удалит почти весь текст — подтвердите, что так и задумано.",
           "This save would delete most of the text — confirm that it's intentional.");
    public static string SaveConflict =>
        Ru("Эту версию изменили в другом месте после того, как вы её открыли — перезагрузите перед сохранением.",
           "This version was edited elsewhere after you loaded it — reload before saving.");

    // T-013 — named provider and language, because the fix is switching one or picking the other.
    public static string LanguageNotSupportedByProvider(string lang, string provider) =>
        Ru($"Настроенный провайдер перевода ({provider}) не умеет переводить на {lang.ToUpperInvariant()}.",
           $"The configured translation provider ({provider}) cannot translate into {lang.ToUpperInvariant()}.");

    public static string AiDailyLimitReached(int limit) =>
        Ru($"Дневной лимит AI ({limit} вызовов) исчерпан — обнулится в полночь UTC.",
           $"Daily AI limit ({limit} calls) reached — resets at midnight UTC.");

    public static string LanguageIsPrimary(string lang) =>
        Ru($"{lang.ToUpperInvariant()} — основной язык этого черновика, правьте его на главной вкладке.",
           $"{lang.ToUpperInvariant()} is this draft's primary language — edit it on the main tab.");

    public static string NoVersionInLanguage(string lang) =>
        Ru($"Версии {lang.ToUpperInvariant()} у этого черновика нет",
           $"No {lang.ToUpperInvariant()} version of this draft");

    // Russian is the only translated locale for now, matching the app's own UI dictionaries
    // (en.ts/ru.ts): every other UI language already falls back to English there, and shipping
    // machine-quality German error text would be a worse answer than the English original.
    private static string Ru(string russian, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? russian : english;
}
