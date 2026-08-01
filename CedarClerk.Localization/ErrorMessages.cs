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
/// **T-050 is closed (01.08.2026)**: the ~60 inline English literals that used to live in the
/// endpoint files are all here now, and `ErrorMessageLocalizationTests` fails the build if a new
/// one is written — verified to actually go red, not merely to exist.
/// </summary>
public static class ErrorMessages
{
    public static string DraftNotFound => Ru("Черновик не найден.", "Draft not found.");
    public static string InvalidToken => Ru("Недействительный токен.", "Invalid token.");
    // T-050, second half (01.08.2026): the ~60 messages that used to live as inline English
    // literals in the endpoint files. Moved verbatim in meaning — this is a translation, not a
    // rewording, so a message someone already recognises stays recognisable.
    public static string DescriptionRequired => Ru("Нужно описание.", "A description is required");
    public static string HandleAndAppPasswordRequired => Ru("Нужны хэндл и app-пароль.", "A handle and an app password are required");
    public static string TermRequired => Ru("Нужен термин.", "A term is required");
    public static string AiEditProPlus => Ru("Правка через ИИ доступна на Pro Plus — перейдите на этот план.", "AI editing is a Pro Plus feature. Upgrade to use it.");
    public static string AiEditNotConfigured => Ru("Правка через ИИ не настроена.", "AI editing is not configured");
    public static string AppearancePrefsTooLarge => Ru("Настройки оформления слишком большие.", "Appearance preferences are too large");
    public static string AutoTranslateNotConfigured => Ru("Авто-перевод не настроен.", "Auto-translate is not configured");
    public static string AvatarMustBeUploaded => Ru("Аватар должен быть загруженным изображением.", "Avatar must be an uploaded image");
    public static string BothTagsRequired => Ru("Нужны и старый, и новый тег.", "Both the old and the new tag are required");
    public static string InvalidEmail => Ru("Введите корректный адрес почты.", "Enter a valid email address");
    public static string ImportFileNotFound => Ru("Файл не найден в каталоге import-tmp.", "File not found in import-tmp directory.");
    public static string FormTooLarge => Ru("Форма слишком большая.", "Form is too large");
    public static string InvalidDocumentStructure => Ru("Некорректная структура документа.", "Invalid document structure.");
    public static string InvalidFileName => Ru("Некорректное имя файла.", "Invalid file name.");
    public static string InvalidInviteCode => Ru("Неверный инвайт-код.", "Invalid invite code");
    public static string InvalidTelegramSignature => Ru("Подпись входа через Telegram недействительна или устарела.", "Invalid or expired Telegram login signature");
    public static string NewDraftDefaultsTooLarge => Ru("Настройки нового черновика слишком большие.", "New-draft defaults are too large");
    public static string NoMarkdownInZip => Ru("Внутри архива нет ни одного .md-файла.", "No .md file found inside the zip.");
    public static string NoStripeSubscription => Ru("На этом аккаунте нет подписки Stripe.", "No Stripe subscription on this account");
    public static string ChannelNotFoundOrNoAccess => Ru("Канал не найден или нет доступа к нему.", "No TG-channel was found or no access to that channel");
    public static string NoAccountWithEmail => Ru("Аккаунта с такой почтой нет.", "No account with that email.");
    public static string NoSuchInviteCode => Ru("Такого инвайт-кода нет.", "No such invite code");
    public static string NoTermsInLanguage => Ru("На этом языке терминов нет.", "No terms in this language");
    public static string NothingToTranslate => Ru("Нечего переводить — сначала напишите тексты на исходном языке.", "Nothing to translate — write the texts in the source language first");
    public static string DestinationNotConnected => Ru("Одно из назначений не подключено.", "One of those destinations is not connected");
    public static string PickADestination => Ru("Выберите хотя бы одно назначение.", "Pick at least one destination");
    public static string PickALanguage => Ru("Выберите хотя бы один язык для перевода.", "Pick at least one language to translate into");
    public static string SignatureIsPro => Ru("Подпись под постом доступна на Pro — перейдите на этот план.", "Post signature is a Pro feature. Upgrade to use it.");
    public static string PresetHasNoForm => Ru("У пресета нет формы.", "Preset has no form");
    public static string PublishToBlogFirst => Ru("Сначала опубликуйте черновик в блоге.", "Publish this draft to the blog first");
    public static string RegistrationFormTooLarge => Ru("Форма регистрации слишком большая.", "Registration form is too large");
    public static string SameSourceAndTarget => Ru("Исходный и целевой языки совпадают.", "Source and target language are the same");
    public static string TagRequired => Ru("Нужен тег.", "Tag is required");
    public static string BotNotRunningNoToken => Ru("Telegram-бот не запущен (не настроен токен).", "Telegram bot is not running (no token configured)");
    public static string SlugHasNoUsableCharacters => Ru("В этом адресе нет пригодных символов.", "That URL has no usable characters");
    public static string SlugTaken => Ru("Такой адрес уже занят.", "That URL is already taken");
    public static string InviteCodeExists => Ru("Такой код уже существует.", "That code already exists");
    public static string NameReservedForAuthor => Ru("Это имя зарезервировано за автором поста.", "That name is reserved for the post's author.");
    public static string NotAZipArchive => Ru("Файл не является корректным .zip-архивом.", "The file is not a valid .zip archive.");
    public static string FormHasNoText => Ru("В форме пока нет текста для перевода.", "The form has no text to translate yet");
    public static string FormAlreadyInLanguage => Ru("Форма уже написана на этом языке.", "The form is already written in this language");
    public static string ServiceMustBeHttps => Ru("Адрес сервиса должен быть https://-ссылкой.", "The service address must be an https:// URL");
    public static string TermAlreadyInLanguage => Ru("Термин уже есть на этом языке.", "The term is already in this language");
    public static string ThirdSlotIsPro => Ru("Третий слот шапки доступен на Pro — перейдите на этот план.", "The third header slot is a Pro feature. Upgrade to use it.");
    public static string TranslationUnusable => Ru("Перевод вернулся непригодным — попробуйте ещё раз.", "The translation came back unusable — try again");
    public static string TelegramAlreadyLinked => Ru("Этот Telegram-аккаунт уже привязан к другому аккаунту Cedar Clerk.", "This Telegram account is already linked to another Cedar Clerk account");
    public static string ToolbarLayoutTooLarge => Ru("Раскладка панели слишком большая.", "Toolbar layout is too large");
    public static string TrialAlreadyUsed => Ru("Пробный период уже использован на этом аккаунте.", "Trial has already been used on this account");
    public static string UnsupportedChatType => Ru("Неподдерживаемый тип чата.", "Unsupported chat type");
    public static string UnsupportedUiLanguage => Ru("Неподдерживаемый язык интерфейса.", "Unsupported interface language");
    public static string WatermarkTooLong => Ru("Текст водяного знака слишком длинный.", "Watermark text is too long");
    public static string CannotChangeOwnAdmin => Ru("Нельзя менять собственные права администратора.", "You cannot change your own admin rights");
    public static string CannotLockOwnAccount => Ru("Нельзя заблокировать собственный аккаунт.", "You cannot lock your own account");

    public static string StripePlanNotConfigured => Ru("Stripe не настроен для этого плана — см. docs/integrations-setup.md", "Stripe is not configured for this plan — see docs/integrations-setup.md");
    public static string StripeNotConfigured => Ru("Stripe не настроен — см. docs/integrations-setup.md", "Stripe is not configured — see docs/integrations-setup.md");
    public static string PayPalAuthFailed => Ru("PayPal не принял ключи — проверьте ClientId/Secret и Cedar:PayPal:Mode (live или sandbox)", "PayPal auth failed — check ClientId/Secret (and Cedar:PayPal:Mode: live vs sandbox)");
    public static string PayPalNoApprovalLink => Ru("PayPal не вернул ссылку на подтверждение.", "PayPal did not return an approval link");
    public static string BlueskyCredentialsRefused => Ru("Bluesky не принял эти данные — проверьте хэндл и используйте app-пароль, а не пароль от аккаунта.", "Bluesky refused those credentials — check the handle and use an app password, not your account password");
    public static string DraftNotFoundPlain => Ru("Черновик не найден.", "Draft not found");
    public static string ScheduleOnlyToOwnChannels => Ru("Планировать можно только в подключённые каналы — сначала подключите канал (окно «Каналы»).", "You can only schedule posts to your connected channels — connect this channel first (Channels popup)");
    /// <summary>T-106 — an earlier part of the thread did not go out, so this one must not either.</summary>
    public static string ThreadPartAbandoned(int index) => Ru(
        $"Предыдущая часть треда не ушла — часть {index + 1} не отправлена, чтобы в канале не осталось дыры.",
        $"An earlier part of the thread did not go out — part {index + 1} was held back rather than leaving a gap.");

    public static string UnknownNetwork(string network) => Ru(
        $"Неизвестная сеть: {network}", $"Unknown network: {network}");

    public static string DraftIsEmpty => Ru("Черновик пуст.", "Draft is empty.");

    /// <summary>T-106 — the document shrank between parts, so the part being sent no longer exists.</summary>
    public static string ThreadPartGone => Ru(
        "Документ изменился во время публикации треда — часть больше не существует. Опубликуйте заново.",
        "The document changed while the thread was being published — this part no longer exists. Publish again.");

    public static string BotNotRunning => Ru("Telegram-бот не запущен.", "Telegram bot is not running.");

    // T-089 — the one Bluesky failure an author can act on: the stored app password no longer opens
    // a session (revoked in Bluesky's settings, or unreadable because the DataProtection key ring
    // and the database were separated — see PublishTargetSecrets).
    /// <summary>
    /// A document the network would refuse, known before sending (T-086). The issue list travels
    /// beside this so the client can say WHICH limit — this line only says whose verdict it is.
    /// </summary>
    public static string PublishWontFit(string network) => Ru(
        $"Пост не помещается в ограничения сети {network} — исправьте отмеченное и попробуйте снова.",
        $"This post does not fit {network}'s limits — fix what is listed and try again.");

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
